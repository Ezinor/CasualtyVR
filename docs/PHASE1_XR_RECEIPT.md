# PHASE1_XR_RECEIPT — native OpenXR bridge, pop-up depth, headless verification

Date 2026-10-03. Environment: dev copy, **xr-sim** runtime with the `prey-quest3-vdxr` headset profile
(Quest 3 / VDXR geometry: 1824×1968 per eye, FOV L/R −54/40°, U/D 44/−55°, IPD 63 mm), **xr-tape**
API layer recording at the Khronos loader boundary. **No physical headset yet.** Grades: LIVE
(in-process, substitute runtime). Nothing below is HEADSET.

## What was built

| Piece | Where | Notes |
|---|---|---|
| Native bridge | `src/CasualtyVR.XrBridge` (C++17, CMake, OpenXR loader 1.1.63) | D3D11 binding on Unity's own device (found via `GetNativeTexturePtr()->GetDevice`), LOCAL space, one swapchain per eye, R8G8B8A8_UNORM_SRGB, raw `CopyResource` from Unity eye RTs |
| Managed rig | `src/CasualtyVR.Core` (BepInEx plugin `casualtyvr.core`) | `VrRig`: wait once in LateUpdate, two bare eye cameras, submit via `GL.IssuePluginEvent` at end of frame. `PopupDepth`: per-frame z layering. F10 toggles VR, F9 recenters |
| Launcher | `tools/launch-dev.ps1 -XrSim -XrTape` | Per-process `XR_RUNTIME_JSON` / `XR_API_LAYER_PATH`. Fails if no fresh trace with a header record, then runs `xrtape_check.py --expect-runtime xr-sim` |
| Cost matrix | `tools/vr-perf-matrix.ps1` | Same seeded scene per config, steady-state fps after world ready |

Playbook patterns applied: XR-001 (state flags only after success), XR-004 (symmetric stop),
XR-005 (wait once, render both eyes from the cache, submit after all cameras, submit the rendered
pose), XR-006 (adapter LUID check: RTX 3070 MATCH), XR-008 (API 1.1 then 1.0; xr-sim accepted 1.1),
STR-010 (empty legal frame instead of a bad one), STR-019 (convergence by asymmetric frusta; the
runtime's FOV is used as-is).

## Results

**xr-tape, final run** (`CasualtyVR_Logs\xrtape-*`, 2,624 frames): **19 passed, 0 failed, 1 skipped**
(no depth layer submitted). Covered: `session_reached_focused`, `frame_contract` (wait/begin/end
1:1:1), `eye_order`, `ipd_plausible` 0.063 m, `no_vertical_disparity`,
`submitted_fov_matches_located` (0 rad), `submitted_pose_matches_located` (0),
`eye_subimages_distinct` (all pairs), `no_submit_with_invalid_pose`,
`eye_pair_shares_display_time`.

**Frame cost** (`tools/vr-perf-matrix.ps1`, seed 1234, steady state):

| Config | fps |
|---|---|
| no VR (control) | 120 (monitor vsync) |
| VR session, eyes not rendered | 90.0 (runtime period 11.11 ms) |
| VR, both eyes, pop-up moving **all** renderers | **10** |
| VR, both eyes, no pop-up | 90.1 |
| VR, both eyes, pop-up skipping `Collider2D` objects (now default) | **90.1**, pop-up 0.9 ms |

Bridge stage timing at 90 Hz: `xrWaitFrame` is the only non-zero stage (it is pacing). Locate,
begin, swapchain acquire/wait, copy and end all ≈0.00 ms.

**Compositor capture** (`Working\xrsim-shots\verify2-*_sbs.png`): upright, lifepod centred at head
height, both eyes populated.

## Findings (route into the playbook later)

1. **Moving a Collider2D's transform for rendering costs ~85 ms per frame** with this game's ~2,400
   collider-bearing renderers (tilemap chunks, limbs, items). This holds even when the transform is
   restored before physics steps. Physics2D re-syncs the moved shapes. **Rule:** render-only offsets
   must never touch physics objects. Measured cause, not just correlation: the same config with only
   collider objects skipped returns to 90 fps.
2. **Unity D3D11 render textures are bottom-up relative to a raw swapchain copy.** The first capture was
   upside down. Fixed by a Y-flip in the eye projection (`Debug.FlipY`, default true). Winding flips with
   it. The game's sprite/tilemap shaders do not cull, so nothing visibly breaks. **Re-check if 3D
   (culling) materials are added in Phase 4.**
3. **LOCAL origin is not the head.** xr-sim reports the seated head at y = 1.6 m in LOCAL (real runtimes
   vary). The diorama is anchored to the mid-eye position captured at the first valid frame (F9
   recaptures).
4. `cvr_xr_wait_frame` refuses to wait over a frame that was never submitted. This keeps the
   wait/begin/end contract exact (`frame_contract` passes). The final waited frame at quit is not
   submitted (2,625 waits / 2,624 ends), which is legal.

## Known issues / not covered

* Process exited with `0xC0000005` at quit in **one** run that combined RenderDoc and xr-sim. Not seen
  in any run without RenderDoc. Open.
* Not yet exercised: session loss/restart, focus loss (XR-002), runtime pause (XR-003),
  F10 stop/start cycling repeatedly (XR-004 proof), real runtimes (VDXR, SteamVR), the menu scene in VR.
* No input in VR yet (mouse/keyboard on the desktop window still drive the game).
* UI canvases (screen-space overlay) are not in the headset (R9). Only the world is.

## First headset session (user, 2026-10-03, Quest 3 + VirtualDesktopXR 1.1.0) — HEADSET

| Report | Evidence | Outcome |
|---|---|---|
| Image upright, comfortable | user | HEADSET pass |
| "Seemed quite smooth" | Player log: 88–90 fps steady, `xrWaitFrame` 1–2 ms (≈9 ms headroom), eye 2172×2384 | LIVE + HEADSET pass |
| F10 toggling works | 3 stop/start cycles in log, each FOCUSED | HEADSET pass |
| Image starts low, re-entering fixed it | VDXR's first located pose had the head at y=−1.27 m (pre-tracking placeholder). The auto-recenter captured it | **Fixed:** anchor follows the head until FOCUSED + 45 frames, then locks (F9 re-locks) |
| Wants a bit more zoom | user | **Fixed:** default 25 units/m (view 1.8 m tall, was 1.5). PageUp/PageDown zoom live and save |
| "Kind of crashed when exiting" | WER `AppHangB1` (a hang, not a crash) at 21:27. Log ends before shutdown | **Fixed (xr-sim proven, VDXR pending):** graceful exit (zero-layer close of the pending frame, `xrRequestExitSession`, wait for STOPPING ≤1.5 s, `xrEndSession`, destroy), run under a 3 s watchdog thread so quit can never hang |
| Can see far more of the map | user | **Fixed:** VR-only black window frame (layer 31) whose opening = the flat camera's view × `FrameMargin` (default 1) |
| Menus not visible in headset | user | Expected (R9). Phase 3 |

Headless re-verification after the fixes: `--cvr-dbg-cycle 3` + quit under xr-sim. Three cycles each
reached FOCUSED and anchored, each shutdown went through STOPPING, the quit was logged cleanly with exit 0,
and xr-tape passed 19/19 (1,945 frames). The xr-sim compositor capture shows the frame edge clipping
the view at the flat camera's boundary. **Limit:** xr-sim never reproduced the VDXR hang, so the graceful
path is proven only on xr-sim. The watchdog is what guarantees no hang on VDXR.
