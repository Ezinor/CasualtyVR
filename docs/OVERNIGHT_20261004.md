# Overnight round — 2026-10-04 (unattended, xr-sim only; headset checks pending)

Backup of the build before this round (the headset-tested liquids build): `Working/backups/build-20261004-liquids/`
(deployed plugin + patcher, configs, src, tools, docs). To roll back, copy `plugins-CasualtyVR` over
`Working/game-dev/BepInEx/plugins/CasualtyVR`.

| Request | Done | Where | Verified (xr-sim) |
|---|---|---|---|
| Flickery, offset hover glow | The game's `hoverSquare` (and block-placing `placeSquare`) sat in the Top layer, which the pop-up depths put 2 units in front of everything, so the glow floated off its object. Now pinned at eye-render time onto what it marks: a block's front face, or the actor plane for items/buildings. New option **Hover highlight** (on/off) | `PopupDepth.PinMarkers` | built and running; hover itself not driven headless |
| Crates etc. flat | Thickness now uses the game's own hover rule (`PlayerCamera.HandleWorldHover`: `Item`, `BuildingEntity` = what you can right-click), including trigger colliders and the Ground layer, which holds crates, doors, lamps. Seed 1234 start: 25 -> 31 objects (container crate, shuttle doors, lamp, pump, shower, heater, buttons, trees, bushes, corpse) | `SpriteSlabs.Rescan` | log `Slabs: building ... slabbed True` |
| Controls | Pause stays on the left menu button; R stick click freed (now *favourite*). **Throw on L stick click** (hold to charge, release to throw, tap to drop; R stick up still throws too). **Ragdoll = both grips held** (single-grip actions pause while both are held). The whole mapping is now config (`[Controls]`) and editable in the menu | `VrInput`, `Plugin` | compiles; xr-tape 19/19 |
| In-game settings menu | **VR settings**: cyan "VR" button added under Settings in the pause menu, or **F12**. Tabs: *View* (zoom, diorama distance, window fade/size, HUD size, hover highlight, dashboard distance/height/width, cursor size, controller hands on/size, recenter), *Blocks* (3D blocks, front/back depth, pop-up depth, character thickness/depth/layers, liquid depth/opacity), *Controls* (every controller input -> any game action, reset), *Passthrough*. Every row is `< value >`; changes save to the BepInEx config immediately and apply live (blocks rebuild when depths change) | `VrSettingsMenu`, `HudScaler` | `Working/xrsim-shots/menu1_left.png`; UI self-test clicked its tab button |
| Passthrough greenscreen | **Passthrough** tab: on/off, key colour presets (green, blue, magenta, black, white) and R/G/B steps. Outside the window: the frame drawn after post-processing in the key colour (exact). Inside: the backmost layer (`Back` sorting layer, the cave-rock backdrop) hidden and the eye cameras cleared to the key colour. Solid blocks are filled opaque in greenscreen mode (their tile art has see-through texels that showed the key colour) | `Greenscreen`, `DioramaFrame`, `VoxelTerrain.ConfigureDepth` | `hands1_left.png` / `hands2_left.png` |
| Hands | The game already draws its own hand in minigames (`MinigameBase.handSprites`, following the cursor, with pain shake), and the laser drives it. Added: the same hand art at both controllers in the headset (billboard, hand end at the aim point, along the pointing direction; trigger = pressed point, grip = closed grasp; left mirrored). Menu: Controller hands on/off, size | `VrHands` | `Working/xrsim-shots/hands3_left.png` |

Results: 89–94 fps steady (seed 1234), xr-tape 19/19 (1 skip) on the final build.

## Known limits / to check in the headset

* Greenscreen inside the window passes through the game's colour grading (ColorAdjustments, LiftGammaGain),
  so it can be a slightly different green from the outside; widen the key tolerance in Virtual Desktop or
  pick the colour there. Background walls (not the backmost layer) stay visible, as requested.
* The game's hand art is a dark forearm with a white outline: on a dark scene mostly the outline shows.
* Ragdoll on both grips: whichever grip lands first sends its own action for that instant.
* HUD size scales the game's main canvas (HUD, inventory, menus) only while VR runs.
* Debug flags added: `--cvr-dbg-settings <tab>` (open the menu headless), `--cvr-dbg-greenscreen`
  (greenscreen on without saving it), `--cvr-dbg-noliquids`.

## Headset feedback (user, 2026-10-04 morning) and fixes

Menu "looks and works well"; hovering, throw and ragdoll work well. Fixed:
* **Pause-menu VR button** was cut off: now just the cyan "VR" text (the cloned round icon is invisible but
  still the click target).
* **R stick click -> none** (was favourite). Existing configs migrated once (`Controls.MappingVersion` 2).
  R stick *up* still throws (unchanged; say if that should go too).
* **Greenscreen now replaces the background walls** too (`WorldGeneration.backgrounds`: rock/soil/steel...,
  order -9999), not only the `Back` layer. While it is on, a second eye-only volume neutralises colour
  grading, bloom and tonemapping, so the key colour inside the window is exactly the chosen one: sampled
  (0,255,0) inside and outside (`Working/xrsim-shots/green2_left.png`). The game's grading is therefore off in
  the headset while the greenscreen is on.
* **Hands:** the game's sprites (dumped with `--cvr-dbg-dumphands` to `BepInEx/CasualtyVR/hands/`) are
  upright forearms with the hand at the top; the first version laid them sideways. Now the hand points along
  the controller. Three poses: open hand at rest (handGraspIdle1), pressing finger on the trigger
  (handPointClick), fist on the grip (handGraspClick). `Working/xrsim-shots/hands4_left.png`.

xr-tape 19/19 (1 skip), 85–90 fps.

## Hand cursor (user request, 2026-10-04)

`Dashboard.CursorStyle` = **Hand** (default) / Cursor / Off, on the menu's View tab. Hand = the same game hand
art cut at the wrist (top 26% of the sprite, hot spot over the fingers): open hand at rest, pressing finger
while the laser hand's trigger (or a mouse button) is held, fist on its grip; right-hand art when the
character's active hand is slot 0, mirrored for slot 1 (as MinigameBase does). Own size,
`HandCursorSizeMetres` 0.05 (the arrow's `CursorSizeMetres` was 0.005 in the user's config and would make it
tiny). The View tab grew to 15 rows and wrapped into two cramped columns; tabs now stay one column up to 16
rows. Capture `Working/xrsim-shots/cursor2_zoom.png`. xr-tape 19/19.

## "Unplayable: can't see the character or most objects" (user, 2026-10-04)

Cause: the user's run was a **Foggy** layer (layer modifier 0 -> `WorldGeneration.SetFog(1)`), not the seed-1234
world used in testing. The game's fog is one big sprite (`WorldGeneration.fogSprite`, Shader Graphs/FogShader,
Top/0). On the monitor it is a light haze; in the eye cameras it rendered as dark opaque blobs over everything
(`Working/xrsim-shots/bug2_left.png` random foggy world, `fog1_left.png` seed 1234 with `--cvr-dbg-fog`).
Apparently it builds its see-through look from a scene texture the eye cameras do not get (inference; hiding it
restores the view, `fog2_left.png`). Not caused by the hand cursor change; it would have happened in any build
on a foggy layer.

Fix: for the eyes only, the fog sprite is drawn with the game's lit sprite material at `Diorama.FogHazeOpacity`
(0.3) x fog amount: a haze that glows near lights and fades in the dark (`fog3_left.png`). Menu (View): **Fog**
Haze/Off, **Fog haze opacity**. Debug: `--cvr-dbg-fog` forces the Foggy modifier, `--cvr-dbg-nofog` hides it.
Lesson: test with random worlds and the layer modifiers (Foggy, Rainy, Flooded, ...), not only seed 1234.
xr-tape 19/19 with fog forced on.

## Offset glow on the lifepods ("capsules", user 2026-10-04)

The lifepod's glowing screen panel (`lifepodscreenbackdrop`, Sprite-Unlit, order -8700, and its shower/heat icons
at -8500) was pushed 4 units back with the background band, while the buttons on it (Disinfect/Heat, -8500 but
with colliders, so never moved) stayed at the actor plane: the illuminated panel sat behind and offset from its
buttons. PopupDepth now keeps small (< 64 units^2, non-tilemap) back-band sprites that overlap a back-band
collider object at that object's depth (the actor plane). Logged as `Popup: '...' kept with its object`.
Capture `Working/xrsim-shots/pod1_zoom.png`. xr-tape 19/19.

## Cursor polish (user, 2026-10-04)

* No cursor while a minigame is open (every minigame type draws the game's own hand at the mouse).
* Hand cursor click point = the index fingertip of each pose, measured on the dumped art (open 0.15/0.07,
  pressing 0.07/0.06, fist knuckle 0.21/0.03 of the cut hand's width/height); it was one point between the index
  and middle fingers. Bigger: `Dashboard.HandCursorHeightMetres` 0.07 (new key; the old 0.05 key is ignored).
* Controller hands off by default: `Controllers.ShowControllerHands` false (new key; the old `ShowHands = true` is ignored).
* Menu: single column up to 18 rows, font scaled to the row height (`menu2_zoom.png`). xr-tape 19/19.

## Fog, round 2: Mist (user, 2026-10-04)

The lit haze "flickers and moves when I move my head": a lit sprite floating in front of the scene samples the
per-eye, screen-space 2D light textures at a depth where they do not line up with what they lit. New default
`Diorama.FogStyle` = **Mist** (new key; Haze and Off remain): the fog sprite is hidden for the eyes and one
unlit, world-anchored quad (VR layer, Top/0, just in front of the block faces) follows the player: a dark
desaturated veil (0.16, 0.18, 0.21), clear within ~4 blocks and full by ~22 (as the game's FogShader thins around
`_PlayerPos`), opacity `FogMistDensity` 0.6 x fog amount. Menu: Fog Mist/Haze/Off, Fog strength (per style).
Captures `mist1_left.png` / `mist2_left.png` (head moved: veil stays put). xr-tape 19/19 with fog forced on.

## Extruded world objects (user, 2026-10-04: "drop capsules ... and similar objects extrude")

`ObjectExtruder.cs`: every visible BuildingEntity sprite with its own Collider2D (drop capsules, crates, doors,
lamps, pumps, heaters, traps...) gets a mesh extruded from the sprite's own pixels (read back through the GPU):
the sprite on a front face (z -0.5 x depth) and a darker back face, and a wall along every solid/see-through pixel
edge, shaded like the voxel blocks. Cached per sprite (openables swap sprites), follows the transform, flips and
tint; the original is hidden only while an eye renders. Replaces the stacked-copy thickness for those objects.
Config `Voxels.ObjectExtrusion` (on), `Voxels.ObjectDepth` 1.0 blocks; menu Blocks tab.

Attached sprites now stay with their physics object (`PopupDepth`: a renderer under a non-terrain Collider2D
owner keeps the owner's depth; on an extruded owner it sits on the front face). The drop capsule's emission panel
("Square", unlit, order 801) had floated 1.6 units in front of the capsule. This also keeps creature parts,
traders, jump pads and traps together with their bodies. Terrain chunks (tilemap/composite colliders) are not
owners: the game parents background walls to them.

Test: `--cvr-dbg-spawn dropcapsule` (any Resources path) places one 6 blocks right of the player.
Capture `capsule1_zoom.png`. Seed 1234: 22�25 objects extruded, 90 fps; xr-tape 19/19.
