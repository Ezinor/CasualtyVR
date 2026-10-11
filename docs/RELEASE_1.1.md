# RELEASE_1.1 — CasualtyVR 1.1.0 (2026-10-05): first-person mode

Package: `dist/CasualtyVR-1.1.0.zip` (888 KB), SHA-256
`CC8186FB91EB9BCD3AF3750492F4731859E6B48C2265E49DF3EDB9F32E653F1C`, built by `tools/package-release.ps1`.

- **Layout:** the same as 1.0. Core and the native bridge (now with haptics) go under `BepInEx/plugins/CasualtyVR/`,
  and the docs under `CasualtyVR-docs/`.
- **Player docs:** `release/README.md` has a new *First-person mode* section (controls, hand gestures, settings,
  limits), and `release/CHANGELOG.md` a 1.1.0 entry.
- **Plan and per-phase receipts:** `docs/FIRSTPERSON_PLAN.md`, `docs/FP0_RECEIPT.md` to `docs/FP5_RECEIPT.md`.

## FP-6 changes

- **VR settings:** the first-person options are split into two tabs, *1st person* (view, comfort, world) and
  *Hands* (arms, body, aim, grab, swing, touch, vibration). There are now six tabs, with narrower buttons.
  `--cvr-dbg-settings` accepts a tab-name prefix.
- **`tools/fp-smoke.ps1`:** a scripted first-person smoke test on xr-sim. It drives the head and hands and checks the
  game's results in the log. It returns the number of failures as its exit code; `-Only <scenario>` runs a subset.

  | Scenario | What it checks |
  |---|---|
  | grab-throw | pick up a bottle, throw it |
  | chest-store | store at the chest, open the radial from the chest |
  | mouth | drink, and the vibration call accepted by the runtime |
  | touch | the keypad minigame follows the hand |
  | swing | a swing attack |

- **Version:** 1.1.0.

## Verification

| Check | Result |
|---|---|
| `tools/fp-smoke.ps1` | all 9 checks pass (grab-throw, store and the rest in the full run; chest-store and touch passed again on their own after the timing fixes) |
| Isolated copy `Working/release-test` | game copy, **release zip extracted**, fresh config; the dev probe was added only to start a run headless |
| Start in VR (`--cvr-vr`), xr-tape | 19/19, log `CasualtyVR 1.1.0`, clean quit (808 frames, 0 dropped) |
| Same with `--cvr-dbg-fp` | 19/19, `First person: on` |
| Fresh config defaults | `[FirstPerson] Enabled = false`, `StartVrOnLaunch = false`, `Greenscreen = false` |
| Dev copy, xr-tape, diorama and first person | 19/19 each (after every FP phase) |

## Headset acceptance (user, 2026-10-05)

- **Accepted:**
  - FP-0: camera comfort, block scale, tunnel width, lighting.
  - The camera no longer moves when the hand moves (look pinned to the head).
  - The brightness seam is fixed.
  - Rope climbing "works really well".
  - Swing attacks work well.
- **Reworked after feedback:**
  - The body was too far forward (now 0.3 m behind the eyes).
  - The arms were too flat (now blocky).
  - Plants were sideways (objects now face you).
- **Still to confirm in the headset:** vibration ("didn't notice", to retest), the reworked body and arms, objects
  facing you, the minigame touch panel.

## Known limits (also in the README)

- **Lighting:** the 2D lights are approximated from inside the play plane. Big lights that contain you light the
  view evenly; small ones are drawn as discs.
- **Fog:** shows faint steps (a smooth-fog shader needs a Unity-built bundle).
- **Creatures** are seen side-on.
- **Body:** seen from above it is mostly its extrusion walls.
- **Ragdoll:** the both-grips ragdoll is off in first person.
- **Performance:** in xr-sim, while capturing, first person ran at about 35–80 fps. This needs a headset reading in
  heavy scenes.
