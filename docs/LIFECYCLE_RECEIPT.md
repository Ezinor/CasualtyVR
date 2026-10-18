# LIFECYCLE_RECEIPT — group A of the improvement plan (2026-10-17)

Group A of [IMPROVEMENT_PLAN.md](IMPROVEMENT_PLAN.md): items 1, 2, 4, 5 and 6, the frame cap from item 8, and
item 15. Everything was verified headless under xr-sim (Quest 3 / VDXR profile). **No headset pass has been done
yet.**

- Backup before the changes: `Working/backups/pre-groupA-20261017` (deployed plugin, dev config, Core and bridge
  sources, tool scripts; based on git `380fc1a`).
- New test: [`tools/vr-lifecycle.ps1`](../tools/vr-lifecycle.ps1), with three scenarios (`input`, `heading`,
  `faults`).

## Changes

| # | Change | Where |
|---|---|---|
| 1 | Controls run every frame, rendered or not (FAIL-INPUT-025). `VrInput.Update` used to run only on frames that rendered, so held keys stayed held while the headset did not render, and a frame that skipped its wait repeated key edges. Now a frame that does not render gets the latest synced input, or neutral input when the session is not running, and keeps last frame's pointer. The bridge also zeroes its input on every state change away from FOCUSED. A control held when focus returns (or when VR starts) is ignored until it is released (playbook ch. 09, "neutralize, do not freeze") | `VrRig.LateUpdate`, `VrInput.Update`/`ResetFocus`, `XrBridge.cpp HandleStateChange` |
| 2 | Session end (FAIL-XR-029). `LOSS_PENDING`, `EXITING`, `INSTANCE_LOSS_PENDING`, and `SESSION_LOST`/`INSTANCE_LOST` from `xrWaitFrame` all set `sessionLost`. The bridge stops waiting and skips the exit handshake at shutdown. `VrRig` stops VR and shows a ScreenNotice. A Check() failure that repeats every frame is logged once, then every 100th time | `XrBridge.cpp`, `VrRig.Update` |
| 8 | Frame pacing (FAIL-XR-024). Vsync and `Application.targetFrameRate` are turned off only while the session runs. Settings changed in the meantime (the game's console `fps`) are kept. Both come back when the session stops running and on VR stop. Waits skipped because the previous frame was not yet submitted are now counted (`waits skipped` in the perf line and the stop line) | `VrRig.UpdatePacing` |
| 4 | Tracking dropouts (FAIL-XR-013, FAIL-HAND-003). Views are valid when orientation is valid. If position is not, each eye is placed at the last tracked head position plus its own offset turned by its current orientation, and those exact poses are both rendered and submitted (XR-005). This is a hold, not the decay the playbook prescribes: the poses are absolute every frame, so no offset outlives the dropout. Hands keep their last tracked pose for 500 ms (`poseHeld`); `VrGrab` resets its motion track while a pose is held, so the jump on reacquire is never read as a throw or swing. A settle frame only counts while position is tracked | `XrBridge.cpp cvr_xr_wait_frame`/`UpdateInput`, `VrGrab`, `VrRig.UpdateSeatedOrigin` |
| 5 | Recenter heading. `VrSpace.Heading` is the seat's yaw. F9 / *Recenter* sets it to the head's yaw, while the automatic anchor and a runtime recenter use 0 (the runtime's LOCAL forward). The diorama, the first-person forward, the HUD panel and the dashboard all go through it (`VrSpace.Seated`, `SeatedRot`). `REFERENCE_SPACE_CHANGE_PENDING` for LOCAL re-anchors on the next tracked frame | `VrSpace`, `VrUi`, `VrRig.UpdateSeatedOrigin`, `XrBridge.cpp cvr_xr_poll` |
| 6 | Fault isolation (FAIL-TEST-031). Each subsystem update in `LateUpdate` runs in its own try/catch. A failure logs once, shows a notice and turns off only that feature until VR restarts; first person falls back to the diorama. Version guard (FAIL-PACK-015): `[BepInProcess("CasualtiesUnknown.exe")]`, plus a warning (log at load, notice at VR start) when `Application.version` is not `7.0.1`. The headless test hooks moved out of `LateUpdate` into `VrRig.DebugHooks` | `VrRig`, `Plugin` |
| 15 | Bridge log on disk (FAIL-TEST-039). `BepInEx/CasualtyVR-bridge.log` is appended per VR start and flushed per line, 1 MB cap, without the per-second timing and input lines. The previous game session's copy is kept as `.prev.log`. Controller dropouts are logged (`controller right: tracking lost, holding the last pose` / `pose lost` / `tracked again`) | `XrBridge.cpp Log`, `cvr_xr_set_log_file`, `Plugin.Awake` |

ABI: `CvrFrameInfo.positionValid`, `CvrInfo.waitsSkipped/sessionLost/referenceSpaceChanged` and `CvrHand.poseHeld`
were added at the end of their structs, mirrored in `XrNative.cs`. Bridge and Core must ship together, as before.

New debug flags: `--cvr-dbg-recenter <s>` (F9 at that level time) and `--cvr-dbg-throw <s>` (an exception in the
debug hooks).

## Verification (all headless, xr-sim, seed 1234, dev copy)

| Check | Result |
|---|---|
| `tools/vr-lifecycle.ps1` (3 launches) | **35/35** |
| Same `input` scenario on the 1.2.0 build (backup) | 7/16. Discriminating failures: `held=[D]` stayed after the stick was released with head tracking off, and through focus loss in VDXR's no-render model. 1.2.0 also never stopped VR on `state lost` |
| xr-tape (`launch-dev -XrSim -XrTape --cvr-quit`) | **19/19**, 795 frames |
| `tools/fp-smoke.ps1` | **11/11** |
| `--cvr-dbg-cycle 3` (stop/start) | 3/3 cycles reached FOCUSED; vsync restored to 1 after each stop |
| Dev config | Unchanged. BepInEx re-saved it once during fp-smoke without its UTF-8 BOM, with identical settings; restored byte-for-byte from the backup (SHA-256 `D1D36925…D780`) |

What the `input` / `heading` / `faults` scenarios drive:

- **Input:**
  - `stick l 1 0` gives `held=[D]`.
  - `head valid off`, then release: `held=[]` (1.2.0: `[D]`).
  - `focus norender on` + `focus lose`: `held=[]`.
  - `focus regain` with the stick held: `Input: focus regained with [L.stickRight] held; ignored until released`,
    and `held=[]` until a release.
  - `hand r valid off`/`on`: held, then lost, then tracked again.
  - xr-sim `recenter`: `Recentered (runtime)`.
- **Heading:** with `head rot 60 0 0` (60° left), `--cvr-dbg-recenter 45` logs
  `Recentered (key) ... heading 300.0 deg`, and the head's world forward is `(0.00, 0.00, 1.00)`, straight into
  the diorama. The compositor capture `Working/lifecycle-heading_left.png` shows the lifepod in front.
- **Session end:** `state lost`, `state exiting` and `instanceloss` each give:
  - `VR stopped (the OpenXR runtime ended the session)`,
  - the notice,
  - `Frame pacing: the game's own`,
  - 0 `xrWaitFrame failed` lines,
  - the game still running.
- **Faults:** `--cvr-dbg-throw 28` logs one error for `debug hooks`, and frames keep being submitted
  (2065 → 2427 over about 5 s).

## Not verified, or limits

- **Orientation-only dropout:** xr-sim's `head valid off` clears both bits, so the position-hold path (orientation
  valid, position not) never ran here. What ran is the no-render path. Needs a headset (cover the Quest's cameras)
  or an xr-sim mode that clears position only.
- **Real hardware:** the runtime-recenter event and the session end (closing Virtual Desktop, headset sleep) have
  not been tried on Virtual Desktop yet.
- **Frame cap:** the console `fps` interaction was not exercised; only the vsync restore and the pacing log lines
  were.
- **Not in this round:** item 3 (teardown on the render thread) is unchanged. `cvr_xr_shutdown` still runs on a
  worker thread.
- **Test-tool note:** BepInEx buffers `LogOutput.log`, so lines read from it can predate a command just sent.
  `vr-lifecycle.ps1` reads the Unity player log (`CasualtyVR_Logs/Player-*.log`) instead; `fp-smoke.ps1` still
  reads `LogOutput.log` with fixed sleeps.

## Headset check, next time

1. Hold the left stick, then lift the headset off: the character stops. Put it back on while still holding: it
   does not walk until you let go and push again.
2. Turn your chair 90°, press F9: the diorama (and first person's forward) is in front of you.
3. Hold the Quest button to recenter: the view follows without F9.
4. Close Virtual Desktop's streaming while in VR: a message on the game window, and F10 starts VR again once
   it is back.
5. Cover the Quest's cameras for a second or two: the view keeps turning, with no black flash, and the hands
   stay put.
