# TARGET_PROFILE — Casualties: Unknown (Steam Demo)

Recorded 2026-10-03. Evidence grades follow the playbook (`SPEC SOURCE STATIC LIVE HEADSET AUTHOR INFERENCE`).

## Identity

| Item | Value | Grade |
|---|---|---|
| Game | *Casualties: Unknown* (formerly *Scav Prototype*), Orsoniks / Oro Interactive | AUTHOR (Wikipedia, Steam) |
| Build | Steam Demo, game version **7.0.1** (logged by KrokMP) | LIVE (BepInEx log) |
| Install | Steam, `steamapps\common\Casualties Unknown Demo` | STATIC |
| Engine | **Unity 2022.3.62f3** (96770f904ca7) | STATIC (UnityPlayer.dll version) |
| Scripting backend | **Mono** (`Managed\Assembly-CSharp.dll`, no `GameAssembly.dll`) | STATIC |
| Architecture | x64 | STATIC |
| Render pipeline | **URP with the 2D Renderer** (`Unity.RenderPipelines.Universal.2D.Internal`, `Light2D` used in ~10 classes) | STATIC + SOURCE |
| Graphics API | Probably D3D11 (Unity's Windows default). **Not confirmed yet.** | INFERENCE |
| Physics | **Physics2D only** (`Rigidbody2D`, `HingeJoint2D` ragdoll limbs) | SOURCE |
| Built-in XR | None. `ScriptingAssemblies.json` lists no XR assemblies, and `Plugins\x86_64` has no XR plugin | STATIC |

## Hashes (SHA-256)

```
CasualtiesUnknown.exe        003C996CBF572DB5C55459AC702EF092720E47C544B024B01B894A7B7041A8B9
UnityPlayer.dll              DC005461655B67912029EEA6BCC303FE244C3A336E2046107F934B901AB9CE2B
Assembly-CSharp.dll (as found, already patched? see below)
                             FD0AD2851F6BAE4AB2589634643B92E16696C0E36F090BDD3F4C41E8F1C97095
```

The KrokMP `autoupdater_patcher` rewrites `Assembly-CSharp` in memory at preload time. The on-disk DLL
may still be stock. That has not been verified against a clean Steam download.

## Mods already installed in this copy (affects the baseline)

BepInEx 5.4.23.5 (Doorstop 4.5.0), KrokMP co-op 3.1.2 (+ autoupdater patcher), CatPatch, Rsh Lib,
HerobrinesProsthetics, ScavShrapnelMod, MultiplayerSpriteReplacer, ExpiePettingMod, Cigs, Skip-Lore,
Sell From Bags, RemiyamuremodLayerUnlock. **None of them should be in the VR dev or baseline copy.**

## Code map (from ILSpy decompile at `Working\decompiled\Assembly-CSharp`, grade SOURCE)

| Concern | Owner | Notes for VR |
|---|---|---|
| Camera | `PlayerCamera` (singleton `PlayerCamera.main`) | **Orthographic** `Camera.main`. Clamped to the world bounds (`orthographicSize * aspect`). Also owns input, inventory, radial menu, tooltips, drag and drop, and the death screen |
| Input → world | `Camera.main.ScreenToWorldPoint(Input.mousePosition)` | Used ~20× in `PlayerCamera`. Aiming is `body.targetLookPos`. **The mouse is the game's pointer.** |
| Keys | `KeyBinds.GetBind("…")` → legacy `Input.GetKey*` | Legacy Input Manager, not the new Input System |
| World | `WorldGeneration.world`: `worldBlocks[x,y]` grid, `width/height`, `CHUNKSIZE` | Rendered as a **2D array of `Tilemap` chunks** (`chunks[,]`, `renderChunks[,]`), with culling toggled per chunk. `tiles[]` / `tileColors[]` per block id |
| Block API | `GetBlock`, `GetBlockInfo`, `WorldToBlockPos`, `BlockToWorldPos`, `ChunkUpdated[,]` events | **This is the voxelisation seam.** It is a clean grid with change events |
| Characters | `Body` + `Limb` (`SpriteRenderer`, `Rigidbody2D`, `HingeJoint2D`) | A 2D ragdoll. Sprites per limb, plus wound and wearable overlays |
| Lighting / vision | `GlobalDark`, `VisionMask.UpdateMask(playerPos, hitPoints)`, `Light2D` | A line-of-sight mask polygon plus 2D lights. 2D lights only exist in the 2D Renderer |
| Liquids | `FluidManager` (per-block liquid) | Can be voxelised later as a second grid layer |
| UI | Screen-space `mainCanvas` (`PlayerCamera.uiScale`), many **minigames** (surgery, bandage, syringe, keypad…) via `MinigameBase` | Has to become a world-space panel or a render-texture quad |
| Post-processing | `VolumeProfile[] biomeProfiles` | URP volumes. Check how they behave in stereo |

## Prior art and related work

* **No existing VR or 3D mod found for this game** (as of 2026-10-03). There are Bonelab player models
  *of* the character, but that is the reverse direction.
* **Krokosha co-op mod** (BepInEx + Harmony) proves deep hooks into `Body`, `PlayerCamera`, minigames and
  the console. Useful as a reference for which methods are stable to patch.
* **UUVR** (Raicuparta): a universal Unity VR injector (OpenXR/OpenVR), distributed through Rai Pal. It is
  the cheapest possible T0/T1 spike. Its compatibility with the 2D Renderer is unknown.
* **DramaticShapeVoxelMod**: a Lua mod for the *Pokémon Gen 1 recompilation*, not Unity. Its *method* is
  what transfers: extrude the terrain into real geometry, show characters as leaning sprite slabs, and
  use depth-buffer occlusion instead of y-sort. The result is a diorama with pitch control.
* Playbook prior art for Unity managed plugins: GTFO-VR, RoR2VR, BendyVR, White Knuckle VR, and the
  Unity VR-flip pattern (`18-beyond-the-native-injector.md#unity-vr-flip`). The flip itself does **not**
  apply here, because Unity 2022 removed the built-in `enabledVRDevices` path.
