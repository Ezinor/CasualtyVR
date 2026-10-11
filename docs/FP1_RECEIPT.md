# FP1_RECEIPT — first person: readable world and comfort (2026-10-05)

Plan: `docs/FIRSTPERSON_PLAN.md` (FP-1). Builds on `docs/FP0_RECEIPT.md`. Source backup after FP-1:
`Working/backups/fp1-20261005/`. Status: **built and verified headless; waiting on the headset**.

## What changed

- **Characters face you (`Billboards.cs`, setting *Characters face you*, on).**
  - Items, creatures, other characters and corpses are turned, each as a whole, about their root's vertical axis
    to face your head. A group is the topmost physics object (Rigidbody2D) or BuildingEntity above a sprite, so a
    creature's limbs, clothes and held items turn together and you see it in profile, as the game draws it.
  - The game's objects are never moved. Copies on the VR-only layer use the same sprite, material (blood and
    damage), colour and sorting, and the originals are hidden in the eyes.
  - Not grouped: the player, terrain, extruded buildings (already 3D) and back-wall objects. Character thickness
    (slabs) is off in first person, since the billboards replace it.
  - Capture `fp1pod2_fwd_left.png`: a water bottle stands upright in the capsule, facing you.
  - Capture `fp1fog_fwd_left.png`: a lantern billboard lights the room.
- **Block markers on the faces you see:**
  - The game's block hover square, the building ghost and block cracks are placed on the face of their block that
    you see. If the aim ray entered that block, it is the face the ray came through, at the ray's depth.
  - Otherwise it is the side facing you, in the middle of the tunnel.
  - An item's hover outline turns to face you.
  - Capture `fp1pod2_fwd_left.png`: the hover square lies on the floor tile being aimed at.
- **Fog and darkness by distance:**
  - On Foggy layers (mist colour) and with Void sides (black, *Void darkness* 0.6), eight see-through sheets stand
    across the view at 2–30 blocks, facing the head.
  - They are depth-tested against the blocks, so the haze grows with distance and nearer walls cut it off.
  - They draw before characters and items, so those are not fogged.
  - Limit: a faint step is visible where a sheet meets the floor. Smooth fog needs a depth-reading shader, which
    means a custom shader bundle built with the Unity 2022.3.62f3 editor (installed); later.
  - Captures: `fp1fogcave_*`, `fp1void_*`.
- **Comfort (VR settings, *First person*):**
  - *Blackout when unconscious* (on): the game's own blackout is a black screen image, which the headset's
    see-through HUD cannot show. In first person the view darkens below 40 consciousness, is black at 20, and is
    black while asleep.
  - *Fall vignette* (0 = off): darkens the edges during fast vertical motion.
  - *When knocked down: Stay / Diorama* (Stay): Diorama switches to the diorama view while the body is down.
  - *Turning: Snap / Smooth*, with *Smooth turn speed* (120°/s). Snap is the right-stick flick, turning you around.
    Walking forward follows whichever way along the tunnel the view faces.
  - Overlay check: `--cvr-dbg-overlay` forces half blackout plus the vignette (`fp1overlay_fwd_left.png`).
- **Proxy lights for lamps:** done in FP-0 round 2 (copy lights for lights on physics objects).

## Verification (xr-sim, seed 1234)

| Check | Result |
|---|---|
| Build | 0 warnings, 0 errors |
| xr-tape, diorama | 19/19 (1 skip) |
| xr-tape, first person | 19/19 (1 skip) |
| Exceptions in the log | none |
| Lifepod with spawned items (`--cvr-dbg-spawn crystalenemy,waterbottle`) | billboards drawn (9–10 groups) |
| Foggy layer, cave (`--cvr-dbg-fog --cvr-dbg-cave`) | depth haze, nearer walls cut it off |
| Void sides | black sides, darkening with distance |

**New debug flags:**
- `--cvr-dbg-spawn a,b,c` spawns several objects, 2.5 + 1.5·i blocks ahead, after the cave teleport.
- `--cvr-dbg-overlay` forces the blackout and vignette on.

## Open

- Smooth (shader) fog.
- Billboards are not fogged.
- Seeing a creature only in profile when it is right next to you.
- Hands and arms (FP-2) are next.
