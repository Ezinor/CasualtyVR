# FP4_RECEIPT — first person: physical extras (2026-10-05)

Plan: `docs/FIRSTPERSON_PLAN.md` (FP-4). Source backup: `Working/backups/fp4-20261005/`.
Status: **built and verified headless, except grip-climbing (see below); waiting on the headset**.

## What changed

- **Eat and drink at the mouth (`VrGrab.Mouth`).**
  - Holding a usable item of category food, water or drug within 15 cm of the mouth (8 cm below and 5 cm ahead of
    the eyes) for 0.35 s uses it with the game's own `Body.UseItem`.
  - Uses repeat at most once per 1.2 s, as sips or bites, and each one gives a short vibration.
- **Grip-climbing (`VrGrab.Climb`).**
  - Grip near a rope or ladder (`Climbable.GetClosestClimbable`, hand within 0.9 blocks of its path) starts the
    game's own climb (`Body.StartClimbing`, which also needs the body within 1 block).
  - While the hand grips, its room-space movement moves you along the rope the other way: pull down to go up.
  - The last hand to grab leads, so you climb hand over hand.
  - It writes the game's public `climbableProgress` and holds `climbVelocity` at 0 while gripping. The stick
    climbing still works.
- **Vibration.**
  - New in the native bridge: one `XR_ACTION_TYPE_VIBRATION_OUTPUT` action per hand, bound to
    `/user/hand/*/output/haptic` on Touch controllers, and the export `cvr_xr_haptic(hand, amplitude, seconds)`
    (`xrApplyHapticFeedback`).
  - It pulses on grab, use, climb, swing attack, and eat or drink.
  - Settings: `Controllers.Haptics` (on) and `HapticStrength`; in the menu, *Vibration* in the First person tab.
- **Minigames by touch (`VrUi.TouchDashboard`, setting *Touch minigames*, on).**
  - In first person a minigame's panel comes within reach: 0.42 m ahead and 0.32 m below the eyes, 0.62 m wide,
    facing you.
  - The right hand is the pointer when it is within 10 cm in front of or 15 cm through the panel. The laser still
    works from further away.
  - The game's own minigame hand follows. Trigger clicks as before.

## Verification (xr-sim, scripted hands)

| Test | Result |
|---|---|
| Holding a water bottle (`--cvr-dbg-hold`), hand to the mouth for 2.5 s | `waterbottle to the mouth -> use` twice (two sips) |
| Vibration | `[bridge] haptic: xrApplyHapticFeedback(hand 1, 0.25, 0.08s) -> 0 XR_SUCCESS` |
| Keypad minigame (`--cvr-dbg-spawn dropcapsule --cvr-dbg-keypad`), hand at the panel centre | mouse (960, 540) |
| Same, 12 cm right | mouse (1331, 540) |
| Same, up and left | mouse (588, 712) |
| Capture `fp4mg_touch_left.png` | the keypad close and low, the game's hand at the touch point |
| Grip-climbing | **not verified**: a spawned rope item is not placed (its climb path was 124 blocks away). The code reaches the game's distance check (`Grab: climbable ... distance` log). Needs a placed rope or ladder, in the headset or with a placement step. |
| xr-tape, diorama and first person | 19/19 and 19/19, no exceptions |

**New debug flag:** `--cvr-dbg-spawn-near` spawns test objects 0.5 blocks from the character.

## Open

- **Grip-climbing:** check on a real rope or ladder.
- **Damage vibration:** a pulse when the character is hit (HAND-003: drive it from a channel the game already
  modulates, such as pain or hit flashes).
- **Hand poses:** grip and point poses on the character's hand art.
- **Next:** FP-5, the torso and legs with damage and effects (required before the release), then FP-6, settings and
  the release.
