# RENDER_PASS_CENSUS — gameplay frame, seed 1234

Capture: `Working\game-dev\BepInEx\CasualtyVR\run-20261003-200218\gameplay_frame5557.rdc` (23.8 MB),
taken 2026-10-03 by the probe (`--cvr-autostart --cvr-seed 1234 --cvr-renderdoc`) about 5 s after world
generation, at the lifepod start position. Window 1920×1080, RTX 3070, D3D11 FL 11.1. Grade: **LIVE**
(RenderDoc replay of the shipping build). Unity release builds emit no debug markers, so RenderDoc
groups passes only by render-target changes ("Colour Pass #n"). Pass roles below come from RT
size/format and bound resources.

## Frame shape

| Events | RenderDoc group | Target | What it is | Draws | Evidence |
|---|---|---|---|---|---|
| 72 | — | — | Full-screen triangle (setup blit) | 1 | Draw(3) |
| 75–258 | Colour Pass #1–#3 | 320×180 R11G11B10F (×3) | **2D light textures**, one per light batch / layer range. Point-light volume meshes (36 indices) | ~23 | PS samples `FalloffLookupTexture` (URP 2D point light) |
| 286–1499 | Colour Pass #4 | **640×360** R11G11B10F + D32S8 | **World**: tilemap chunks (1536-index chunk meshes), sprites, ragdoll, liquids, line renderers. Sprite-Lit shaders read the light textures | ~70 | RT 1902 / depth 1910 |
| 1519–1908 | (part of #4/#5) | ≤640×360 chain | **Post-processing**: bloom down/up sample chain, then colour grading, film grain | ~16 | Full-screen Draw(3)s, RT 1968… |
| 1928 | Colour Pass #5 | **1920×1080** (RT 910) | **Final upscale blit** of the 640×360 image to the backbuffer | 1 | Draw(3) → 910 |
| 1929–2171 | Colour Pass #5/#6 | 1920×1080 + depth 914 | Depth clear, then **screen-space UI canvases** at full resolution | ~22 | DrawIndexed → 910 |
| 2178 | — | — | Present | — | |

**GPU total: 1.31 ms per frame** (149 draws, RenderDoc timing counters). The flat game is far
from GPU-bound. That leaves a large stereo budget (2 eyes × headset resolution) even with the voxel
meshes planned for Phase 4.

## Findings that change the VR work

1. **The game renders the world at 640×360 and upscales it.** This comes from `PixelPerfectCamera` on
   `Main Camera` (orthoSize 22.5 → 45 blocks × 8 px/block = 360 px). A VR camera must **not** carry
   `PixelPerfectCamera`. It needs to render at headset resolution. The pixel-art look survives
   because every sprite and tile texture is point-filtered (`filter=Point` in the probe). Grade: LIVE.
2. **2D lights are cheap and screen-space.** Only ~23 of the 2,078 `Light2D` components render. Light
   textures are at half the world RT resolution (320×180), in **the camera's screen space**. For a
   perspective stereo camera, the 2D Renderer generates light textures per camera. Whether they line
   up correctly for a perspective/off-axis camera is **unverified (INFERENCE)**. It is the first
   thing the Phase 1 spike has to look at.
3. **UI is drawn after the upscale, straight to the backbuffer**, as screen-space-overlay canvases.
   In VR these canvases render to nothing (overlay canvases draw only to the window). They have to be
   re-targeted (camera-space → render texture → world quad). This confirms the Phase 3 plan.
4. **Post-processing runs at the low resolution, before the upscale.** Bloom and film grain will need
   checking per eye (grain must not differ between the eyes, or it shimmers in stereo).
5. **No depth is used for 2D sorting beyond the world pass.** The world pass has a D32S8 depth target,
   but 2D sprites sort by sorting layer/order. Phase 2's z-offset "pop-up book" and Phase 4's voxel
   meshes will need depth writes enabled on materials that currently rely on painter's order.

## Reproduce

```powershell
tools\deploy-dev.ps1
tools\launch-dev.ps1 -RenderDoc -Windowed --cvr-autostart --cvr-seed 1234 --cvr-quit
# capture lands in Working\game-dev\BepInEx\CasualtyVR\run-<stamp>\gameplay_frame*.rdc
```

Do **not** launch through `renderdoccmd capture`. It stops Doorstop from bootstrapping BepInEx, so
the game runs without the probe or the sandbox (observed 2026-10-03). `RenderDocPreload` loads
`renderdoc.dll` from the BepInEx preloader instead. That happens before Unity creates its D3D11
device, so RenderDoc still hooks the device.
