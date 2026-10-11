# RELEASE 1.2.0 (2026-10-07)

- **Package:** `dist/CasualtyVR-1.2.0.zip` (re-packaged 2026-10-07 with first-person highlights off by default), 981 KB, SHA-256
  `E03419F8883869B413D08BE3A8E13A9AE6CCCBBEB8108E762C43F2F3E846466F` (also in `dist/CasualtyVR-1.2.0.zip.sha256`).
- **Player notes:** `release/README.md`, `release/CHANGELOG.md`.
- **Snapshot:** `Working/backups/release-1.2.0`.

## Contents

The layout is the same as 1.1:

| File | Size |
|---|---|
| `BepInEx/plugins/CasualtyVR/CasualtyVR.Core.dll` | 219 KB |
| `casualtyvr_shaders` | 17 KB |
| `native/CasualtyVR.XrBridge.dll` | 182 KB, now with the static CRT |
| `native/openxr_loader.dll` | 2.6 MB |
| `CasualtyVR-docs/` | README, CHANGELOG, LICENSE, third-party notices, OpenXR licence |

## What is in it

Since 1.1.0. Details:
- `docs/PERF_FP.md`
- `docs/SHADERS.md`
- `docs/LAYER_MODIFIERS.md`
- `docs/FP7_RECEIPT.md`
- `docs/COMPATIBILITY.md`

### Before this round (2026-10-06)

- First-person performance: 40 → 90 fps.
- Smooth shader fog.
- First-person rain.
- Every layer modifier checked.

### Headset feedback round 1 (FP7)

- Run speed set to 0.8.
- Upright body.
- Both legs.
- Speech captions.
- Reset page.

### Headset feedback round 2 (2026-10-07)

**Speed:**
- The speed multiplier applies only while first person is active (`FirstPerson.MoveSpeedPatch`).
- On leaving first person, the cap returned to the game's at once: `--cvr-dbg-fpoff 24` logged `cap 19.9 of 25.0` → `24.9 of 25.0` (the 0.1 is the game's own leg factor).
- VR shutdown clears `FirstPerson.Active`.

**Highlights:** `FirstPerson.Highlight` = Off (default) / Blocks / All.
- First the item outline (the hover square with an item sprite) went off by default. Then, at the user's request before release, the striped block marker did too.
- *Blocks* brings back the block marker; *All* also brings back the item outline.
- **Verified:**
  - Items, all three modes, with a spawned water bottle: `Markers: item outline 'waterbottle' hidden/shown`.
  - Block marker under Off / Blocks: `Markers: block marker hidden/shown`, capture `mkOff_aim`.
  - In the released package's fresh config, first person: `block marker hidden`.
- The user's dev config held an auto-written `Highlight = Blocks` from the test runs; it was set to Off.

**Settings menu:**
- **Merged rows:**
  - `OptNum` rows (Off or the amount): controller hands, object depth, character thickness, liquid depth, arms, body, and vibration, which now exposes `HapticStrength`.
  - One cursor size row.
  - Turning + speed, with 300°/s added for the user's setting.
  - Void darkness folded into tunnel sides.
- **Moved or removed:**
  - Recenter becomes a header button.
  - Thickness layers is config-only.
- **Other menu changes:**
  - Version shown at the bottom.
  - Panel made opaque.
  - Text shrinks to fit.
  - Controls show the game's localised action names (`Locale.GetOther("gameset" + action)`).
  - Game tooltips (`UIUtil.GetUITooltip`) and world hover labels (`GlobalDark.SetTooltip`) are suppressed while the menu is open.

**Lockpicking:**
- **Cause:** the earlier sprite measurement used trimmed texture rects, so "fingertip 165 px left / 235 px up" was wrong.
- **Corrected geometry:**
  - Every hand sprite is 256×630, stretched into the 768×1890 image.
  - The game's pivot is (0.25, 0.86).
  - The pointing fingertip is at (0.248, 0.907), 89 px above the click.
  - The lockpick tip is at (0.244, 0.885), 47 px above the click.
  - Against a 52 px ring, aiming with the tip missed.
- **Fix:**
  - In VR the lockpick's pivot is the tool tip.
  - The default hand offsets are now 0/0, the user's own setting and the game's geometry.
- **Verified:**
  - New fp-smoke scenario "lockpick" (pivot at the fingertip; ring touch + trigger engages).
  - Capture `lock2_ringtop`.

**Polish:**
- The eye now anchors on the neck joint instead of the hips: the hips stayed up when the game bent the spine under low ceilings. The eye is also kept out of solid blocks.
- Head direction is the average of both eyes (canted displays).
- On-screen notices for VR start failures (`ScreenNotice`).
- Direct3D 11 guard.
- `Window.PreferredDisplay` default empty. It was the user's own "1920x1080", which would have moved other players' windows; the user's config keeps theirs.

**Compatibility:** `docs/COMPATIBILITY.md`.
- Index, Vive, WMR and HP profiles.
- BGRA swapchains.
- Adapter mismatch is no longer fatal.
- Static CRT.
- Proton notes in the README.

## Verification

| Check | Result |
|---|---|
| `tools/fp-smoke.ps1` | 11/11 (with the new lockpick scenario) |
| xr-tape, dev copy, first person / diorama | 19/19 each, 0 exceptions |
| fp-perf (settled) | diorama 90.1 fps, first person 91.1 fps |
| Flat game (no VR) | clean, `CasualtyVR 1.2.0` |
| No-runtime start (`XR_RUNTIME_JSON` → missing file, one process) | notice "no OpenXR runtime is available ... press F10" |
| xr-sim profile suggestions | simple, vive, wmr, index, touch: all `XR_SUCCESS` |
| **Release zip in `Working/release-test`** (old plugin and config removed, zip extracted, dev probe added only to drive the run) | 19/19 default config; 19/19 with `--cvr-dbg-fp` (`First person: on (running speed 80% of the game's)`); shader bundle loaded; 0 exceptions |
| Fresh config defaults | `[FirstPerson] Enabled = false`, `StartVrOnLaunch = false`, `Greenscreen = false`, `PreferredDisplay` empty, minigame offsets 0/0, `Highlight = Off`, `MoveSpeed = 0.8` |
| Fresh config, first person, aiming at the floor | `Markers: block marker hidden in first person` |
| User's dev config | after all tests, identical to the morning backup except the new 1.2 entries BepInEx adds and `Highlight = Off` (tests edit it temporarily and restore it) |

## Not verified

- On real hardware other than Quest 3 + Virtual Desktop: Index, Vive, WMR and HP controllers.
- Linux / Proton.
- Tutorial world text facing the player (same code as damage numbers and speech, but not triggered in a test).
