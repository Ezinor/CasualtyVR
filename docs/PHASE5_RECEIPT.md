# PHASE5_RECEIPT — senses and movement: sound at the head, game haptics, real crouch, ropes (2026-10-25)

Phase 5 of [BUILD_PLAN.md](BUILD_PLAN.md): FP 10a, 10b, 12a and 12b of [FIRSTPERSON_REVIEW.md](FIRSTPERSON_REVIEW.md)
(10a = [IMPROVEMENT_PLAN](IMPROVEMENT_PLAN.md) item 10), with decision D6 (real crouch on by default). Test build:
`1.3.0-test13`.

- **Backup before the changes:** `Working/backups/pre-phase5-20261025` (HEAD, dev config, deployed plugin).
- **Code:**
  - [`Ears.cs`](../src/CasualtyVR.Core/Ears.cs) (new): the listener at the head;
  - [`GameHaptics.cs`](../src/CasualtyVR.Core/GameHaptics.cs) (new): vibration from game events;
  - `VrInput.RealCrouch` and `FirstPerson`: real crouch and the eye anchor; `FirstPerson.ClimbTransitions`;
  - `VrRig`: both features under fault isolation ("sound at head", "game haptics"), test hooks `--cvr-dbg-rope` and
    `--cvr-dbg-hurt`;
  - `Plugin`, `VrSettingsMenu`: six settings;
  - the project now references `UnityEngine.AudioModule`.
- **Tests:** `tools/fp-comfort.ps1` session 2 gains `crouch`, `sound`, `rope` and `haptics`; fp-smoke's machete
  session checks that a hit that lands vibrates.

## 10a Sound at the head (FAIL-AUDIO-001, FAIL-AUDIO-004; AUDIO-001, AUDIO-002)

The game hears through an AudioListener on its camera ('Main Camera'), which never turns and sits **3 blocks off the
play plane**. In first person:

- the mod's own listener follows the VR head, position and rotation;
- the game's listener is muted.

Moving the ears into the plane would make 3D sounds louder: with Unity's log rolloff, a sound at your feet went from
1/3 gain to full gain. So each 3D source's rolloff becomes a custom curve with the flat game's loudness at the same
distance along the plane: gain(d) = flatGain(√(d² + 3²)).

- **New sounds:** those from the game's `Sound.Play` are converted as they start (Harmony postfix).
- **Other 3D sources:** every 120 frames (Scans phase 16).
- **2D sounds:** music and UI (spatialBlend 0) are untouched.
- **Putting it back:** leaving first person, VR stopping, or the feature failing restores the game's listener and
  every source's rolloff.

## 10b Vibration from game events (HAND-003)

| Event | Signal | Pulse |
|---|---|---|
| Hurt | the game's screen shake (`Shaker.Shake`, before the shake setting scales it): impacts by force, broken bones, dislocations, traps, explosions, earthquakes | both hands, 0.4 at shake 50, 0.6 at 100, full from 200; 40–240 ms |
| Hurt, no shake | the character's limb health dropping, or its pain rising (bites, cuts, shots) | both hands, 0.25 + lost/40 |
| A hit that landed | `Body.Attack` returning true (it hit a block, an animal or a building) | right hand, 0.45 + damage/60 |
| Heartbeat (optional, off) | danger from the game's vital signs: consciousness < 85, blood pressure < 95, oxygen < 90, heart > 140 | left hand, lub-dub at the game's simulated heart rate; none when the heart stops |

At most one damage pulse every 80 ms. Everything goes through `VrHaptics.Pulse`, so the *Vibration* switch and
strength apply.

## 12a Real crouch (D6: on by default; HAND-004)

- **Trigger:** your head 0.3 m (*Crouch for real*, 0.15–0.6 m) below its height at the last recenter holds the
  game's crouch key, with 8 cm of hysteresis.
- **Exceptions:** not on a rope or ladder (where "down" climbs down), not in water (where it dives), not while
  ragdolled.
- **The view:** while it lasts, the eye anchor stays at the standing eye height above the feet, so only your real
  head moves the view; the character's crouch would otherwise lower it a second time.
- **The game rule it removes** (HAND-004, "stand up and see through the level"): under a low ceiling the anchor comes
  down so the view stays 0.3 blocks below it however far you stand up.

## 12b Ropes and ladders

`FirstPerson` logs the view's largest one-frame move in the 0.6 s after getting on or off. With a rope hung by
`--cvr-dbg-rope` beside the player in the test tunnel:

| Transition | Largest one-frame move | Over 0.6 s |
|---|---|---|
| On (right stick up) | 0.046 m | 1.61 m (climbing) |
| Off (right stick down to the ground) | 0.054 m | 0.94 m |

Both are smooth, so no change. Getting off at the **top** of a ladder onto a ledge is not in the test tunnel; that
is for the headset.

## Headset round: test13 → test14 (2026-10-25)

All four passed: sound, crouch, vibration and ladders ("all of these pass").

| Feedback | Cause | Change in test14 |
|---|---|---|
| Gripping the chest opens the inventory "too far away from my chest" | the chest zone (from before Phase 5) was a 22 cm ball round a point 10 cm ahead of the eyes: a fist about 30 cm ahead of the eyes, 20 cm in front of the chest, counted | `VrGrab.AtChest` is a box in your facing: at most 16 cm ahead of the eyes (the grip point is inside the fist, a few cm off the chest), 10 cm behind, 22–58 cm below the eyes, 20 cm to either side. The radial's log line gives the hand's position, for tuning |

fp-smoke `chest-store` gains "a hand 30 cm out does not open the inventory" (the old ball accepted it); `-Quick`
25/25, including storing at the chest and the machete stored there.

## Headless checks

fp-comfort session 2 (cave, Gentle preset):

| Check | Result |
|---|---|
| crouch: the head down crouches | pass: "Crouch: real on (head 0.45 m below …)" |
| crouch: the view is your head's height (no double drop) | pass: view 1.15 m above the feet = standing eye 1.60 − 0.45, character crouch 1.00 |
| crouch: standing up stands | pass |
| sound: the game's listener is off in first person | pass |
| sound: ahead, then behind when you turn your head | pass: z +3.00 → −3.00 |
| sound: the flat game's loudness | pass: 0.223 against 0.222 |
| rope: hangs, climbs on, climbs off | pass (table above) |
| haptics: the heartbeat beats at the heart rate | pass: 70 bpm with `--cvr-dbg-danger` |
| haptics: being hurt vibrates | pass: dislocation, shake 50 → 0.40 for 0.09 s |

Capture: `Working/senses-phase5/crouch_*.png`.

fp-smoke's machete session:

| Check | Result |
|---|---|
| A miss does not count as a hit | pass: swings at the air log no "hit landed" |
| A hit that lands vibrates | pass: chops at the floor, "hit landed (50 damage) → 1.00" |

The first full run failed the hit check because the test's swings never touched anything: a hand at chest height,
swinging forward, cannot reach the floor 4.5–5.9 blocks from the torso. The test now chops downward too. The light
pulse every swing used to get (0.6) is now a tick (0.25), so a hit that lands feels different from a miss. The
game's own life-pod landing shake at the start of a run also vibrates (shake 400, full strength); that is the game's
event, so it stays.

Regression (`tools/regress.ps1`, 902 s):

| Suite | Result |
|---|---|
| fp-smoke | 26/27; the hit check, fixed as above (`-Only swing,swing-gate` 6/6 after) |
| vr-menu | 0 failures (the new rows pass the layout check) |
| vr-lifecycle | 43/43 |
| fp-comfort | 0 failures in 138 s, Phase 5 checks included |
| motion-takes | as in 4a |
| xr-tape | 19 passed, 1 skipped |

## For the headset (test13; passed)

- **Sound:** a sound in front of you while facing left, then while facing right; turn your head and listen for it
  to move. Is the overall loudness as before?
- **Crouch:** crouch down for real. Does the character crouch, and does the view stay with your head? Try a
  crouch-only tunnel and stand up in it. Is 30 cm the right depth?
- **Vibration:** take a fall or a hit; hit something with a melee weapon, then miss.
- **Ladders:** get on and off at the top and at the bottom.
