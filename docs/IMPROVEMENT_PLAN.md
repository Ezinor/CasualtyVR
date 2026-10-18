# IMPROVEMENT_PLAN — review after 1.2.0 (2026-10-17)

A code review of CasualtyVR 1.2.0 (commit `380fc1a`), checked against the updated VR Modding Playbook.
Pattern IDs (`XR-004`) and failure IDs (`FAIL-INPUT-025`) refer to the playbook.

Evidence grades follow the playbook: **SOURCE** means it was read in this repository's code. **INFERENCE** means
it is likely but has not been measured or seen on this target. No item has been run yet. Each item names the
check that would confirm it.

## Summary

| # | Item | Kind | Grade | Size |
|---|---|---|---|---|
| 1 | Held inputs survive frames that do not render | Bug | SOURCE | S |
| 2 | A lost session or exited runtime is never handled | Bug | SOURCE | S–M |
| 3 | Native teardown runs off the render thread | Latent bug | SOURCE | M |
| 4 | A dropout in position tracking blanks the whole frame | Bug / comfort | SOURCE | S |
| 5 | Recenter ignores heading, and runtime recenter is not followed | Bug / comfort | SOURCE | S |
| 6 | One exception kills the VR frame; no game-version guard | Robustness | SOURCE | S |
| 7 | Periodic scans line up into a hitch every 1.33 s | Perf | SOURCE + INFERENCE | M |
| 8 | Frame pacing: skipped waits, competing frame cap | Perf | SOURCE + INFERENCE | S |
| 9 | Possible first-person judder from 50 Hz physics | Perf / comfort | INFERENCE | S–M |
| 10 | Spatial audio follows the flat camera, not your head | First person | INFERENCE | M |
| 11 | The head can pass into blocks; no fade | First person | SOURCE | S |
| 12 | The world resizes once after spawn in first person | First person | SOURCE | S |
| 13 | Depth layer, eye resolution scale | Feature | — | M |
| 14 | Hardware coverage: Index, Vive, WMR, G2, Proton, KrokMP | Release | — | — |
| 15 | Native log is lost on a crash or hang | Diagnostics | SOURCE | S |
| 16 | Code health, tests, docs, CI, playbook | Housekeeping | — | S–M |

**Status (2026-10-17):**

- **Group A is implemented and passes the headless checks.** See [LIFECYCLE_RECEIPT.md](LIFECYCLE_RECEIPT.md):
  `tools/vr-lifecycle.ps1` 35/35, xr-tape 19/19, fp-smoke 11/11.
- A headset pass is still due. Item 4's orientation-only path cannot be driven by xr-sim.
- Item 4 holds the last position rather than decaying it: the poses are absolute every frame, so nothing
  outlives a dropout.
- Not started: item 8's measurement half and groups B–E.

Suggested order:

- **A:** 1, 2, 4, 5, 6, 8 (frame cap), 15. Small, high value, and all can be verified headless under xr-sim.
- **B:** 7, 8 (histogram), 9. Measure first, then fix.
- **C:** 3. The bridge's thread ownership.
- **D:** 10, 11, 12. First-person immersion. Needs headset passes.
- **E:** 13, 14, 16.

---

## Fixes

### 1. Held inputs survive frames that do not render — FAIL-INPUT-025

`VrInput.Update` only runs inside the `shouldRender && viewsValid` branch of `VrRig.LateUpdate`
([VrRig.cs:170-176](../src/CasualtyVR.Core/VrRig.cs), [:327-328](../src/CasualtyVR.Core/VrRig.cs)). It is
skipped when:

- the runtime says not to render (headset taken off, the runtime's own menu),
- tracking is invalid,
- no frame was waited.

In each of those cases `held` keeps the last frame's keys. `Input.GetKey` then keeps reporting them: walking, attack
or climb stay held. The bridge already zeroes input when the session is not focused, but C# never reads that zeroed
state ([XrBridge.cpp:578](../src/CasualtyVR.XrBridge/XrBridge.cpp)).

The same gating also breaks key edges. When a frame skips its wait (item 8), `prevHeld` does not advance, so
`GetKeyDown` is true on two game frames. One A press can then jump twice, or open and close the inventory.

- **Fix:** call `VrInput.Update` every `LateUpdate` while VR is active, outside the render branch, passing a
  neutral input when the frame did not render. Keep only the laser and pose work conditional on valid views.
- **Check:** under xr-sim, hold `stick l 1 0`, force the session out of focus or `shouldRender` off (or stop the
  sim's frame loop), and confirm the walk stops (`input: held=[]` in the verbose log).

### 2. A lost session or exited runtime is never handled — FAIL-XR-029, XR-004

[`HandleStateChange`](../src/CasualtyVR.XrBridge/XrBridge.cpp) handles only `READY` and `STOPPING`. It ignores
`LOSS_PENDING`, `EXITING` and `XR_TYPE_EVENT_DATA_INSTANCE_LOSS_PENDING`, which are only logged.

When Virtual Desktop disconnects or SteamVR closes:

- With `LOSS_PENDING`, `sessionRunning` stays true. `xrWaitFrame` then fails every frame, and each failure is
  logged: about 180 lines per second into `LogOutput.log`.
- After `STOPPING`/`IDLE` nothing paces the loop, and vsync was set to 0, so the game runs uncapped.
- `Active` stays true, so the player needs F10 twice to get VR back.

- **Fix:**
  - The bridge reports a "session ended / lost" flag.
  - `VrRig` calls `StopVr("runtime ended the session")` and shows a ScreenNotice ("headset disconnected,
    press F10").
  - Optionally, retry `StartVr` every few seconds after a loss.
  - Rate-limit repeated identical native errors.
  - Restore vsync, or set a frame cap, whenever the session is not pacing frames.
- **Check:** kill xr-sim during a headless run. The log should show one stop line and no spam, and the game should
  keep a normal frame rate.

### 3. Native teardown runs off the render thread — FAIL-XR-028

`StopVr` runs `cvr_xr_shutdown` on a new thread ([VrRig.cs:517](../src/CasualtyVR.Core/VrRig.cs)). That function
calls `SubmitFrame`, which uses `xrBeginFrame`/`xrEndFrame` and the D3D11 **immediate context**, then
`delete g`. Meanwhile Unity's render thread may still be holding last frame's queued submit event.

The result can be two threads on one immediate context, or a use-after-free of `g`. The `--cvr-dbg-cycle` test
passes, so this is latent, not observed.

- **Fix:**
  - Add a second plugin event id ("close frame + stop") that runs on the render thread.
  - Wait for it with `GL.Flush` and a render-thread fence or flag.
  - Only then destroy the session, keeping the existing watchdog.
  - Guard `g` with a mutex, or swap it out atomically.
- **Check:** log the thread IDs of the frame owner and of the stop caller (the atlas discriminator), then run
  `--cvr-dbg-cycle 20` under load.

### 4. A dropout in position tracking blanks the whole frame — FAIL-XR-013, FAIL-HAND-003

Views count as valid only when **both** the orientation and position bits are set
([XrBridge.cpp:705](../src/CasualtyVR.XrBridge/XrBridge.cpp)), and hands likewise
([:554](../src/CasualtyVR.XrBridge/XrBridge.cpp)). Any short loss of position (Quest camera occlusion, a Virtual
Desktop hiccup) gives:

- a zero-layer frame, seen as a black flash,
- vanished hands and laser.

- **Fix:** render whenever orientation is valid, keeping the last known position ("decay, not freeze", playbook
  ch. 09). Hold a hand's last pose for about 0.5 s before hiding it. Treat valid-but-not-tracked as inferred.
- **Check:** if xr-sim can toggle validity flags, test it there. Otherwise cover the Quest's cameras for a second
  in the headset.

### 5. Recenter ignores heading, and runtime recenter is not followed

**F9 sets position only.** It records the seated origin's position but never the heading
([VrRig.cs:534-549](../src/CasualtyVR.Core/VrRig.cs), [VrSpace.cs](../src/CasualtyVR.Core/VrSpace.cs)). So the
diorama, and first person's forward, stay along LOCAL −Z even after you turn your chair.

**Runtime recenter is not followed.** A runtime recenter (a long press of the Quest button) sends
`REFERENCE_SPACE_CHANGE_PENDING`, which is only logged ([XrBridge.cpp:670](../src/CasualtyVR.XrBridge/XrBridge.cpp)).
The origin then stays off until you press F9.

- **Fix:**
  - F9 also records the head's yaw. The diorama turns to face you; in first person your forward becomes the
    corridor direction.
  - On the reference-space event, re-anchor automatically at `changeTime`.
- **Check:** in xr-sim, turn the head 90° and press F9: the diorama should be in front of you.

### 6. One exception kills the VR frame; no game-version guard — FAIL-TEST-031, FAIL-PACK-015

**No exception isolation.** `VrRig.LateUpdate` calls about 15 subsystems in a row with no isolation. One exception
(for example, a game update that renames a field used by `VoxelTerrain`) aborts `ApplyView` and `VrInput` for that
frame, and for every frame after it.

- **Fix:** wrap each subsystem update. When one throws, log the exception once, disable that subsystem, show a
  ScreenNotice, and keep the rest running.

**No version guard.** There is no check of `Application.version` (tested: Demo 7.0.1) and no `[BepInProcess]`.

- **Fix:** log a warning, and show a notice once, on an untested game version, so bug reports start with the
  right fact.

## Performance and smoothness

### 7. Periodic scans line up into a hitch every 1.33 s — FAIL-PERF-013, PERF-001

About a dozen subsystems rescan the whole scene with `FindObjectsOfType` or `GetComponentsInChildren`, each on a
fixed interval:

| Interval | Subsystems |
|---|---|
| 30 frames | Billboards, BodyView, ObjectExtruder, SpriteSlabs, VrArms, FP walls / back proxies |
| 60 frames | FP lights, FP refresh |
| 120 frames | PopupDepth, ShaderFog, Greenscreen, FP tilemaps |

Every counter starts at `-1000`, so all of them are in phase. At 90 Hz they all land on the same frame every
120 frames (1.33 s).

The perf tools report average fps only. The "worst ms" figure is logged but never checked. That fits
"reports 90 Hz but feels uneven" (FAIL-PERF-001).

- **Measure:** add a frame-time histogram to `fp-perf.ps1` and `vr-perf-matrix.ps1`: p50, p99, and the count over
  budget (playbook A5). Also use the bridge's submit-interval histogram (FAIL-PERF-024 discriminator).
- **Fix:**
  - Stagger the scans (a phase offset per subsystem), or run them round-robin with a millisecond budget.
  - Where the game has a spawn hook, register objects as they are created instead of scanning.

### 8. Frame pacing: skipped waits, competing frame cap — FAIL-XR-024, FAIL-PERF-024

**Skipped waits.** `cvr_xr_wait_frame` returns 0 while the previous frame has not been submitted yet. That happens
when the render thread falls behind ([XrBridge.cpp:684-688](../src/CasualtyVR.XrBridge/XrBridge.cpp)). That game
frame then neither waits nor renders the eyes (eyes are disabled): one frame runs free and one VR frame is missed.
Nothing counts this; the "Frame index jumped" warning cannot see it.

**Competing frame cap.** The game's console `fps` command sets `Application.targetFrameRate`. VR start resets vsync
but not that cap, so the two limiters can compete.

- **Fix:**
  - Count skipped waits and log them in the perf line.
  - Set `Application.targetFrameRate = -1` while VR runs, and restore it on stop.
  - If skips are common, have the main thread wait briefly for the render thread's submit instead of skipping.

### 9. Possible first-person judder from 50 Hz physics — INFERENCE

The first-person eye follows the head limb, and the game moves bodies in `FixedUpdate`. The decompiled code sets no
`Rigidbody2D.interpolation`. At 72, 90 or 120 Hz the target position would then step unevenly, partly hidden by
`FpSmoothing`.

- **Check:** log the eye target's change per frame while running.
- **Fix if confirmed:** interpolate the player's anchor between fixed steps in our own code. Do not move
  transforms that have a Collider2D.

## First person

### 10. Spatial audio follows the flat camera, not your head — FAIL-AUDIO-001, FAIL-AUDIO-004

The game's sounds are 3D: `Sound.Play` sets `spatialBlend = 1`. The listener is the game's own camera, whose
orientation never changes. Placement is not confirmed yet: it is in the scene, not in code.

So in first person, facing left (world −x), a sound ahead of you is heard on your left, and turning your head
changes nothing.

- **Fix:** in first person, give the listener the VR head's position and rotation. Keep the distance falloff close
  to the flat game's: moving the listener into the play plane makes everything louder (FAIL-AUDIO-004).
- **Check:** in the headset, a sound in front of you while facing left.

### 11. The head can pass into blocks; no fade — FAIL-CAM-016

Leaning moves the eye freely into blocks and through the corridor's side walls, so you can see through terrain.

- **Fix:** a short fade to black while the head is inside a solid block or outside `FrontZ..BackZ`. This is the
  standard VR comfort fix, and `VrInput.WorldAim` already does the block lookups it needs.

### 12. The world resizes once after spawn in first person — FAIL-CAM-014

`FirstPerson.Scale` uses 2.4/1.6 until `Measure` reads the real eye height (once, when the character first stands
still on the ground). The world visibly resizes then.

- **Fix:** remember the measured height (it is the same character rig every time), or seed it from the last value.

## Features

### 13. Depth layer, eye resolution scale

**Depth layer.** Submitting the eye depth buffers with `XR_KHR_composition_layer_depth` gives Virtual Desktop SSW
and SteamVR motion smoothing better reprojection when frames drop (the README's "busy scenes" limit). The buffers
already exist: the eye render targets have 24-bit depth.

**Eye resolution scale.** Add an eye-resolution % setting: smaller eye render targets copied into a sub-rect of
the swapchain (`CopySubresourceRegion` plus `imageRect`). It suits weaker GPUs. Keep the submitted FOV unchanged
(FAIL-PERF-007).

### 14. Hardware coverage (release risk)

**Untested controllers and Proton.** The Index, Vive, WMR and G2 layouts are validated against xr-sim's path
tables only. Proton is untested. The bug-report form could ask for the interaction profile string (players' logs
already have the `Controllers: profile` line).

**KrokMP.** The developer's own Steam copy runs the KrokMP co-op mod, which this mod has never been tested with.
Both may patch `UnityEngine.Input`. Run a dev copy with KrokMP installed once (FAIL-HOOK-011, FAIL-HAND-033).

## Diagnostics and housekeeping

### 15. Native log is lost on a crash or hang — FAIL-TEST-039

The bridge's log reaches BepInEx only every 2 s and at stop. A native crash or runtime hang loses the lines that
explain it.

- **Fix:** also write bridge warnings and errors to `BepInEx/CasualtyVR-bridge.log`, flushed on each line.

### 16. Code health, tests, docs, CI, playbook

- **Debug code.** Move the roughly 130 lines of headless-test code out of `VrRig.LateUpdate` (spawn, cave,
  modifier, talk, rain, fog, dump; `Array.IndexOf(Args, …)` every frame) into a `DebugHooks` class. It has no
  behaviour change, and the per-frame path becomes readable.
- **Self-tests at `--cvr-autostart`** for the maths invariants:
  - the `VrSpace` handedness conversion (det = +1, `cross(forward, left) == up`; FAIL-CAM-009),
  - the projection built from four tangents (STR-007),
  - the input hysteresis.
  The UI self-test already shows how.
- **Folder case.** `CLAUDE.md` and the docs say `tools/…`, but git tracks `Tools/`. That breaks on case-sensitive
  clones (CI, Linux). Rename it in git, or fix the docs.
- **Playbook clone.** `VR Modding Playbook/` was downloaded without `.git`, so future updates cannot be diffed.
  Clone it as `CLAUDE.md` says.
- **CI.** A small GitHub Action could build the native bridge, which needs no game files, and lint the tree for
  game files or decompiled code.
- **Optional, public:** CasualtyVR is not in the playbook's roster. Its distinctive findings could go back through
  `tools/research_receipt.py`:
  - URP 2D lights turned to face each eye,
  - the "never move a Collider2D transform for rendering" trap (90 → 10 fps),
  - ortho game → stereo diorama.
