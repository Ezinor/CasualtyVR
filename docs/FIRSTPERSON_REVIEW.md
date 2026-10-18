# FIRSTPERSON_REVIEW — making first person good (proposal for review, 2026-10-18)

This document reviews first person as of 1.2.2 against published VR design guidance and the VR Modding Playbook,
and proposes what to do next. **Nothing here is built yet.** The decisions in section 5 were made on 2026-10-18.

## 1. Where first person stands

**What works (your headset feedback, FP-0 to FP-7):**

- **The view:** scale, lighting, climbing a rope ("works really well") and swing attacks.
- **The body:** the body behind your eyes, blocky arms, objects and characters facing you.
- **Movement and text:** the 0.8× running speed, and readable speech.
- **Co-op (1.2.2):** windows, name tags and icons.

You chose an intense camera ("this is an intense game"), with comfort options opt-in and no highlights under your
hand.

**Still open from the first-person receipts:**

- hand poses (grip and point), noted since FP-2;
- damage vibration;
- a depth-writing body material.

**Still open from the improvement plan:**

- 10, sound follows the flat camera;
- 11, the head can enter blocks;
- 12, a scale pop after spawn.

## 2. What the guidance says (distilled)

### Comfort

- **Accelerations are what make people sick, not speed.** "The vestibular organs respond to changes in motion but
  not to constant velocity" ([Meta][meta-ux]). Keep accelerations brief and infrequent, and prefer quantised
  speeds (stop / walk / run) ([Meta][meta-ux], [Meta][meta-loco]).
- **Real-world speeds:** walk about 1.4 m/s, run about 2.8 m/s ([Meta][meta-ux]). Meta's web SDK caps slide
  locomotion at about 4–6 m/s ([Meta IWSDK][iwsdk]).
- **Vertical motion:** keep the camera's height steady over uneven ground, and change elevation only when needed
  ([Meta][meta-ux]). Players tolerate vertical motion they make themselves, such as climbing, much better than
  motion done to them ([Cloudhead][cloudhead]).
- **No camera shake or head-bob** ([Oculus guide][manning], [UploadVR][upload]). If shake is wanted, keep it in
  the periphery only, as an in/out motion with no tilt ([Hackaday][hackaday]).
- **Predictability:** players are more comfortable when they can anticipate camera motion ([Meta][meta-ux]).
- **Turning:**
  - Snap turn is the default nearly every shipped mod chose (playbook ch. 03, "the defaults these mods shipped").
  - Smooth turning should be opt-in ([Meta][meta-loco]); Cloudhead found smooth stick turning the worst option
    ([Cloudhead][cloudhead]).
  - A blink (short fade) helps a snap ([Meta][meta-loco]).
- **Head inside geometry:**
  - Fading to black is the common default. Pushing the view back scored best for immersion but worst for sickness
    ([IEEE VR 2018][ieee]), and developers call it "harsh".
  - The usual hybrid: light padding, then a fade for anyone who pushes through ([community survey][unityfade]).
- **Presets:** offer Recommended / Comfortable / Advanced ([Meta][meta-loco]). The conservative pattern is "cheap
  proven comfort ON, experimental comfort OFF" (playbook ch. 03).

### Hands and interaction

- **Hands as the main interface.** Players already know what hands do, so objects should answer the way hands
  expect ([TechCrunch][alyx-tc], [Engadget][alyx-eng]).
- **Affordance before action:**
  - Half-Life: Alyx's pull is select (glow plus sound), then lock-on (the target stays fixed while the hand
    moves), then a pull gesture ([Road to VR][alyx-rtvr]).
  - Without a visual highlight, the other channels are a hand pose that changes about 25 cm out and a haptic
    tick on hover, with the action firing about 8 cm out (playbook ch. 02, "an invisible zone needs a
    discoverability channel").
- **Throwing** (playbook ch. 02, "throwing, knocking and physical melee", from Quake VR):
  - Use the hand's **peak** velocity over about 150 ms, not the last frame's.
  - Detect release when the **analog grip falls about 30 % from its peak**.
  - Release the object **where the hand was** at that moment.
- **Melee gestures** (playbook ch. 02, "gesture detection", from FEAR VR):
  - Threshold the velocity **along the hand's forward axis**, not total speed.
  - Re-arm below a low speed rather than on a timer.
  - Use the cooldown the game itself uses.
  - Ignore samples more than 100 ms apart.
  - Gate on **what the hand holds**: a swing with a gun must not fire it (FAIL-HAND-006).
  - Test with labelled motion takes recorded in the headset (TEST-027).
- **Grip pose versus aim pose:** OpenXR's grip pose places what is in the hand; the aim pose points rays. On Touch
  controllers they are "tens of degrees apart" (playbook ch. 02).
- **Haptics come from accepted game events**: a hit that landed, damage taken, an item picked up. Not from raw
  button presses (playbook ch. 20).
- **Menus:** offer both laser and touch, so either first instinct works ([Road to VR][alyx-rtvr]).
- **Body:** floating hands can feel as present as a full body ([Springer 2021][springer]). Shoulders follow the
  head's position and the body's yaw, and lean towards a hand only near full reach (playbook ch. 12).

### Sound

The listener is the tracked head, after the same recenter and scale as the eyes (playbook ch. 20, "treat the
listener as a pose consumer").

## 3. Gaps (current code against the guidance)

| # | Area | Now (SOURCE: this repo) | Guidance | Gap |
|---|---|---|---|---|
| G1 | Speed | Top speed `19.9 blocks/s` (0.8 × 25) at 3.06 blocks/m = **6.5 m/s**. The game's own 25 = 8.2 m/s | Run about 2.8 m/s, walk about 1.4 m/s | More than twice a real run |
| G2 | Stick | Digital: the game reads keys, so `moveDir` is ±1. Any push means full speed | Quantised walk / run, player-chosen | No walk speed |
| G3 | Head in walls | Free; you can lean into blocks and see through them | Fade to black | Missing (plan item 11) |
| G4 | Vertical | The eye follows the head limb with one smoothing value for all axes. Every jump lifts the view about 1–2 m, and every landing dips it | Steady height, no bob | Landing dips and step bumps reach the eyes |
| G5 | Snap turn | 180° flip, instant | Snap plus a short blink | No blink |
| G6 | Comfort set-up | Separate settings (vignette 0, ragdoll Stay, smoothing 0.3) | Presets | No presets |
| G7 | Throw | Average velocity over the last ≤4 frames; release at grip < 0.4; the game throws from the character's hand | Peak over 150 ms, analog-grip release, release at the hand | Throws can feel weak or random |
| G8 | Swing | Total speed ≥ 2.6 m/s, fixed 0.45 s cooldown. Not gated on the held item | Forward-axis speed, re-arm, game cooldown, item gate | A swing with a gun or tool may fire or use it (to check) |
| G9 | Hand pose | Aim pose only: the bridge has no grip pose. Hands, arms and the held item hang off the aim pose | Grip pose for what is in the hand | Held items sit at the pointing angle |
| G10 | Hand shape | The character's side-view hand, no poses | Open / grip / point | Open since FP-2 |
| G11 | Grab affordance | None (highlights off by your choice) | Hand pose and haptic on hover | You cannot tell what a grip will grab |
| G12 | Sound | Listener on the flat game camera | Listener at the head | Wrong direction when facing left; head turns change nothing (plan item 10) |
| G13 | Haptics | Grab, mouth, swing, climb | Accepted events, including damage and hits that landed | No damage or hit-landed pulse (open since FP-4) |
| G14 | HUD | A see-through panel fixed in the room 1.2 m ahead; inventory at the chest radial | Wrist health / pockets (Alyx); laser and touch | The HUD floats in the corridor |
| G15 | Crouch | Stick only | Real crouching as an option (planned in FP-0, not built) | Missing |
| G16 | Scale pop | The world resizes once after spawn | — | Plan item 12 |

## 4. Proposed work, in packages

The sizes are rough: S is one short session, M one session, L more.

### FP-8 Comfort (first: it affects everything else)

| Item | What | Size |
|---|---|---|
| 8a Analog walking | Stick deflection scales the top speed through the existing `actualMaxSpeed` patch. Half push = walk, full push = the run speed setting | S |
| 8b Run speed in m/s | The setting is shown in real units: *Run speed 2.5 / 3.5 / 5 / game (6.5 m/s at today's 0.8)*. Default: your decision (D1). Note that **game balance is unchanged for creatures**, so a slower you is a harder game | S |
| 8c Head in walls | Fade to black while the eye is inside a solid block or outside the corridor's sides, with a short ramp (item 11) | S |
| 8d Vertical steadiness | Separate vertical smoothing; landing dips and step bumps under about 0.3 blocks are absorbed. Jumps and falls still move you | S–M |
| 8e Snap blink | An optional 80–120 ms fade on the 180° turn | S |
| 8f Comfort presets | *Intense* (today's), *Moderate* (blink, vertical steadiness, mild fall vignette), *Gentle* (also ragdoll → diorama, walk-speed cap). One menu row that sets the others | S |
| 8g Scale pop | Remember the measured eye height (item 12) | S |

### FP-9 Hands that feel right

| Item | What | Size |
|---|---|---|
| 9a Grip pose | Add the OpenXR grip pose to the bridge. Arms, hands, held items and throw velocity use it; rays and aim stay on the aim pose | M |
| 9b Hand poses | Open / grip / point from the game's own minigame hand art, driven by grip and trigger (open since FP-2) | M |
| 9c Grab affordance, no highlight | Within reach of something grabbable, the hand half-closes and gives one light haptic tick; the grip then grabs exactly that (lock it) | S–M |
| 9d Throw recipe | Peak velocity over 150 ms; release when the analog grip falls 30 % from its peak; the item placed at your hand before the game's throw (if the game allows) | M |
| 9e Swing recipe | Forward-axis speed, re-arm below 0.8 m/s, the game's own attack cooldown, sample-gap guard, gate by held item (melee or bare hands only). First check whether swinging a gun fires it | S–M |
| 9f Motion takes | Record a few labelled swings and throws in the headset; replay them under xr-sim as a regression test (TEST-027) | M |
| 9g Distance pull (optional, D4) | Alyx-style: point at an item within the game's own reach (10 blocks), grip to lock (tick), flick towards you to pull it into the hand. Uses the game's own pickup, so nothing becomes reachable that the game refuses | M |

### FP-10 Senses

| Item | What | Size |
|---|---|---|
| 10a Sound at the head | The listener at the tracked head, facing where you face, with distance falloff kept close to the flat game's (item 10) | M |
| 10b Haptics from game events | Damage taken (scaled by the game's own pain), a hit that landed (not just a swing), low-health heartbeat (optional) | S–M |

### FP-11 Information within reach (D3)

| Item | What | Size |
|---|---|---|
| 11a Wrist HUD | The game's HUD (health, status, held item) on the left wrist: raise and look at it to read. The floating corridor panel becomes an option | M–L |
| 11b Body zones | Keep chest = radial. Add over the shoulder = inventory, and the hip for what the game has room for. Each zone ticks on hover so you can find it | M |

### FP-12 Movement verbs

| Item | What | Size |
|---|---|---|
| 12a Real crouch | Crouching in the room crouches the character (option; height threshold measured at recenter) | S–M |
| 12b Ladders and ropes | Check getting on and off at the top and bottom. Meta flags these transitions as the risky moments ([Meta][meta-climb]) | S, headset |

Suggested order: FP-8, then 10a and 9e (small, high value), then the rest of FP-9, FP-10 and FP-11.

## 5. Decisions (user, 2026-10-18)

| # | Question | Decision | Effect on the packages |
|---|---|---|---|
| D1 | Default run speed in first person | **Keep 6.5 m/s** | 8a analog walking still applies: a half push walks. 8b only shows the setting in m/s |
| D2 | Default comfort preset | **Intense** (today's) | 8f builds the presets; Intense is the default |
| D3 | Where the HUD lives in first person | **Both, with the wrist as default** | 11a: the wrist HUD on by default, the floating panel an option |
| D4 | Distance pull (Alyx-style) | **Yes** | 9g is in scope |
| D5 | Grab affordance | **Faint glow** as well as the hand pose and haptic tick | 9c adds a faint glow on the locked target. This replaces the "no highlights" default for grabbable targets only; the game's block marker and item outline stay off |
| D6 | Real crouch | **On by default** | 12a defaults on |

## 6. How it would be tested

- **Headless (xr-sim):**
  - scripted head and hand poses, as in `fp-smoke.ps1` and `vr-lifecycle.ps1`;
  - logged speed in m/s and the vertical eye path while walking, jumping and landing;
  - fade-on-wall captures;
  - the throw and swing detectors replayed from recorded motion takes.
- **Headset:** a short checklist per package, in the style of the receipts' "for the headset pass" sections.
  Comfort items (8d, 8e, 8f) can only be judged there.

## Sources

[meta-ux]: https://developers.meta.com/horizon/design/locomotion-comfort-usability/
[meta-loco]: https://developers.meta.com/horizon/design/locomotion-best-practices/
[meta-climb]: https://developers.meta.com/horizon/design/locomotion-virtual-environments/
[iwsdk]: https://developers.meta.com/horizon/documentation/iwsdk/concepts/locomotion/slide/
[cloudhead]: https://gamedeveloper.com/design/lessons-learned-from-five-years-of-vr-locomotion-experiments
[manning]: https://livebook.manning.com/book/oculus-rift-in-action/chapter-7
[upload]: https://uploadvr.com/five-ways-to-reduce-motion-sickness-in-vr/
[hackaday]: https://hackaday.com/2019/01/22/screen-shake-in-vr-minus-the-throwing-up/
[ieee]: https://downloads.hci.informatik.uni-wuerzburg.de/2018-ieeevr-simsick.pdf
[unityfade]: https://forum.unity.com/threads/how-do-you-handle-not-going-through-walls-in-steamvr-using-fading-to-black.811362/
[alyx-tc]: https://techcrunch.com/2020/03/23/half-lifealyx-delivers-the-watershed-moment-vr-gaming-needs/
[alyx-eng]: https://www.engadget.com/2020-03-31-half-life-alyx-valve-interview-vr.html
[alyx-rtvr]: https://www.roadtovr.com/these-details-make-half-life-alyx-unlike-any-other-vr-game-inside-xr-design/
[springer]: https://link.springer.com/article/10.1007/s10055-021-00530-5

- **Meta:** Locomotion comfort and usability · Locomotion best practices · Locomotion in virtual environments ·
  IWSDK slide locomotion
- **Cloudhead Games:** Lessons learned from five years of VR locomotion experiments
- **Comfort write-ups:** Oculus Rift in Action, ch. 7 · UploadVR, five ways to reduce motion sickness · Hackaday,
  screen shake in VR
- **Research:** Würzburg, IEEE VR 2018 poster on head collisions · Springer *Virtual Reality* 2021, avatar
  representation and presence
- **Half-Life: Alyx:** TechCrunch and Engadget coverage · Road to VR, "VR Design Unpacked"
- **Community:** Unity forum thread on fading to black at walls
- **VR Modding Playbook:** ch. 02 (throwing, gestures, discoverability, grip versus aim), ch. 03 (shipped
  defaults, stick-as-buttons, lean), ch. 12 (shoulders), ch. 20 (listener, haptics); patterns TEST-027; failures
  FAIL-HAND-006, FAIL-HAND-055
