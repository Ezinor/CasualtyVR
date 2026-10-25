# PHASE4B_RECEIPT — hands: poses, grab affordance, distance pull (2026-10-23, headset round 2026-10-24)

Phase 4b of [BUILD_PLAN.md](BUILD_PLAN.md): FP 9b, 9c and 9g of [FIRSTPERSON_REVIEW.md](FIRSTPERSON_REVIEW.md), with
decisions D4 (the Alyx-style pull) and D5 (a faint glow). Phase 4a is [PHASE4A_RECEIPT.md](PHASE4A_RECEIPT.md). Test
builds: `1.3.0-test9`, then `1.3.0-test10` after the headset pass (see *Headset round*). Both contain Phases 1–4a.

- **Backups before the changes:** `Working/backups/pre-phase4b-20261023`; before the headset fixes,
  `Working/backups/pre-phase4b-fix-20261024`.
- **Code:**
  - [`HandPoses.cs`](../src/CasualtyVR.Core/HandPoses.cs) (new: the voxel hands and the marker icons);
  - `VrArms` (`PlacePose`: the pose drawn instead of the hand; `--cvr-dbg-dumphands` saves the arm sprites);
  - `VrGrab` (the affordance, the far-item selection, the pull, the marker, each hand's pose);
  - `Billboards` / `FirstPerson` (`BillboardCopies`: where the eye sees an item, for the marker);
  - `VrUi` (draws the marker in first person, outside menus);
  - `VrSettingsMenu` and `Plugin` (three settings).
- **Tests:** `tools/fp-smoke.ps1`, new scenarios `affordance`, `pull` and `poses` (captures of each pose); `--cvr-dbg-pull-aim` logs the hand angles
  that point at a spawned item, so the scenario can aim at it through xr-sim.

## 9b Hand poses

The character's arm art (`--cvr-dbg-dumphands`):

- the upper arm is 4×12 px, the forearm 4×14 px and the hand a **6×4 px** mitten, all at 8 px per unit;
- they come in three greys (0x28, 0x36, 0x4c);
- the game's `Shader Graphs/Limb` material adds the skin colour and lighting.

Each pose is a **small voxel hand** (`HandPoses.MeshFor`), built from boxes in the controller's grip frame:

- three fingers and a thumb, with claws, like the character;
- the fingers point where the controller points (the aim pose), the thumb side runs up the handle (the grip's
  forward), the palm faces in, and the handle passes through the curled fingers (test11; test10 pointed the fingers
  along the handle);
- the left hand is mirrored;
- the faces take the arm's three greys from a small palette (top and back of the hand light, sides mid, underneath
  dark; claws 0x16), and
  the hand's own Limb material tints and lights them like the arms;
- a voxel is 0.09 units (the arm's pixels are 0.125), so the palm is about 11 cm across the knuckles at the game's scale.

Offline preview of all five poses from four angles: `Working/hands-phase4b/voxel-hands-preview.png`.

| Controller | Pose |
|---|---|
| Nothing within reach | Open |
| Something within reach or locked for a pull (9c) | Half-closed |
| Grip | Fist |
| Trigger past half | Pointing |
| Trigger past 90 % | Pointing and pressing |
| Holding an item, or the hand wears something (gloves, bandages: any sprite under the hand limb) | The character's own hand |

The pose replaces the hand's block, starting half a voxel inside the forearm so no gap opens at the wrist. *Hand poses* (*Hands & body › More settings*) turns it off.

## 9c The grab affordance

Playbook ch. 02: a grab zone you cannot see needs a channel that tells you it is there. Three are used:

1. **The pose:** the open hand half-closes.
2. **A haptic tick:** 0.15 for 15 ms, once, when something comes within reach (or under the laser, 9g). Nothing
   repeats while it stays.
3. **A glowing hand on the item (D5, revised):** an open pixel-art hand (a fist once locked for a pull), drawn
   additively in pale white with a 1 px halo and a slow ±15 % pulse:
   - strength: 80 % when the item is in reach, 60 % when pointed at, 100 % once locked;
   - placement: at the centre of the item as the eye sees it (its billboard copies), a little towards the eye,
     facing it;
   - size: 8–24 cm tall, growing with distance.

   The game's own outline highlight stays off unless *Highlight under your hand* says otherwise.

What counts as "within reach" is the grab's own choice (the nearest loose item within 0.9 blocks of the hand), so
the marker shows exactly what the grip will take. Ropes, ladders, crates, doors and machines in reach get the pose and
the tick, not the marker. *Hand on what you can grab* (*Hands & body*; config key `GrabGlow`) turns the marker off.

## 9g The distance pull (D4)

When nothing is within reach, the hand's laser selects a loose item further off:

| Rule | Value |
|---|---|
| Cone | within 6° of the laser, or within 0.6 blocks of it close up (`atan(0.6 / distance)`) |
| Reach and sight | the game's own `Body.DoPickupCheck` (a `Ground` linecast and under `Body.interactionRange` = 10 blocks), with its alerts off |
| Refresh | every 3 frames, or at once if the item stops being loose |
| Lock | the grip, while the item is selected (tick 0.3 / 30 ms) |
| Pull | a flick **towards the head** of at least 1.5 m/s (the 4a velocity, room space), while the grip is held |
| Result | the game's own `Body.PickUpItem` into that hand (tick 0.5 / 60 ms, the backpack sound) |
| Cancel | letting go of the grip, or the item is picked up or moved by anything else |

Because the pull ends in the game's own pickup, every game rule still applies (a full hand, an item stuck in the
world). *Distance pull* (*Hands & body*) turns it off.

## Headset round: test9 → test10 (2026-10-24)

| Feedback on test9 | Cause | Change in test10 |
|---|---|---|
| The pull works well | — | none |
| The glow works, but "not a big fan of it"; asked for "a moderately glowy character hand on the item" | — | the item no longer glows; a glowing pixel hand marks it (9c, point 3) |
| The hands "look terrible" (two screenshots: big, smooth, flat slabs) | three causes, below | voxel hands in the arm's style (9b) |

Why the test9 hands looked wrong:

1. **The style:** the minigame drawings are 255×628 px, smooth and high-resolution. The arms are 4 px wide pixel
   art, with the hand a 6×4 px mitten.
2. **The size:** the hands were scaled from the hand bone's length, with a wide spread drawing, so they came out
   much larger than the arms.
3. **The flat picture:** turned to face the eye, a flat hand on a controller is seen edge-on from the head and
   smears into a slab. This would happen whatever the art.

A first attempt at pixel-art sprites in the arm's greys still smeared that way in the captures, so the hands are now
3D.

## Headset round: test10 → test11 (2026-10-24)

| Feedback on test10 | Cause | Change in test11 |
|---|---|---|
| Size and fit "pretty good" | — | none |
| Holding the controllers level, the hands point straight up | the hand followed the grip pose's forward axis, which runs along the handle (nearly upright when the controller is level) | the fingers follow the **aim** pose (where the controller points); the thumb side runs up the handle; the hand is placed so the handle passes through its curled fingers on the palm side (`VrArms.HandleAt`) |
| The character has three fingers and a thumb, and claws | — | three fingers and a thumb, a dark claw (palette 0x16) on each tip; the marker icon too |
| fp-smoke takes a long time | one game launch per scenario (12 launches, ~10 min) | scenarios share sessions (5 launches); `-Quick` runs two sessions (22 checks in 125 s) |

## Also in this round: test speed and a focus bug (test12)

- **Test speed:** the suites share game sessions, since starting the game is most of a run:

  | Suite | Launches before | After |
  |---|---|---|
  | fp-smoke | 12 | 5 (`-Quick`: 2) |
  | fp-comfort | 5 | 2 |
  | vr-lifecycle | 5 | 3 |

  `tools/regress.ps1` runs every suite with a timed summary:
  - `-Quick` (5 launches): 4/4 suites in 246 s;
  - the full run: 872 s, against about 25–30 min before. Per suite:

    | Suite | Result | Time |
    |---|---|---|
    | fp-smoke | 25/26 (the chest finding below; 22/22 in `-Quick` after the fix) | 244 s |
    | vr-menu | 0 failures | 295 s |
    | vr-lifecycle | 43/43 | 212 s |
    | fp-comfort | 0 failures | 89 s |
    | motion-takes | as in 4a | 6 s |
    | xr-tape | 19 passed, 1 skipped | 25 s |
- **An empty hand at the chest could lock a distant item instead of opening the inventory.** Found by the full
  fp-smoke run: the laser pointed at a bottle 5.7 blocks off while the hand was at the chest, and the grip locked it
  for a pull. Far selection is now off at the chest (`VrGrab.Hover`).
- **The laser could not click any uGUI menu while the game window was not the focused desktop window.** Unity's
  `StandaloneInputModule.Process` returns early when `EventSystem.isFocused` is false. This was the cause of the
  vr-menu `controls` failures in full runs: another window had come to the front. `VrInput.KeepUiFocus` keeps the
  EventSystem focused while the laser drives the pointer, never touching OS focus or the window. Found and fixed in a
  side session; three full vr-menu runs in a row: 0 failures.

## Headless checks

New scenarios (xr-sim drives the hands; items spawned with `--cvr-dbg-spawn`):

| Scenario | Check | Result |
|---|---|---|
| `affordance` | within reach is shown (tick and log) | pass |
| | the hand half-closes | pass |
| | the grip takes the one shown | pass |
| | an empty grip makes a fist | pass |
| `pull` | pointing selects the item (aim 21.55°, −36.00°; 3.1 blocks) | pass |
| | the grip locks it | pass |
| | a flick pulls it into the hand ("pulled waterbottle(Clone) from 3.1 blocks … in hand") | pass |

Captures: `Working/hands-phase4b/affordance_*.png`, `pull-selected_*.png`.

Regression on test11 (hands follow the aim, three fingers and claws, fp-smoke in shared sessions):

| Suite | Result |
|---|---|
| `tools/fp-smoke.ps1 -Quick` | 22 checks in 125 s (the `poses: open` check then read only new log lines; fixed) |
| `tools/fp-smoke.ps1` (full) | 25/26 in 246 s; `poses: open` saw a bottle in reach of the raised hand. The pose shots now hold the hands higher: `-Only poses` 5/5 |
| `tools/vr-menu.ps1` | the `controls` action picker failed 3 checks in the full run again (it did in test10's too) and passed on its own rerun: a flake in the full run, not in 4b's code |
| `tools/vr-lifecycle.ps1` | 46/46 |
| xr-tape | 19 passed, 1 skipped |

Regression on test10 (the voxel hands and the marker):

| Suite | Result |
|---|---|
| `tools/fp-smoke.ps1` (all scenarios, with `affordance`, `pull` and `poses`) | 26/26 |
| `tools/vr-menu.ps1` | 0 failures (the `controls` action picker failed 3 checks in the full run, a known flake; it passed on its own rerun) |
| `tools/vr-lifecycle.ps1` | 46/46 |
| xr-tape | 19 passed, 1 skipped |

Regression on test9:

| Suite | Result |
|---|---|
| `tools/fp-smoke.ps1` (all scenarios, with `affordance` and `pull`) | 22/22 |
| `tools/vr-menu.ps1` (the *Hands & body* rows changed) | 0 failures |
| `tools/vr-lifecycle.ps1` | 46/46 |
| `tools/fp-comfort.ps1` | 0 failures |
| `tools/motion-takes.ps1` (synthetic) | 5 swings / 0 / 4 throws (4 cancelled swings) / 3 drops, as in 4a |
| xr-tape (`launch-dev.ps1 -XrSim -XrTape`) | 19 passed, 1 skipped (no depth layers) |

## For the headset

**Passed (2026-10-24):** test9 passed the pull, test10 the hands' size and fit, and test11 the rest: "hands and claws
are good and the glowing hand on items is good."
