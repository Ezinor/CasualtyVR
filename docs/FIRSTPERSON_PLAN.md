# FIRSTPERSON_PLAN — first-person mode with character hands (draft, 2026-10-05)

Status: **plan agreed 2026-10-05 (decisions in section 8). FP-0 accepted in the headset (`docs/FP0_RECEIPT.md`); FP-1 to FP-6 built; released as 1.1.0 (`docs/RELEASE_1.1.md`); receipts `docs/FP0_RECEIPT.md` to `docs/FP5_RECEIPT.md`.** It builds on 1.0.3 (voxel diorama, controller input, VR UI panel, minigame
hands). The existing diorama view stays the default. First person is a second view that you switch to; it does not
replace the diorama.

## 1. The idea in one paragraph

The game is a side-on 2D plane, but Phase 4 already turned it into a slab: blocks run from `FrontDepth` in front of
the play plane to `BackDepth` behind it (3 blocks in total by default). First person puts your head **inside that
slab, at the character's head**, looking along the tunnels. In first person the world is a corridor about 3 blocks
wide. You can walk only along x (left/right in the game), and up and down by jumping, climbing and falling. The
corridor's two long sides (the old camera side and the back) need a treatment; you suggested a dark void.

Every game action is still "a 2D world point plus a button". The game reads
`Camera.main.ScreenToWorldPoint(Input.mousePosition)` everywhere: aiming (`PlayerCamera:1127` sets
`body.targetLookPos`), attacking (`Body.Attack` swings toward `targetLookPos`), mining, hovering, picking up,
throwing, and gun angle. Your hands therefore only have to choose **that point** and **that button** in a more
physical way. The game's own rules stay authoritative: we call its own methods and do not write our own physics for
it (HAND-018).

## 2. What the corridor looks like

| Element | Treatment |
|---|---|
| Floor, ceiling, walls ahead/behind | The existing voxel blocks. They are already solid, with lit sides and tops. |
| **The two long sides** (±z) | Three options, all built behind one setting, `FirstPerson.Sides`:<br>**Cave walls** (**default**, user decision): the game's own background art (`Back` sorting layer, `world.backgrounds`) on the back side, which is what you see behind tunnels in the flat game, plus a darkened mirrored copy on the front side.<br>**Void** (option): black, with distance darkness so it reads as darkness rather than a hole.<br>**Greenscreen**: the existing key colour, for passthrough. |
| Open air and the surface | The game's sky/background as a distant backdrop. |
| Darkness and line of sight | The game's `VisionMask` (a 2D shadow mesh cast from the player) and the fog are hidden in the eyes. The walls now occlude for real, and distance darkness replaces the mask. |

**The flat-sprite problem.** Everything that is not terrain (items, creatures, other characters, plants,
decorations) is a flat sprite in the xy plane. Looking down the corridor, you see it exactly edge-on, so it
disappears.

- **Billboards (yaw only).** In the eye cameras, each sprite group turns about its vertical axis to face you.
  The group is its owner: a creature's limbs turn together around the creature's root. Owner grouping already
  exists in `PopupDepth`.
- **Drawn as eye-only copies.** The billboards are drawn with the same `forceRenderingOff` swap the mod already
  uses, because transforms with a `Collider2D` must never move for rendering (90 → 10 fps).
- **Buildings.** Crates, doors, capsules and other buildings (`ObjectExtruder`) extrude to about 1 block deep, so
  they read as boxes. Items and creatures stay billboards.

## 3. Camera and comfort

The character is a physics ragdoll that jumps, wall-slides, gets knocked down and falls unconscious. **User
decision: stay in first person as much as possible**: "this is an intense game so it's ok if the camera is intense".
Comfort is opt-in from the VR menu. Rules:

- **Position follows the head limb, smoothed; rotation is never forced.** Your head yaw and pitch are always your
  own (CAM-001). Only position is synthetic, and it is smoothed with a small dead-band so walk bob and hit shake
  do not reach your eyes.
- **Ragdoll, knockdowns and unconsciousness stay in first person**: the view rides the head limb's position
  (rotation still yours). Unconsciousness and sleep fade to black, as the game does flat. Only death and true
  cutscenes leave first person. VR-menu comfort options: position smoothing strength, an **optional** automatic
  cut to the diorama on ragdoll (off by default), the vignette, and the turn style.
- **Comfort options** (all in the VR menu, all off or mild by default). A vignette on fast vertical motion (falls, jumps, bounce shrooms). Smooth or snap turning
  by 180° (the world is a line, so "turning" just means facing the other way). A roomscale clamp: you may lean
  about ±0.5 m, and beyond that the view fades (CAM-005).
- **Scale (CAM-018).** One block is 1 world unit. The scale is chosen so that the character's eye height equals
  your real eye height. It is measured from the character's actual height in the spike (estimated at about
  0.8–0.9 m per block). A `FirstPerson.WorldScale` setting overrides it.

## 4. The character's hands

- **Your own arms.** These are the character's own arm sprites (ArmF and ArmB, three segments each, `Body.limbs[3..8]`)
  with whatever they wear, so wounds, bandages, splints, fur colour and clothing all show.
  - They are rebuilt as an eye-only puppet. A two-bone IK runs from the shoulder to your controller, with the
    elbow pole built by cross product (HAND-005/006). The segments get thickness with the `ObjectExtruder` pixel
    extrusion.
  - The game's ragdoll arms are not moved, for collider and performance reasons; only the puppet copies follow
    your controllers.
- **Hand poses.** The hand end uses the game's hand art with open, grip and point poses (the `handSprites` set
  already used for the controller hands and the cursor), driven by grip and trigger.
- **The main hand.** The game's active slot (`handSlot`, switched with switch-hands) decides which controller is
  the character's main hand. Whatever the character holds (`GetItem(handSlot)`) is drawn in that hand, anchored at
  a grip point taken from its sprite (HAND-016).
- **Your own body.** In the eyes your own head is hidden. Arms only at first. **Before the first-person
  release**, the torso and legs are shown too, with their damage and effects (wounds, blood, bandages, splints,
  broken or dislocated limbs, amputations, clothing) (user decision).

## 5. Interactions (HAND-014: near, far and fast are different intents)

| Verb | VR action | Game call |
|---|---|---|
| Aim / point | Right-hand ray. It marches along its xy projection and returns the first hit in the game's own masks (Item, Body/Limb, Descriptor, Ground, then blocks). That point becomes the mouse via the existing `Input` hook. The character turns to face it, as in the flat game. | `targetLookPos`, hover, `hoverSquare` |
| Attack / mine | Trigger, as now, **and** a **swing** of the hand above a speed threshold, aimed along the swing (INPUT-010: measured in room space). Both stay (user decision). | `Body.Attack` |
| Pick up | Reach to an item and press grip. It goes into **that hand's** slot. | `DoPickupCheck` + `PickUpItem(item, slot)` |
| Drop / throw | Open the grip slowly to drop. Open it while moving to throw, with force from hand speed and direction from its velocity. | `DropItem` / `ThrowItem(force)` |
| Use / eat / drink | Trigger while holding. Later: bring the item to your mouth. | `UseItemInHand` |
| Interact with buildings (open, use, loot) | Touch the building and press grip (today's right-click). | the existing right-click path |
| Store and inventory | Bring a held item to your chest to open the game's radial menu (the game opens it when you drag over your body). The inventory, crafting and wound view stay on the VR panel, which can be summoned in front of you or onto the left wrist. | radial / panel |
| Guns | The hand ray sets the gun angle; trigger fires. | `gunangle` from `targetLookPos` |
| Climbing (ropes, ladders) | First version: stick up/down while on a climbable. Later: grip and pull. | `Climbable` |
| Minigames | Unchanged at first: they appear on the panel with the existing hands. Later: done with your real hands. | — |

The laser stays for menus and anything beyond reach. Reach range is capped at the game's
`Body.interactionRange` (10 blocks), so nothing becomes reachable that the game would refuse.

**Locomotion:**
- **Left stick:** forward/back walks along the direction your head faces in x.
- **Right stick:** up = climb, down = crouch; a flick sideways turns 180°.
- **Buttons:** A jumps. Real crouching also crouches, as an option.

The current bindings remain available in the Controls tab.

## 6. Technical risks to settle first

1. **2D lights in perspective.** URP's 2D lights render a screen-space light texture per camera. Phase 4 showed
   that block faces light correctly in the diorama, but a view looking down a corridor stresses this far more.
   If it breaks, the fallback is to sample the lights in world space from the main camera's light texture.
2. **Chunk culling.** The game shows only the chunks inside the ortho camera's view, but in first person you see
   further along x. Visible chunks need to be kept within a radius around the player (CAM-002: cull frustum
   separate from render frustum).
3. **Comfort itself.** The game was never designed for it, so the spike is a go/no-go gate.
4. **Billboard cost.** These are per-group eye-only draws in heavy scenes, on top of the 75–85 fps already
   measured there.
5. **Facing.** The game flips the whole character (`isRight`) when you aim behind it. In first person this
   decides which arm is in front; the puppet arms must survive the flip without swapping hands.

## 7. Phases

| Phase | Goal | Done when |
|---|---|---|
| **FP-0 Spike** (1 session) | Head at the character's head, Cave-wall sides (Void as the fallback), smoothed position, F7 toggles between diorama and first person, chunks kept within a radius. Measure scale; check lighting and comfort. | **Your headset verdict**: comfortable, readable, lit. Otherwise stop and rethink. |
| **FP-1 Readable world** | Billboards, buildings extruded, Void / Greenscreen side options, distance darkness, sky, ragdoll and unconsciousness handling, comfort options. | Creatures, items and crates are recognisable down a tunnel. |
| **FP-2 Hands** | The puppet arms with clothing and wounds, hand poses, the held item in the hand, own head hidden. | The arms follow the controllers and show the character's own state. |
| **FP-3 Core interactions** | Aim ray → mouse, trigger attack/mine, grip pick-up/drop/throw, building interact, chest → radial, panel summon, guns. | A full run is playable in first person without the keyboard. |
| **FP-4 Physical** | Swing attacks, mouth to eat/drink, grip-climbing, haptics (HAND-003), minigames with your real hands. | Each gesture triggers deliberately and never fires for another (HAND-014 proof). |
| **FP-5 Body** | Torso and legs as eye-only puppets, with wounds, blood, bandages, splints, broken/dislocated/amputated limbs and clothing. | Looking down shows the character's real state. **Required before release.** |
| **FP-6 Settings and release** | A "First person" tab in the VR settings, xr-tape checks driven by xrsim `head`/`hand` poses, docs, 1.1 release. | xr-tape passes; headset acceptance. |

Testing follows the existing loop: the dev copy only, `launch-dev.ps1`, xr-sim captures from scripted head and
hand poses, and **several seeds and layer types** (lesson from the fog miss), not just seed 1234.

## 8. Decisions (user, 2026-10-05)

1. **Sides:** cave walls by default, Void as an option.
2. **Intensity:** stay in first person as much as possible, through ragdoll and knockdowns; comfort options in the
   VR menu.
3. **Attack:** swing to attack as well as the trigger.
4. **Body:** arms only at first, but the torso and legs with damage and effects are required before this version
   is released (FP-5).
