# CasualtyVR — VR mod plan (PORT_BRIEF + AUTHORITY_MAP + phases)

Target facts are in [TARGET_PROFILE.md](TARGET_PROFILE.md). Playbook: `VR Modding Playbook/` (https://github.com/phunkaeg/vr-modding-playbook)
(route through `AGENTS.md`; cite pattern and FAIL IDs).

## 1. The core design problem

*Casualties: Unknown* is a **2D side-view game**: an orthographic camera, Tilemap terrain, a Physics2D
ragdoll, and mouse-driven interaction. There is no 3D world to put a head inside. A "first-person VR"
port is not really available. The game logic only exists on one plane.

So the realistic conversions are, in increasing cost:

| Tier | Experience | What it takes |
|---|---|---|
| **T0 Theatre** | The flat game on a big curved screen | A render texture on a quad. This is the fallback mode that is always kept |
| **T1 Pop-up book** | The existing sprites and tiles are pushed to different depths by layer and seen in true stereo from a seated vantage, like a diorama on a table | A perspective stereo camera plus a z-offset per sorting layer |
| **T2 Laser-pointer play** | Controllers replace the mouse and keyboard. A laser hits the game plane and the game sees a virtual mouse there | `Input.mousePosition` redirection, key mapping, UI panels |
| **T3 Voxel diorama** | The terrain is extruded into real 3D blocks, characters and items become extruded sprite slabs, and you can lean in and look around the scene | Mesh generation from `worldBlocks`, slab meshes, depth occlusion (the DramaticShape method) |
| **T3+ Hands** | Grab items in the diorama by hand and do minigames (bandaging, surgery) with tracked motion | Interaction built on top of T2/T3 |
| *Experimental* | A first-person "helmet cam" inside the voxel world | Only after T3. Comfort is a risk, and the gameplay plane makes it of limited use |

**Recommended target for the first release: T2 on the pop-up book, with T3 voxels as the flagship.**
Voxelisation is not an optional extra here. It is what turns a 2D game into a space worth being in.

## 2. Authority map (PORT-01)

| Layer | Readable | Buildable | Modifiable | Shippable | Route |
|---|---|---|---|---|---|
| Game logic `Assembly-CSharp` (Mono) | Yes (ILSpy, full names) | No | Yes (Harmony at runtime) | As a plugin DLL | **Managed plugin** (playbook Mode 2) |
| Unity engine `UnityPlayer.dll` 2022.3.62f3 | Binary only | No | Hooks only | No | Avoid. Use only public Unity APIs |
| URP 2D Renderer | Unity package source is public | No (it is baked into the build) | Through `ScriptableRendererFeature` / camera stacking / extra cameras | — | Work around it, do not fork it |
| Shaders and materials for new geometry | — | **Yes, in the Unity 2022.3.62f3 editor → AssetBundle** | — | Shipped as a bundle | Needs a matching editor |
| VR runtime | OpenXR spec | **Yes (own C++ plugin)** or the Unity OpenXR plugin injected | — | DLL | Decided in Phase 1 |

Integration route: **hybrid managed plugin.** It is BepInEx 5 + Harmony for game logic and cameras, plus
either Unity's XR stack injected (the UUVR technique) or a small native OpenXR bridge DLL. Ghidra and
x64dbg are for the native bridge and for crashes only. Game-side RE is done with ILSpy, Frida and
Cheat Engine. RenderDoc is used for pass census and stereo proof.

**Key architectural rule:** *the game's orthographic `Camera.main` remains the logic camera.* Every
`ScreenToWorldPoint(Input.mousePosition)` keeps working. VR renders through **separate** cameras, and
VR input is converted *into* virtual mouse coordinates on the logic camera. This decouples VR from
~20 input call sites and from the camera clamp logic (`PlayerCamera` world-bounds clamp).

## 3. Phases

Each phase has an **exit proof**. Do not start feature work on an unproven earlier phase (playbook
`start-new-port.md`: "a T3 feature on an unproven T1 camera manufactures hand bugs").

### Phase 0 — Workspace and baseline (PORT-00/02) · ~1 session · **DONE 2026-10-03**

Results: [BASELINE_RECEIPT.md](BASELINE_RECEIPT.md), [RENDER_PASS_CENSUS.md](RENDER_PASS_CENSUS.md).

1. Make a **dev copy** of the game, `Working\game-dev\`, so the modded co-op install stays untouched.
   Install a clean BepInEx 5.4.23.5 in it and **no other mods**. Make a matching vanilla control copy,
   or toggle Doorstop with `enabled=false`.
2. Write a `CasualtyVR.Probe` BepInEx plugin (read-only). It logs `SystemInfo.graphicsDeviceType`, the
   URP asset and renderer type, every `Camera` (ortho, size, culling mask, target), every `Canvas`
   (render mode), the sorting layers in use, and `WorldGeneration.world` dimensions and `CHUNKSIZE`.
3. Set up a deterministic test scene. Use the in-game console and run settings (fixed seed, tutorial
   start). The KrokMP args show the game can be scripted at startup, so replicate that in the probe.
4. Take a RenderDoc capture of one gameplay frame and write `RENDER_PASS_CENSUS.md`: 2D light passes,
   vision mask, post volumes, UI overlay.

**Exit:** the probe log plus the capture answer: graphics API, renderer, the camera list, the canvas
list, and the light passes. Graded LIVE.

### Phase 1 — Stereo route discriminator (PORT-07/08) · 1–2 sessions · **headless DONE 2026-10-03, headset pending**

Route **1b chosen** (own native bridge). Results: [PHASE1_SPIKE_RECEIPT](PHASE1_SPIKE_RECEIPT.md),
[PHASE1_XR_RECEIPT](PHASE1_XR_RECEIPT.md). xr-tape 19/19 at 90 Hz under xr-sim. The UUVR result (1a)
is still to be recorded from the user.

Two cheap spikes answer one question: *can Unity's own XR path render this URP 2D Renderer game in stereo?*

* **1a, UUVR spike (zero code):** install UUVR through Rai Pal into the dev copy. Record whether it
  produces stereo, whether 2D lights survive, and what breaks. A failure is a valid, recorded result.
* **1b, own bridge:** two perspective `Camera`s (L/R) render into `RenderTexture`s using the game's own
  renderer. A native OpenXR bridge (C++, D3D11 binding) copies them into OpenXR swapchains through
  `GL.IssuePluginEvent`, and pose and FOV come back per frame. Most of the playbook's chapter 09 OpenXR
  lifecycle and transport guidance applies, minus the hooking.

Decision rule: if 1a gives correct stereo with 2D lights intact, adopt the Unity XR stack (less code).
If not, use 1b, which does not depend on the 2D Renderer supporting XR and is fully testable headless.

Headset: **Quest 3** via SteamVR (OpenXR runtime = SteamVR) or Virtual Desktop (its VDXR OpenXR
runtime). Route 1b's D3D11 OpenXR binding works with both. Test on both runtimes before release,
because they differ in recommended resolution and FOV.

**Exit (either route):** under **xr-sim**, `xr-tape --Check` passes, including `eye_subimages_distinct`,
`submitted_fov_matches_located`, `submitted_pose_matches_located` and `eye_order`, and the xr-sim PNG
capture shows two different eye images of the game. Then one headset session (HEADSET).

### Phase 2 — T0 Theatre → T1 Pop-up book · 1–2 sessions

1. **Theatre:** the logic camera renders to an RT that is shown on a curved quad. This is always
   available as a fallback toggle and for menus.
2. **Pop-up book:** a parallax depth table that maps sorting layer/order to a z offset (background
   walls → back, tiles → 0, bodies/items → slightly forward, particles/foreground → front). The VR rig
   is world-anchored in front of the play plane and follows `PlayerCamera` position with smoothing and
   dead-zones (no head-locked motion). Rig scale (world units to metres) and the "table height" are
   config values.
3. Check every per-frame camera effect that assumes orthographic: `GlobalDark`, `VisionMask`, biome
   post volumes, the camera shake (`Shaker`), and zoom (`zoomTime`).

**Exit:** in a headset, comfortable stereo depth layering. The game remains fully playable with
mouse and keyboard while you are in VR.

### Phase 3 — T2 Controller input and UI · 2–3 sessions

1. **Virtual mouse.** The controller laser intersects the z=0 game plane, and the hit point goes through
   `logicCam.WorldToScreenPoint` to become the virtual mouse. Harmony-patch the `Input.mousePosition`
   getter, plus `GetMouseButton*` for trigger and grip. All existing aim, drag and drop, tooltip and
   block-hover code then works unchanged.
2. **Keys.** Patch `KeyBinds.GetBind` consumers and `Input.GetKey*`. The left stick drives movement, and
   the buttons drive jump, crouch, inventory, wound view and the radial menu. Each mapping needs proof
   of press *and* release (playbook PORT-10).
3. **UI.** Every screen-space canvas (`mainCanvas`, the minigame canvases, the menus) is re-targeted to
   a world-space panel or an RT quad, and the laser is raycast into it. Minigames get a "work table"
   panel close to the player.
4. Use `INPUT_OWNERSHIP.md` as the single table of who owns each action.

**Exit:** a full run (start → descend one layer → treat a wound in a minigame → craft) is completed
using only controllers.

### Phase 4 — T3 Voxel diorama · 3–5 sessions (can start in parallel, flat, after Phase 0)

This is the DramaticShape approach applied to this game. **Build and prove it in flat mode first**:
a perspective camera tilted toward the plane. That makes it a shippable "3D mode" mod on its own,
and it de-risks VR.

1. **Terrain.** For each chunk, generate a mesh from `WorldGeneration.worldBlocks`. Solid blocks
   become boxes of configurable depth (per block-type depth table), built with greedy meshing and
   only exposed faces emitted. The front face uses the tile sprite UV and the sides use the tile edge
   texels or `tileColors`. Rebuild on `ChunkUpdated[x,y]` (digging/explosions). Hide the matching
   `renderChunks` `TilemapRenderer`s, and mirror the existing chunk culling.
2. **Background.** Background tilemaps become a thin back slab (or several stepped slabs) for depth.
3. **Characters and items.** Generate **sprite-extrusion meshes** from alpha: per-texel columns, cached
   per `Sprite`. Attach them to each `Limb`/item transform so the 2D ragdoll still drives them. The
   cheaper fallback is "leaning slabs" (a quad with thickness). Wound and wearable overlays stay as
   sprites on the front face.
4. **Occlusion and lighting.** Use real depth testing instead of sorting order. Use 2D-Lit-compatible
   shaders so `Light2D` still lights the geometry (to be verified in Phase 0's census), with a fallback
   of URP Unlit plus baked tile colours. `VisionMask` becomes a darkness volume extruded in z.
5. **Liquids.** Add a second grid pass from `FluidManager`, rendered as translucent volume meshes.
6. **Look options**, as DramaticShape has: camera pitch presets, tilt-shift, and world curvature.

Needs: **Unity Editor 2022.3.62f3** (exact patch, because shader variants in AssetBundles must match).

**Exit:** a flat 3D mode runs a full level at target frame rate with digging and explosions updating
the meshes correctly. Then the same scene works in VR.

### Phase 5 — T3+ Hands and VR-native interaction · open-ended

* Grab items directly in the diorama. This maps to the existing drag and drop (`dragItem`) logic, now
  driven by controller position projected onto the plane.
* Physical minigames: bandage wrapping, syringe and suture motions mapped to the `MinigameBase` inputs.
* Point at a limb for wound view (`Physics2D.OverlapPoint` at the hand's projected point).
* Haptics on damage, heartbeat (ECG), and the Geiger counter.
* Experimental: a first-person helmet cam in the voxel world (comfort-gated, opt-in). Detailed plan: `docs/FIRSTPERSON_PLAN.md` (2026-10-05).

### Phase 6 — Performance, compatibility, release (PORT-12/13)

* Budget: 90 Hz stereo with the voxel meshes. Profile chunk rebuild spikes (do the meshing off the
  main thread with Burst/Jobs, which ship with the game).
* **KrokMP co-op compatibility.** A VR player in co-op is a strong feature. Test the mod alongside KrokMP.
* Version policy: the demo updates often. Patch by **method name** (Mono metadata), never by offsets.
  Check signatures at load and fail closed (VR disabled, game still runs).
* Package: a BepInEx plugin, the native bridge DLL and the asset bundle. Uninstall is deleting the
  folder. Publish on Nexus or Thunderstore.

## 4. Headless test loop (used from Phase 1 onward)

```
run-with-xrsim.ps1 (XR_RUNTIME_JSON per process, no machine-wide change)
  └─ dev-copy CasualtiesUnknown.exe  + BepInEx + CasualtyVR plugin (autotest: fixed seed, scripted inputs)
       └─ xr-tape layer (XR_API_LAYER_PATH)  →  trace.ndjson  →  xrtape_check.py --expect-runtime xr-sim
  xr-sim scenarios: scripted head/controller poses, button edges, session state faults
  xr-sim D3D11 compositor PNG capture  →  visual diff of eye images
```

Grade results honestly: xr-sim/xr-tape prove the **submission contract**, RenderDoc proves **what was
drawn**, and only a headset proves **it feels right**.

## 5. Top risks and unknowns

| # | Risk | Discriminator | Phase |
|---|---|---|---|
| R1 | The URP 2D Renderer may not support Unity XR stereo | UUVR spike 1a | 1 |
| R2 | ~~The graphics API may not be D3D11~~ **Cleared 2026-10-03:** D3D11 FL 11.1 (LIVE) | Probe `graphicsDeviceType` | 0 |
| R3 | `Light2D` may not light 3D extruded meshes as hoped | Flat voxel test with a 2D-Lit shader | 4 |
| R4 | Code that assumes orthographic (vision mask, clamps, shaders) | Phase 0 census plus the Phase 2 checklist | 2 |
| R5 | Demo updates break patches | Method-signature guard at load | 6 |
| R6 | The KrokMP autoupdater patches `Assembly-CSharp` | Keep it out of the dev copy, test it separately | 0/6 |
| R7 | **Largely cleared 2026-10-03** ([PHASE1_SPIKE_RECEIPT](PHASE1_SPIKE_RECEIPT.md)): lights, fog and post render correctly on extra perspective and off-axis cameras (LIVE, flat). Canted/rotated eyes and headset FOVs still to verify. Original concern: 2D light textures are generated in camera screen space at half the world RT resolution. Their behaviour under a perspective/off-axis stereo camera is unknown | Phase 1 spike: render one perspective camera, compare lit sprites with flat | 1 |
| R8 | **Mitigated:** bare cameras render at full resolution. Also: cameras cloned with `CopyFrom` must reset view/projection matrices. Original: `PixelPerfectCamera` forces a 640×360 world RT. A VR camera must not inherit it | Phase 1: VR cameras are created bare | 1 |
| R9 | Screen-space-overlay canvases never reach a VR eye | Phase 3 canvas re-targeting | 3 |
