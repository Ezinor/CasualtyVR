# PERF_FP — first-person performance (2026-10-06)

Tools:
- `tools/fp-perf.ps1`: per-variant config overrides on the dev copy, restored afterwards. `-Extra` adds game flags;
  "diorama" means first person is not created (`--cvr-dbg-nofp`).
- `tools/vr-perf-matrix.ps1`.

Verbose `fp:` log fields: `fpUpdate` / `fpRender` (ms per frame of first-person code, per frame and in the eye-render
hooks), and `popup`.

New debug flags:
- `--cvr-dbg-nofp`
- `--cvr-dbg-modifier <i>[,<i>]` applies layer modifiers as the game does: 0 Foggy, 1 Warm, 2 Hot, 3 Cold,
  4 Freezing, 5 Flooded, 6 Dry, 7 Rainy, 8 Infested, 9 Ionized, 10 Unchipped.

## The finding

The dev config had `[FirstPerson] Enabled = true` (from the user's headset session), so "baseline" runs were first
person.

- The diorama was unchanged at **90 fps**.
- First person ran at **40 fps** in the default scene.
- It was **CPU-bound**: GPU utilisation was 37% at 65 W, against 46% at 131 W for the diorama.

## Fixes

| Change | Default scene, first person (xr-sim) |
|---|---|
| Before | 40 fps; first-person code 1.9 + 2.4 ms/frame |
| **Far plane 4000 → `FirstPerson.ViewDistance` (60 blocks)**. Looking along the world's rows, the eyes culled, sorted and drew every sprite, light and chunk across the whole world in that direction. | 69 fps |
| **Range-limited lights:** the scan keeps only lights that can reach the view, instead of turning 1830 lights (and 247 copies) per eye | 90 fps; code 0.8 + 0.4 ms |
| **Static wall copies:** full refresh twice a second, otherwise only near ones shown. Back-wall copies are range-limited. | (included above) |
| **Spatial queries** (`Physics2D.OverlapCircleAll`) instead of `FindObjectsOfType` for billboard groups and back-wall objects; light scan once a second; aim ray in coarse steps then refined (35 instead of 100 probes) | Infested: code 0.33 + 0.17 ms |

## Results (xr-sim, seed 1234, 90 Hz)

| Scene | Diorama | First person |
|---|---|---|
| Default (lifepod) | 90 | 89–90 |
| Rainy (`--cvr-dbg-modifier 7`) | 90 | 90 |
| Infested (8, shadecrawlers everywhere) | 86 | 84 |

**Infested:** the remaining few fps are the game's crowd itself; the diorama loses about the same.

**Checks:** `tools/fp-smoke.ps1` 9/9, and xr-tape 19/19 in both views.

**Setting:** *View distance (blocks)* in the *1st person* tab (20–200).
