# RELEASE_1.0 — CasualtyVR 1.0.0 (2026-10-04)

Package: `dist/CasualtyVR-1.0.0.zip` (+ `.sha256`), built by `tools/package-release.ps1`. Extract it into the
game folder: `BepInEx/plugins/CasualtyVR/` (CasualtyVR.Core.dll, native/CasualtyVR.XrBridge.dll,
native/openxr_loader.dll) and `CasualtyVR-docs/` (README, CHANGELOG, third-party notices, OpenXR licence).
Player docs come from `release/`.

## Release changes (from the dev build)

- Version 1.0.0 (`Plugin.Version`, logged at start).
- `VR.StartVrOnLaunch` (default **off**, user decision 2026-10-04; it replaced `StartInVr`, which defaulted to on, so
  saved configs do not keep the old default). F10 starts VR. If no runtime is found, the game carries on flat and the
  log says why.
- MIT licence (`release/LICENSE.txt`, shipped in `CasualtyVR-docs/`).
- Quiet log: perf/input lines, bridge timing, UI self-test, alpha probe, UI texture dump (it wrote a PNG on every
  run), and slab/popup/liquid diagnostics only when verbose: `Debug.VerboseLog`, `--cvr-verbose`, or any headless
  test run (`--cvr-autostart`).
- Not shipped (developer tooling): the probe plugin, the sandbox patcher and runtime, and the debug flags. The
  flags remain in the code and are inert unless passed.

## Verification (xr-sim, isolated copy `Working/release-test`, extracted from the zip, fresh config)

| Check | Result |
|---|---|
| Start with no flags | `CasualtyVR 1.0.0`, VR started by itself (StartInVr build), theatre panel at the first-run intro |
| Clean quit (window close) | bridge "requesting session exit", 4370 frames submitted / 0 dropped, VR stopped |
| xr-tape on that run | 19/19 (1 skip) |
| No OpenXR runtime (`XR_RUNTIME_JSON` pointed at nothing) | `XR_ERROR_RUNTIME_UNAVAILABLE`, "VR start failed", game responsive and flat |
| Gameplay | same binary as the dev deploy: voxels, slabs, extrusion, liquids, fog, menu all verified there (OVERNIGHT_20261004.md) |

Headset acceptance: Phases 1–4 and the follow-ups were accepted on a Quest 3 / Virtual Desktop, except the last
three changes (cursor polish, mist fog, object extrusion), which still need a headset pass on 1.0.0.

## User install report (2026-10-04): "Start does nothing"

On the user's modded Steam copy, clicking *Start* for a run did nothing. Their log shows 185 times:
`[Krokosha_MP_CU] Multiplayer: You can't play singleplayer with MP mod active. Deactivate it in Settings > General`
(the KrokMP co-op mod), both while VR ran and after VR was stopped with F10. Not a CasualtyVR issue; documented
in the README's known limits. (In VR the mod's on-screen hint was not noticed.)

## Open items (post-1.0)

Performance in heavy scenes (rain, crowds: 75–85 fps measured), the other layer modifiers (Rainy, Flooded...)
in VR, haptics, left-handed defaults.

## 1.0.1 (2026-10-04): minigame click point at the fingertip

User: in minigames the click "is not lined up with the pointer finger". Minigames act at `MinigameBase.handPos`, which
is the pivot of the hand image. The game puts that pivot at (0.25, 0.86) of a 768x1890 image, about 225 px below and
80 px beside the index fingertip. While VR runs, `VrHands.UpdateMinigamePivot` sets the pivot to the tip of the
pose shown, from a table measured on the dumped art (index fingertip for hands, tool tip for tweezers, wrench,
lockpick and blade). It is mirrored with the game's left-hand scale and restored when VR stops. Headless test:
`--cvr-dbg-spawn dropcapsule --cvr-dbg-keypad` opens a keypad minigame.

## 1.0.2 (2026-10-04): 1.0.1 reverted, adjustable minigame hand offset

The user found 1.0.1 "much worse: clicks have to be way down and to the right of where I'm visually trying to click".
Measured in a keypad minigame (`--cvr-dbg-spawn dropcapsule --cvr-dbg-keypad`, verbose log): the click point is
exactly the laser/mouse (UI raycasts at `Input.mousePosition`; `handPos` settles on it), the hand pivot sits there,
and the pointing fingertip is drawn 165 px left of and 235 px above it. The flat game has the same geometry. Aim
habits differ by minigame (finger vs palm), so no single fixed point fits. 1.0.2 restores the game's pivot and adds
`Controllers.MinigameHandOffsetX/Y` (menu: Controls tab), which shifts the hand image relative to the click point in
image pixels, live, mirrored for the left hand.

## 1.0.3 (2026-10-04)

The user tuned the minigame hand offset in the headset: X 50, Y -50 "feel good". These are now the defaults.
