# FP0_RECEIPT — first-person spike (2026-10-05)

Plan: `docs/FIRSTPERSON_PLAN.md`. Pre-change source backup: `Working/backups/pre-fp0-20261005/`.
Status: **round 1 accepted in the headset (2026-10-05)**: the camera is comfortable, and block scale, tunnel width and lighting
feel good. Round 2 (below) fixes the two things reported then.

## What it does

- **Toggle:** F7 (`FirstPerson.ToggleKey`), or *VR settings > First person* (new tab: on/off, tunnel sides, camera
  smoothing, tunnel width, world scale, recenter). Off by default. `--cvr-dbg-fp` forces it on for one session.
- **When it applies:** only in a level. Menus, loading and the end screen keep the theatre panel. Verified: death
  (`--cvr-command kill`) switches back to the theatre ("First person: off").
- **Camera:**
  - Your head is placed at the character's head limb, and F9 recenters so that your current head pose is the
    character's eye. Rotation is always your own (CAM-001).
  - Position is smoothed (`FirstPerson.Smoothing` 0.3) and snaps on teleports of 5 or more units.
  - It stays in first person through ragdoll and knockdowns (user decision).
- **Space mapping (`VrSpace`):** one tracking-to-world mapping shared by the eyes, the controllers, the laser, the
  controller hands and the UI panels. The diorama path is numerically unchanged. In first person, your forward is
  the character's facing along world x (yaw ±90°).
- **Scale (CAM-018):**
  - Measured: the character's eye is **4.91 blocks** above its feet, and its collider is 5.0 blocks tall.
  - Automatic scale: 4.91 / 1.6 m = **3.07 blocks per metre**, so **one block ≈ 33 cm**.
  - Overridable with `FirstPerson.UnitsPerMetre`.
- **Corridor:**
  - In first person the voxel slab is re-centred on the play plane, `±CorridorWidth/2`, with a default of 4 blocks
    (1.3 m). Actors on the play plane are therefore straight ahead, not beside your face.
  - The chunks rebuild when the width changes.
- **Sides (`FirstPerson.Sides`):**
  - **CaveWalls (default)**, back side: the game's per-chunk background walls (PopupDepth puts them at the back
    plane). Front side: darkened, mirrored copies of those walls, plus copies of background tilemaps (the lifepod's
    and buildings' interiors).
  - **Void:** black, using the greenscreen path with a black key.
- **Back-wall objects:** machines and buttons set into walls carry Collider2D objects, which can never be moved.
  - Extruded ones (`ObjectExtruder`) are placed against the back wall, and their decorations go onto the extruded
    front face.
  - Non-extruded ones are drawn as copies on the back wall, with the originals hidden in the eyes.
  - Clicks still reach them, because the game is 2D.
- **Hidden in the eyes:** the character's own body (including held items, until FP-2), the game's line-of-sight
  mask, the diorama frame, and the fog mist (fog in first person is FP-1).
- **Input:**
  - Left stick forward/back walks the way you face.
  - Right stick up/down climbs or crouches.
  - A right-stick flick sideways turns you around.
  - Buttons keep their mapping.
  - **Aim:** the right-hand ray is marched through the corridor and stops at the first block, item or creature (the
    game's Item/Body/Limb layers, not your own body), or at a side wall, within the game's own 10-block interaction
    range. That point becomes the mouse.
  - With the dashboard open, the laser drives the panel as before.
- **HUD:** a see-through panel 1.2 m ahead of the recentered pose.

## Lighting (risk 1 of the plan) — found and partly solved

- **The problem.** URP's 2D lights are meshes in each light's xy plane, rendered into a screen-space light texture.
  From inside the play plane they are edge-on: in the first capture, half the view (everything lit) was black.
- **The fix (`FirstPerson.FaceLights`, on by default):** while an eye renders, each non-global light within reach
  turns to face the eye, and its z is clamped into the corridor; one wall-hole light sat at z = 1090.
  - The character's own `EyeLight` (Head, point light, radius 26) is at the eye. It is drawn as a disc half its
    radius ahead of the eye, like a headlamp, with its 2D shadows off while it is moved. Its shadows are cast from
    its 2D position, so ahead of you it sat inside rock and darkened everything (tested; debug modes
    `--cvr-dbg-lightmode 0..3`).
  - Lights on physics objects (held or dropped lamps; 246 in seed 1234) are left alone: moving them would make
    Physics2D resync. Proxy lights for these are FP-1.
- **Result in the lifepod:**

  | Version | Lit part of the view |
  |---|---|
  | Lights left as they are | 43% |
  | Lights turned to face the eye | 52–61% |

  The room reads as a room: screen on the back wall, heater extruded against it, door and tunnel ahead
  (`Working/xrsim-shots/fppodc_fwd_left.png`).
- **Deep caves are as dark as the flat game is.** At the same spot with first person off, the monitor view was
  nearly black around the character as well (`fplm3_desktop.png`, `dio_cave_desktop.png`).
- **Known artefact:** a vertical brightness seam where a light's disc plane passes close to the eye.

## Verification (xr-sim, seed 1234)

| Check | Result |
|---|---|
| Build | 0 warnings, 0 errors |
| xr-tape, diorama | 19/19 (1 skip) |
| xr-tape, `--cvr-dbg-fp` | 19/19 (1 skip) |
| Camera direction (verbose `fp:` log) | forward (1, 0, 0), rotation y 90, scale 3.07 |
| Lifepod captures | `fp3_*`, `fppodc_*` |
| Cave captures (`--cvr-dbg-cave`: teleports into the nearest 14×8 open tunnel) | `fpcave*`, `fplm*` |
| Death → theatre | first person off, theatre on |

**Frame rate in xr-sim:** 40–80 fps while capturing. This is not representative of the headset; it needs a headset
reading.

## Not in FP-0 (next phases)

- **Visuals (FP-1):** items, creatures and other characters are still flat sprites, seen almost edge-on (thin
  slabs). Billboards come in FP-1, along with block cracks and hover squares on the faces you see, fog, proxy
  lights for lamps, distance darkness for Void, and the seam.
- **Hands and body:** no hands or arms yet (FP-2). Torso and legs come in FP-5, which is required before release.

## Round 2 (2026-10-05): look pinned to the head; brightness seam

User: "the hand/mouse controls where the character's head/camera look so when not in a menu I suppose that will have
to be pinned to the head/eye", and the brightness seam.

- **Why the camera moved when you pointed:** every frame the game turns the head and torso toward the mouse
  (`targetLookPos`, `Body` around line 3391) and flips the body when you aim behind it. The camera rode the head limb.
- **Look pinned to your head:** outside menus, `FirstPerson.PinLook` writes the game's own look override
  (`overrideLookPos`/`overrideLookTime`, the same mechanism corpses and scary objects use) from your gaze each
  frame. Your hand still aims attacks and clicks.
  - Settings: *Character looks where you look* (on), and *Aim with* Hand/Head (Hand). Head makes the mouse follow
    your gaze.
- **Camera anchor:** standing, the view keeps to the body's centre line, with only the head's height used.
  Ragdolled, it still rides the head limb.
  - Headless check: pointing the hand forward, up, down and behind, with attacks, left the eye where it was. The
    only movement was the character standing up.
- **Reticle:** in first person the cursor is a small diamond in 3D where the aim ray stops, at a constant apparent
  size, drawn on top.
- **Seam cause:** found by elimination (glow sprites, particles, liquids, shadows and area lights all ruled out;
  shifting the eye showed a boundary at z ≈ 0). The cause was lights on physics objects: `LifePodLight`, a radius-35
  point light 12 blocks above the pod, carries a Collider2D, so it could not be turned. Its mesh, edge-on in the
  play plane, lit everything on one side of that plane.
- **Seam fix, part 1 — copy lights.** Lights on physics objects now get a copy light of our own (serialized settings
  copied with `JsonUtility`). The copy is lit only while an eye renders, so the monitor is unchanged, and the
  original is dark in the eyes.
- **Seam fix, part 2 — even light.** Any point light whose range contains the player becomes an even light over the
  whole view, at the strength it has at the player's position (linear falloff). This applies to copies and to
  ordinary lights alike, except the character's own `EyeLight`, which stays the headlamp.
- **Result in the lifepod:** no seam, with the room evenly lit (`fpamb_fwd_left.png`).

  | Version | Lit part of the view |
  |---|---|
  | Before | ~45% |
  | After | 70% |

- **Also changed, as side effects of the hunt:**
  - Particles face the eye in first person (they lay flat in the play plane before).
  - Big see-through sprites on the play plane, and big flat physics sprites, go to the back wall.
  - 2D light shadows are off in the eyes (`FirstPerson.NoLightShadows`).
- **Checks:** xr-tape 19/19 in the diorama and in first person.
