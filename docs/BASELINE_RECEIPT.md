# BASELINE_RECEIPT — Phase 0 (PORT-02)

Date 2026-10-03. Target: Casualties: Unknown Demo 7.0.1, Unity 2022.3.62f3, hashes in
[TARGET_PROFILE.md](TARGET_PROFILE.md).

## Dev copy

`Working\game-dev` is a copy of the Steam install **without** any of the user's mods, plus:

| Change | Why |
|---|---|
| BepInEx 5.4.23.5 x64 (fresh from GitHub) | Plugin host |
| `globalgamemanagers` productName `CasualtiesUnknown` → `CasualtyVRDevCopy` (same length, 9 bytes; original in `globalgamemanagers.orig`); same in `app.info` | **Engine-level isolation.** Unity derives `persistentDataPath` (`LocalLow\Orsoniks\<product>`) and the PlayerPrefs registry key (`HKCU\Software\Orsoniks\<product>`) from this name. Without the change, the dev copy reads and writes the user's real save, settings and registry. This works even if BepInEx fails to load |
| `CasualtyVR.Sandbox.Patcher` (BepInEx preloader) + `CasualtyVR.Sandbox.Runtime.dll` in `Managed` | Game-level isolation: rewrites 11 `Application.persistentDataPath` and 25 `PlayerPrefs.*` call sites in Assembly-CSharp to `<dev copy>\CasualtyVR_UserData` |
| `RenderDocPreload` (same patcher assembly) | Loads `renderdoc.dll` before D3D11 device creation when `--cvr-renderdoc` is passed |
| `CasualtyVR.Probe` plugin | Read-only probe and automation (see below) |

Uninstall or vanilla control: delete `winhttp.dll` (disables Doorstop/BepInEx entirely). Restore
`globalgamemanagers.orig` only if a true retail-identity run is needed, and never run that against
the user's data without a backup.

**Why both layers:** on 2026-10-03, launching through `renderdoccmd` stopped BepInEx from loading.
With only the game-level sandbox in place, that run sat at the main menu pointed at the user's real
save folder. It was caught and killed. The user's data was verified unchanged against the
pre-session backup in `Working\backup-userdata-20261003\`. The identity patch was added afterwards
so that one layer failing is not enough to reach user data.

## One-command runs

```powershell
tools\deploy-dev.ps1                                                    # build + copy into the dev copy
tools\launch-dev.ps1 -Windowed --cvr-autostart --cvr-seed 1234 --cvr-quit   # unattended baseline
tools\launch-dev.ps1 -RenderDoc -Windowed --cvr-autostart --cvr-seed 1234 --cvr-quit  # + frame capture
tools\launch-dev.ps1                                                    # interactive play, F8 = probe report
```

Probe flags: `--cvr-autostart` (marks the sandboxed tutorial flag done and starts a run),
`--cvr-seed N`, `--cvr-probe-delay S`, `--cvr-screenshot`, `--cvr-renderdoc`, `--cvr-quit`. Reports
go to `Working\game-dev\BepInEx\CasualtyVR\run-<stamp>\`. Unity's log goes to
`Working\game-dev\CasualtyVR_Logs\`.

## Deterministic test scene

Seed 1234 → lifepod start at about (0, 485). The world hash is **`fnv1a64=4947A0FE6D30C272`** in
three separate runs (19:49, 19:50, 20:00). Seed 999 gives `395F02800D2B1576` (positive control: the
seed controls generation). Grade: LIVE. Only the block grid is hashed; entity placement and
`Time`-driven effects are not yet shown to be deterministic.

## Stock numbers (seed 1234, about 5 s after generation)

| Item | Value | Grade |
|---|---|---|
| Graphics API | Direct3D 11, FL 11.1 | LIVE |
| Pipeline | URP, single renderer `Renderer2D` (`Renderer2DData`) | LIVE |
| XR | `XRSettings.enabled=False`, no XR display subsystem, `supportedDevices=[None]` | LIVE |
| Main camera | Orthographic, size 22.5, z=-3, `PixelPerfectCamera` + `UniversalAdditionalCameraData` (Base, no stack) | LIVE |
| World render resolution | 640×360, upscaled to window | LIVE (RenderDoc) |
| GPU time | 1.31 ms per frame, 149 draws | LIVE (RenderDoc) |
| CPU frame | 120 fps vsync-locked, worst frame 9.0 ms over 120 frames (windowed 1920×1080) | LIVE |
| World | 1024×1024 blocks, 1 block = 1 unit, `CHUNKSIZE` 64 → 16×16 Tilemap chunks (15 enabled near player) | LIVE |
| Renderers | 4,009 active: 3,080 SpriteRenderer, 475 LineRenderer, 284 TilemapRenderer, 160 particle, 10 mesh | LIVE |
| Lights | 2,078 `Light2D` (1 global, 2,077 point, no shadows). About 23 actually drawn | LIVE |
| Sorting layers | Back(-1), Default(0), Top(1), LineOfSight(2) | LIVE |
| Canvases | 4 ScreenSpaceOverlay (Console, GlobalDark, Main Camera/Canvas, World/Canvas) + 1 WorldSpace (Main Camera/WorldCanvas) | LIVE |
| Post | Global volume: ColorAdjustments, FilmGrain, Bloom, LiftGammaGain | LIVE |
| Player | `Experiment/Body`, 15 limbs, 19 SpriteRenderers, sprites 8 px/unit, point filtered, `Shader Graphs/Limb` | LIVE |

## Known gaps

* The game's own settings (1920×1080 FullScreenWindow, vsync on) override `-screen-*` arguments
  once a run starts. `-Windowed` only affects the menu. Harmless for now.
* BepInEx's manager GameObject is destroyed when the first scene loads. Plugins must host their
  behaviour on their own `DontDestroyOnLoad` object (the probe's `ProbeRunner` does).
