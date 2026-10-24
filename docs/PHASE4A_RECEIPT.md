# PHASE4A_RECEIPT — hands: grip pose, swing and throw recipes, motion takes (2026-10-23)

Phase 4a of [BUILD_PLAN.md](BUILD_PLAN.md): FP 9a, 9e, 9d and 9f of [FIRSTPERSON_REVIEW.md](FIRSTPERSON_REVIEW.md). Hand
poses, the grab affordance and the distance pull are 4b. Test build: `1.3.0-test8`, which contains Phases 1–3.

- **Backup before the changes:** `Working/backups/pre-phase4-20261023`.
- **Code:**
  - the bridge (`XrBridge.h`/`.cpp`: the grip pose);
  - `XrNative`;
  - `VrInput` (`GripPose`, `GripTracking`);
  - `VrArms` and `VrHands` (on the grip pose);
  - [`HandMotion.cs`](../src/CasualtyVR.Core/HandMotion.cs) (new: the recipes, `MotionTakes`);
  - `VrGrab` (uses them);
  - `VrSettingsMenu` (the recording row);
  - `VrRig` (diagnostics);
  - `Plugin` (`--cvr-dbg-replay`).
- **Tests:**
  - [`tools/motion-takes.ps1`](../tools/motion-takes.ps1) (new);
  - `tools/fp-smoke.ps1`: the new `swing-gate` scenario, the throw check, and it now reads the player log.

## 9a The grip pose

The bridge now has a second pose action per hand, `/input/grip/pose`, bound on every profile: simple, Vive, WMR,
Index, HP and Touch. It is located at the same display time as the aim pose, with the same 0.5 s dropout hold, and
`CvrHand` gains `gripOrientation`, `gripPosition` and `gripValid`. If the grip is not located, everything falls back
to the aim pose. The bridge logs the first location of each hand ("controller right: grip pose located …").

Who uses which pose (playbook ch. 02, "grip pose versus aim pose": on Touch they are tens of degrees apart):

| Grip pose (where the hand holds) | Aim pose (where it points) |
|---|---|
| Your arms in first person: the wrist sits behind the palm, and the hand and **what it holds point along the controller's handle** | The laser, the cursor, menu clicks |
| The diorama's controller hands | First-person aiming into the world |
| Grabbing, the chest and the mouth, climbing | |
| Throw velocity and the throw's start point; swing detection | |
| Motion takes | |

## 9e The swing recipe

`HandMotion.Swing` uses the right hand's room-space grip position, timed by the mod's own clock (playbook ch. 02:
never the runtime's predicted display time). It follows FEAR VR's gesture rules, adapted to this game:

| Rule | Value | Was |
|---|---|---|
| Fast | ≥ 2.6 m/s over the last ~3 frames | same |
| Not a retraction | rejected if the velocity points back towards the shoulder (component < −50 %) | not checked |
| One swing per motion | re-arms only once the hand has slowed below **0.8 m/s** | a fixed 0.45 s timer |
| Gap guard | samples more than **100 ms** apart start over (a hitch is a teleport) | none |
| The game's rate | no attack while `body.attackCooldown > 0` (each weapon's own cooldown) | the timer |
| **Held-item gate** (FAIL-HAND-006) | swings only with bare hands, with items that have no left-button action (the game punches), or with `tool`-tagged items | none |
| Throw or swing? | with an item in hand, the swing waits until the hand has passed its fastest (≤ 150 ms) and is cancelled if the grip lets go first | none: a throw's flick also attacked |

FEAR thresholds the hand's **forward axis** because its melee is a thrust. This game's attack is a swing towards the
mouse, so the swing's own direction counts, and retractions are rejected instead.

**The gun check, from the game's code:** `PlayerCamera.HandleAttacks` → `Body.UseItemInHand`. If the held item has a
left-button action (`usable && usableWithLMB`), that action runs. For guns, `GunScript.triggerPressed = true`, so
**a swing with a gun in hand would have fired it.** Otherwise it is a punch.

The 38 left-button items fall into two groups:

- **Melee:** every melee weapon and tool (29: machete, sickle, pickaxe, shovel, wrench…) carries the `tool` tag, and
  all of them attack.
- **Not melee:** guns (4), placeables (scrap metal, climbing rope, scaffolding), lights, the rangefinder and the
  grappling hook.

## 9d The throw recipe

From Quake VR's throwing review:

- **Release:** while the hand holds an item, it lets go when the analog grip falls **30 % below its peak** since the
  grab. Before, it let go at a fixed grip of 0.4, which is late.
- **Velocity:** the fastest two-sample velocity in the last **150 ms**, frozen at the release. Before, it was the
  average of the last 4 frames, which decays exactly at release.
- **Start point:** after the game's own `ThrowItem`, the item is moved to **where your hand was at the release**,
  provided it is within 4 blocks of the body and no block is in between (a `Ground` linecast). Before, it started
  from the game's hand. The direction is the hand's, and the force comes from its speed (the game's own throw speed
  stays).

## 9f Motion takes

- **Recording:** *Hands & body › More settings › Record a motion take*, with three options: **Swings** (do five
  swings), **Throws** (five throws) and **Other** (ordinary moves that should not attack). The menu closes, five
  ticks count down, then it records 20 s between a long buzz at the start and one at the end. It writes both hands'
  grip poses, grip, trigger, whether the hand holds something, the head, and the live detections, to
  `BepInEx/CasualtyVR/takes/<label>-<time>.csv`.
- **Replay:** `--cvr-dbg-replay <folder|synthetic>` runs the same `HandMotion` code over the takes at plugin start
  and logs swings, throws, drops and cancelled swings for each.
- **Check:** `tools/motion-takes.ps1 [-Takes <folder>]` checks the counts per label. It copies recorded takes next to
  the dev copy first, read-only from wherever you point it.

Until you record real takes, generated ones stand in, at 90 Hz with noise:

| Take | Motions | Result |
|---|---|---|
| synthetic-swings | 5 arcs at up to about 4.4 m/s | **5 swings** |
| synthetic-other | 5 slow reaches (1 m/s) and **5 fast retractions** (5 m/s, back towards the shoulder) | **0 swings** (the old detector: 5) |
| synthetic-throws | 4 grabs, flicks of up to 5.8 m/s, the grip let go to 55 % mid-flick | **4 throws, 0 swings, 4 swings cancelled** (the old detector would also have attacked) |
| synthetic-drops | 3 slow hand openings at rest | **3 drops** |

## Checks

| Suite | Result |
|---|---|
| `motion-takes.ps1` (generated) | 4/4 takes as expected |
| `fp-smoke.ps1` | **15/15**: the pickup and throw, now "threw waterbottle … (peak over 150 ms) … from the hand"; chest store and radial; mouth and vibration; touch; swing; **grip pose located**; **the pistol is not swung** ("swing not used with pistol"); **the machete swings**; lockpick |
| `vr-lifecycle.ps1` | 46/46. The bridge's input struct grew, and focus, dropout and IMGUI input still pass |
| `vr-menu.ps1` | 0 failures. *Hands & body › More settings* has the recording row, and every control is still ≥ 2.6° |
| `fp-comfort.ps1` | 0 failures |
| xr-tape | 19/19 (1 skipped, as before) |

**fp-smoke now reads the Unity player log.** It read BepInEx's buffered `LogOutput.log`, so the chest-store line was
sometimes not there yet when checked. That was the failure seen once in Phase 3, and once more here before the fix;
the store itself worked (the player log had "stored waterbottle(Clone) in slot 2").

## Test build

`dist/CasualtyVR-1.3.0-test8.zip` (1033 KB), SHA-256 `FBCCB6EAF34B96EDA13BB6B0C711FDA92454842A7C77E3AF93A542CEB54EC9B5`.
The native bridge changed in this build, so install both DLLs.

The dev config was restored after every run.

## For the headset pass

1. **Arms and held items:** with something in your hand, it should now point along the controller's handle, like
   holding a stick, not up the laser. Does the hand look right, or is the wrist bent too much?
2. **Swings with a melee item and with bare hands:** one attack per swing. No attacks when you pull your hand back
   or reach slowly. With a gun in hand, a swing does nothing (the trigger still fires).
3. **Throws:** they should carry the flick and start from your hand. A slow opening of the hand drops.
4. **Record takes:** under *Hands & body › More settings › Record a motion take*: one *Swings* (5 swings), one
   *Throws* (5 throws: pick something up each time) and one *Other*. Then run
   `tools/motion-takes.ps1 -Takes "<game folder>\BepInEx\CasualtyVR\takes"`.
