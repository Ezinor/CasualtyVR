# LAYER_MODIFIERS — every layer modifier in VR (2026-10-06)

The game picks one random modifier per layer (`WorldGeneration.ApplyLayerModifiers`, chance from the run setting
`layermodifierchance`). `--cvr-dbg-modifier <i>[,<i>]` applies them as the game does.

| # | Modifier | What it does (decompiled) | VR result |
|---|---|---|---|
| 0 | Foggy | `SetFog(1)` | Fog sprite replaced by the mist (diorama) and by smooth shader fog (first person, `docs/SHADERS.md`) |
| 1–4 | Warm, Hot, Cold, Freezing | `temperatureOffset` ±4 / ±8 / −10 | Body temperature only. The frost and overheat effects come through the game's own full-screen pass, which the eyes already apply (head-locked, like the pain effects) |
| 5 | Flooded | `PlaceLiquids(25, 1, 600)` | Diorama: liquids fill their tunnels (LiquidVolume). First person: being underwater reads as blue water with the surface overhead. Minor: a dark seam where two water surfaces meet. 78–88 fps |
| 6 | Dry | clears all liquid | nothing VR-specific |
| 7 | Rainy | `forceRain`, `SetFog(0.8)` | **Fixed.** Diorama: the game's rain shader draws correctly, and the dark veil is the VR mist (fog 0.8). First person: the rain sheet was edge-on (invisible), so it is now hidden in the eyes and replaced by falling streak particles at the game's own rain strength (`VrRain.cs`, setting `Diorama.Rain`), 90 fps |
| 8 | Infested | shadecrawlers everywhere | Crowd: 86 fps diorama, 84 first person after the perf work (`docs/PERF_FP.md`); billboards fine |
| 9 | Ionized | rad barrels and antirad pickups | Barrels are objects (extruded or facing you); radiation uses the game's full-screen pass. 89–90 fps |
| 10 | Unchipped | `SetUnchipped(true)`: line-of-sight darkness | Diorama: the line-of-sight mask darkens what the character cannot see (capture `mod10dio`). First person: the mask is hidden because the walls occlude for real |

- **Errors:** none in any run.
- **Captures:** `Working/xrsim-shots/mod<i>{dio,fp}_*`.
- **Debug flag:** `--cvr-dbg-rain` forces the rain overlay to full strength for tests. The rain map is low or zero in
  many places.
- **Bug found and fixed on the way:** the rain particle system never emitted. Unity's automatic particle culling paused
  it while its bounds were empty, so it is now set to always simulate.
