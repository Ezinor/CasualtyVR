# FP5_RECEIPT — first person: your body (2026-10-05)

Plan: `docs/FIRSTPERSON_PLAN.md` (FP-5, required before the release). Source backup: `Working/backups/fp5-20261005/`.
Status: **built and verified headless; how it looks and feels needs the headset**.

## What it does (`BodyView.cs`, settings *Your body*, *Body thickness*, *Body back offset (m)*)

- **The torso and legs** (`Body.limbs` UpTorso 1, DownTorso 2, ThighF/CrusF/FootF 9–11, ThighB/CrusB/FootB 12–14),
  and every sprite under them, are drawn in first person as pixel-extruded meshes. This reuses
  `ObjectExtruder.MeshFor`, the extrusion used for crates and capsules. A flat side-view sprite seen from above
  would be a line.
- **Depth per part:**

  | Part | Depth (blocks) | Approx. |
  |---|---|---|
  | Upper torso | 0.95 | 30 cm |
  | Lower torso | 0.85 | |
  | Thighs | 0.55 | |
  | Shins, feet | 0.45 | |
  | Attachments | limb + 0.08 | |

  All of it is scaled by *Body thickness*.
- **Placed at the real limbs:** the copies sit exactly where the game's limbs are, so the walk cycle, crouch, lean
  and ragdoll come through. The originals stay hidden in the eyes, and the head stays hidden.
- **Damage and effects, as the game shows them:**
  - Blood, wounds and wetness are in the limb materials, used as-is.
  - Bandages, splints and clothing are child sprites, extruded with their limb.
  - Broken and dislocated limbs show through the game's pose.
  - A missing limb is gone because its original sprite is hidden.
- **Draw order:** the body draws after the world (sorting 29000 and up) and before the arms (30000 and up).
- **The character faces the way you face** (`Body.SwitchDir` when they differ, not mid-attack). Before this, the
  character could still face the way it lay down, so its tail ran out ahead of you (seen in a capture).

## Verification

| Check | Result |
|---|---|
| Build | 0 warnings, 0 errors |
| xr-tape, diorama | 19/19 |
| xr-tape, first person | 19/19 |
| Exceptions in the log | none |
| Captures, looking down | `fp5cave_*` and `fp5pod*`: the extruded torso below the eye, legs below it. `fp5pod2_*` is after a crystal creature's attack, with the game's red pain wash. |

## Limits and open

- **Seen from above,** you mostly see the extrusion walls, which take their edge pixels' colours. Wounds and blood on
  the faces show at an angle. A depth-writing material (as the blocks use) would also fix wall ordering artefacts.
- **The game's art is dark,** so the body is dark too.
- **Thickness and the back offset** need tuning in the headset.
- **Next:** FP-6, settings polish, docs and release. The headset round on FP-1 to FP-5 comes first.

## Round 2 (2026-10-05): headset feedback

- **Feedback:** "The body seems too far forward, it really gets in the way visually." Climbing a rope "seems to
  work really well". Swinging works well. Vibration was not noticed; the user will test again.
- **Body behind the eyes:**
  - The cause: the character's eyes are in its head, forward of its torso, while the view keeps to the body's centre
    line, so the torso filled the view below and ahead of you.
  - The drawn body now sits behind the eyes along the character's facing (`FirstPerson.BodyBehindEyesMetres`,
    default 0.3 m, menu range −0.2 to 1.0). This is a new config key, so saved configs pick up the new default.
- **Blocky arms (user request):**
  - The arms are now pixel-extruded blocks like the body and the crates: limbs 0.42 blocks thick (about 13 cm),
    with the held item and attachments slightly proud. *Arm thickness* in the menu, 1 = default.
  - Sprites that cannot be extruded fall back to a flat outline mesh.
  - Capture `fp5c_arms_left.png`: chunky forearms and the lantern held as a block.
- **Checks:** xr-tape 19/19 in both views, no exceptions.

## Round 3 (2026-10-05): objects face you

- **Feedback:** "objects like the plants should be facing me directly, not sideways."
- **Cause:** plants (glowplant, stoneplant, leadbush, drybush, bananaplant) are BuildingEntity objects with a
  BoxCollider2D. `ObjectExtruder` extrudes them as blocks in the play plane, so in first person their art faced the
  tunnel walls.
- **Fix:** in first person, extruded objects in the tunnel (sorting order above −100: plants, crates, doors,
  capsules) turn about their own vertical axis to face your head, keeping their depth (`FirstPerson.FaceYaw`).
  - Their attached decorations, placed on the front face by PopupDepth, turn with them.
  - Machines on the back wall stay on the wall.
  - Setting: *Objects face you* (`FirstPerson.FaceObjects`, on).
- **Lighting tweak, made while checking:** the "even light" treatment (a point light whose range contains you lights
  the whole view) now applies only to large lights (radius 15 blocks or more, such as the lifepod's ceiling light).
  Smaller nearby lights, such as glow plants, are drawn as a disc ahead of the eye in their own direction.
- **Note:** the dev config has `Passthrough.Greenscreen = true`, left as the user set it, so the captures
  (`fp5face*`) show green tunnel sides.
- **Checks:** xr-tape 19/19 in both views, no exceptions.
