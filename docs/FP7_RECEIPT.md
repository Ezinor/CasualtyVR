# FP7 â€” headset feedback round (2026-10-06)

User's report after the 1.2 work:

1. Movement should be a little slower in first person.
2. Running from a stop, the view was sometimes blocked, probably by the body.
3. Only one leg shows.
4. Dialogue text is drawn on the side of the wall and hidden.
5. Each page of the VR settings needs a reset to defaults.

## Changes

| # | Cause | Fix | Where |
|---|---|---|---|
| 1 | The game's top speed `Body.actualMaxSpeed` is 25 | A Harmony postfix multiplies it by `FirstPerson.MoveSpeed` (default 0.8) for the player while first person is active. Acceleration is unchanged. The menu row *Movement speed* (0.5â€“1) is on the *1st person* tab | `FirstPerson.MoveSpeedPatch` |
| 2 | Three causes:<br>â€¢ The game rotates every limb about the lower torso by `Body.accelRot` when the character speeds up or slows down: 14â€“18Â° from a stop, more with weak legs.<br>â€¢ The spine also bends towards the look target (`torsoLookSmooth`) and the head turns with the look.<br>â€¢ Running and looking down measured the game's upper torso 0.8 blocks in front of the eyes and 0.1 below them, and the view dropped by 0.4 blocks | â€¢ **Body:** the drawn spine is set upright about the lower torso; the legs lose the acceleration lean; the body is kept under the view's (smoothed) anchor; and the upper torso is never closer to the eyes than when standing still.<br>â€¢ **View:** standing, the eye is a fixed height above the hips, measured at rest, and no longer the head limb's height. Crouching follows the head down. Climbing uses the head as before. The eye stays within 1.2 blocks of the head | `BodyView.Update`, `FirstPerson.Update` |
| 3 | All eight leg pieces were drawn at z = 0, which is how the side view stacks them, so in first person one leg sat inside the other | The front leg goes to one side (âˆ’0.3 blocks), the back leg to the other (+0.3). Across the view, depth is the body's width: shoulders 1.15, hips 1.05, thighs 0.5, shins and feet 0.42 blocks | `BodyView.DepthBlocks/SideBlocks` |
| 4 | `Talker.LateUpdate` puts the speech text (a world TextMeshPro) 4 blocks above the head and 2.6 blocks towards the flat camera. The corridor is Â±2 blocks wide, so the text sat inside the front cave wall, edge-on | `WorldText` (eye-only, put back after each eye):<br>â€¢ **Your own speech:** a caption 1 m ahead and 0.2 m below eye level that lazily follows your yaw, about 4 cm per line, drawn over walls (a per-text material with TMP's depth test off).<br>â€¢ **Other world text** (other characters' speech, damage numbers, tutorial text): moved to the corridor centre and turned to face you, scaled up to at least 1.7Â° per line.<br>â€¢ **Damage numbers** last 0.5 s, so a postfix on `WorldGeneration.CreateDamageNumber` triggers a rescan | `WorldText.cs` |
| 5 | â€” | **Reset page** button beside *Done*: the first press arms it ("Press again", 3 s), the second resets every setting shown on that tab. The first-person and greenscreen on/off switches are not reset. It replaces the *Controls* tab's own reset row | `VrSettingsMenu.ResetPage` |

### Bug found on the way

**BodyView** rebuilt every body piece every frame:
- **Cause:** its rescan cleared the "built for this body" marker, so every frame destroyed and recreated about 10 GameObjects.
- **Effect:** the body was correct, but the work was wasted.
- **Now:** it builds once per body, or when clothing changes.

## Verification (xr-sim, seed 1234)

### Speed

`cap 19.9/25.0` while walking in first person, which is 0.8 Ã— 25.

### Body and view

Upper-torso position relative to the eyes, in blocks (verbose `Body:` lines, `run2_*` captures):

| Moment | Game's upper torso | Drawn upper torso |
|---|---|---|
| Running from a stop | 0.84 ahead | 0.78 behind |
| Running and looking down | 0.8 ahead, 0.1 below the eyes | behind |
| Standing still | â€” | 1.26 below the eyes |

| Pose | Head above the hips | Eye above the hips |
|---|---|---|
| Looking down | 1.86 | **2.25** (held) |
| Crouched | 1.03â€“1.79 | follows the head |

### Legs

Looking down shows two legs side by side (`pose_down`, `run2_down`).

### Text

`--cvr-dbg-talk` makes the character speak and shows a damage number every 6 s:
- **The caption:** readable in front of the lifepod (`talk_fwd`, `talk2_a`).
- **The damage number:** handled (`WorldText: facing '42'`). In these shots it is behind the lifepod pillar.
- **Tutorial text:** handled by the same code, but not tested.

### Reset page

`--cvr-dbg-settings <tab> --cvr-dbg-reset` presses it twice:

| Tab | Settings reset | Changed for the test, back to default after |
|---|---|---|
| 1st person | 14 | view distance 100 â†’ 60, Smooth â†’ Snap, Void â†’ CaveWalls |
| Controls | 20 | A = inventory â†’ jump, minigame hand X 120 â†’ 50 |

- **Page scoping:** the 1st-person values changed for the test stayed as they were in the Controls run.
- **Your config:** restored after each test.

### Regression

| Check | Result |
|---|---|
| fp-smoke | 9/9 |
| xr-tape, first person | 19/19, no exceptions |
| xr-tape, diorama | 19/19, no exceptions |

Perf (fp-perf, settled):

| Run | First person | Diorama |
|---|---|---|
| Repeat runs | 90.1 and 90.1 fps | 90.1 fps |
| One earlier run | 80 fps (noise: not repeated) | â€” |

New debug flags:
- `--cvr-dbg-talk`
- `--cvr-dbg-reset`

## For the headset pass

- Running from a stop, and looking down while running.
- Two legs.
- Your character's lines as a caption (size and distance are constants in `WorldText`).
- Movement speed (0.8 by default; *1st person* > *Movement speed*).
- **Reset page** on each tab.

> **2026-10-07 (1.2.0):** the eye now anchors on the neck joint, not the hips (the hips stayed up when the game bent the
> spine under low ceilings); see docs/RELEASE_1.2.md.
