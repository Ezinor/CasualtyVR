# PHASE4_VOXEL_RECEIPT — terrain as 3D blocks (round 1)

Date 2026-10-04. Environment: dev copy under xr-sim (Quest 3 / VDXR profile) + xr-tape. Grade: LIVE
(substitute runtime), headset pending.

## Design (`src/CasualtyVR.Core/VoxelTerrain.cs`)

* One mesh per game chunk (64x64), built from the chunk Tilemap itself: `GetSprite`, `GetTransformMatrix`
  (the game randomly rotates/flips tiles in `ChunkScript.RandomizeTiles`), `GetColor`. Front faces at
  z = -FrontDepth match the flat art; side faces only next to empty cells, streaked from the edge texel row,
  shaded 0.95 top / 0.75 sides / 0.55 bottom. Rebuilt on `WorldGeneration.ChunkUpdated[x,y]` (plus the
  four neighbours), and once more 1.5 s after the game's tile randomisation finishes. Nearest chunk first,
  within `Voxels.BuildBudgetMs` per frame.
* Eye-only: layer 25, removed from the game camera's mask and added to the eye masks. During an eye's
  render the built chunks' `TilemapRenderer`s get `forceRenderingOff` (not `enabled`, which the game uses
  for chunk logic). Colliders and physics are untouched.
* Depth: sprites never write depth but do test LEqual. Each chunk draws twice at the very start of the
  Default sorting layer: a depth-only prepass (URP Particles/Unlit, Blend Zero One, ZWrite On, alpha clip),
  then the game's Sprite-Lit material (2D lights apply). Later sprites are hidden behind block faces and
  visible inside tunnels. PopupDepth skips layer 25 and moves the bands drawn over the terrain in 2D
  (block cracks at 4000, corpses, jump pads) just in front of the faces.
* Config `[Voxels]`: Enabled, ToggleKey F8, FrontDepth 1.0, BackDepth 2.0 (blocks), BuildBudgetMs 2.

## Results (xr-sim, seed 1234)

| Check | Result |
|---|---|
| Build cost | 15 chunks visible, ~3 ms per chunk build, done within the first second of gameplay |
| Frame rate | 90 fps steady after the build (same as before Phase 4) |
| Off-axis capture | `Working/xrsim-shots/voxel3_zoom.png`: lifepod walls and floor extruded, side faces visible, player inside, background visible through the opening, 2D lighting on the blocks |
| Submission contract | xr-tape 19/19 (1 skip: no depth layers submitted) |

## Not yet covered

Headset feel (depth amounts), digging/building rebuild latency, glass/leaves transparency, liquids and
sprite slabs for characters/items (later Phase 4 rounds), heavy/rainy scene perf.

## Headset feedback (user, 2026-10-04) — HEADSET pass, round 1

Blocks feel solid; dug blocks update well; depth "might be a bit much" (tune later via `Voxels.FrontDepth`
/ `BackDepth`). Flagged: the top of extruded ground looked like one pixel stretched.

## Round 2

* **Side faces show the whole tile**, one segment per block of depth, folded at segment boundaries (front
  edge row -> opposite row -> front edge row), so a side continues the front face seamlessly instead of
  streaking the edge texel row. UVs are inset 0.2% of the sprite rect so point sampling never hits a
  neighbouring atlas sprite. Capture: `Working/xrsim-shots/voxel4_zoom.png` (floor tops show the tile
  pattern in depth). Build cost ~3 -> ~4.6 ms per chunk (three times the side quads).
* **Character/item thickness** (`SpriteSlabs.cs`): every visible Default-layer SpriteRenderer with its own
  non-trigger Collider2D (limbs, items, creatures; PopupDepth never moves these) gets `ThicknessLayers` (3)
  copies stacked behind it over `ThicknessDepth` (0.3 blocks), at 70% brightness, same material (the
  per-limb material keeps blood/wetness/damage) and same sorting order (equal orders sort back to front
  for perspective cameras). Copies only behind: sprites do not write depth, so a front copy would paint over
  clothing and overlays. VR-only layer 25, outside the game hierarchy, pooled; rescan every 30 frames.
  Seed 1234 start: 27 sources. Capture `Working/xrsim-shots/slab1_zoom.png`.
* Config `[Voxels]`: CharacterThickness (true), ThicknessLayers 3, ThicknessDepth 0.3. Debug flags
  `--cvr-dbg-novoxels`, `--cvr-dbg-noslabs`.

| Check | Result |
|---|---|
| Perf (`vr-perf-matrix.ps1`, seed 1234) | all on 90.1 / 84.2 fps (two runs), no slabs 87.8, neither 90.1: within run-to-run noise from scene extras (rain); no clear cost from either |
| Submission contract | xr-tape 19/19 (1 skip) |

## Headset feedback (user, 2026-10-04) — round 2 HEADSET pass

Tops and sides "look good now"; characters look good; frame rate good.

## Round 3: see-through blocks

Glass (7), scaffolding (21) and leaves (25) (`Voxels.SeeThroughBlocks`) no longer extrude as solid blocks:
a front pane at FrontDepth and a back pane at BackDepth (80% brightness), sides only towards air, in a
separate mesh drawn at the chunk's own sorting order (1000, after background and actors) without the depth
prepass, so what is behind them shows. Solid neighbours treat them as open and show their sides. Capture
`Working/xrsim-shots/glass1_zoom.png`: the lifepod's upper windows show both panes and the wall sides around
them (they were solid in `voxel3_zoom.png`). 90.6 fps; xr-tape 19/19 (1 skip).

## Round 4: liquids with depth (`LiquidVolume.cs`)

The game stores liquid per block (`FluidManager.fluid`) and every 0.1 s redraws it as one particle quad per
wet cell at z=0 (`RenderFluids`, WaterShader particles on Top/-500). A Harmony postfix on `RenderFluids`
(while VR, voxels and `Voxels.LiquidDepth` are on):
* moves the particles to just behind the block front plane (FrontZ + 0.05), so pools fill their tunnel from
  the front and anything in the water is seen through it. Read back with `GetParticles(buf, maxParticles)`:
  `particleCount` still reports the previous simulation step right after `SetParticles` (first build saw 0).
  The monitor's orthographic camera ignores z. Switching off redraws at z=0;
* rebuilds a translucent surface across the tunnel depth on every open-topped wet cell (game's own
  half-height rule at pool edges), eye-only layer 25, Top/-499, Sprite-Lit so 2D lights apply.
  `liquidColors` is an identity colour (water = white; its shader does the blue), so water uses a fixed tint
  and other liquids are dimmed to 60%; opacity `Voxels.LiquidSurfaceOpacity` 0.35. The first build drew the
  surface white and glowing.

Test scene: probe `--cvr-pool` pours water into the two rows above the floor around the player (seed 1234:
34 cells). Capture `Working/xrsim-shots/liquid3_zoom.png`: blue surface spanning the depth, the
character visible through the front face of the pool. 90 fps; xr-tape 19/19 (1 skip). Debug flag
`--cvr-dbg-noliquids`.
