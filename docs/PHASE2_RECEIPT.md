# PHASE2_RECEIPT — first-person comfort (2026-10-18)

Phase 2 of [BUILD_PLAN.md](BUILD_PLAN.md): FP 8a–8g of [FIRSTPERSON_REVIEW.md](FIRSTPERSON_REVIEW.md) and IP 7, 9, 11
and 12 of [IMPROVEMENT_PLAN.md](IMPROVEMENT_PLAN.md), with decisions D1 (keep 6.5 m/s) and D2 (*Intense* by default).
Test build: `1.3.0-test2`, which includes Phase 1 ([PHASE1_RECEIPT.md](PHASE1_RECEIPT.md)). All numbers are headless,
from xr-sim (Quest 3 profile, 90 Hz), on the dev copy with seed 1234, compared with [PHASE0_RECEIPT.md](PHASE0_RECEIPT.md).

- **Code:**
  - [`FirstPerson.cs`](../src/CasualtyVR.Core/FirstPerson.cs): the eye path, comfort overlays, `ComfortPresets`, the speed patch;
  - [`Scans.cs`](../src/CasualtyVR.Core/Scans.cs) (new), used by every periodic scan;
  - `VrInput`: walk or run;
  - `VrSettingsMenu`: the *Comfort* tab;
  - `Plugin`: six settings.
- **Tests:**
  - [`tools/fp-comfort.ps1`](../tools/fp-comfort.ps1) (new);
  - [`tools/fp-path.ps1`](../tools/fp-path.ps1) (walk segment, `-Preset`, `-Extra`);
  - `tools/vr-menu.ps1` (preset click);
  - `tools/vr-perf-matrix.ps1`.
- **Flags:**
  - `--cvr-dbg-preset <Intense|Moderate|Gentle>` applies a preset (saved);
  - `--cvr-dbg-nofilter` turns off the horizontal filter (Phase 0 behaviour);
  - `--cvr-dbg-pathdump` logs a per-frame dump with the `fp path` lines.

## What was built

| Item | What | Default |
|---|---|---|
| **8a** Walk on a half push | The left stick part way (0.25–0.72) walks: the player's top speed is capped at `WalkSpeedMetres` × the first-person scale. All the way (≥ 0.82) runs, with hysteresis in between. Movement now starts at a 0.25 push (was 0.5). Keyboard input is unaffected | On, 1.4 m/s |
| **8b** Running speed in m/s | The *Running speed* slider shows m/s (the body's `maxSpeed` × the fraction ÷ the scale). It shows % until the scale is known | 0.8 = 6.5 m/s (D1) |
| **8c** Fade inside walls (IP 11) | The view fades to black while the tracked head is inside a solid block (see-through blocks excepted) or more than 5 cm past the tunnel's sides. Fades in over 0.12 s and back out over 0.2 s, on the existing overlay | On |
| **8d** Steady eye height | Standing on the ground: the eye is the remembered standing eye height above the bottom of the body's box collider, which slides along the ground. It no longer rides the head limb, which the animations swing. A moment off the ground (under 0.15 s, not rising) and the game's landing squat do not count; a jump, a fall, climbing or a deliberate crouch do. Vertical follow at most 12/s, so steps glide | Off (Moderate, Gentle) |
| **IP 9** Judder | See *What the judder was* below. Standing, the eye moves at the body's own velocity and eases the remaining difference out at 8/s | Always |
| **8e** Snap blink | On a snap turn, that frame is black and the view fades back in over 0.12 s | Off (Moderate, Gentle) |
| **8f** Comfort presets | A row on *Comfort*: **Intense** (blink off, steady off, vignette 0, ragdoll *Stay*), **Moderate** (blink, steady, vignette 0.3), **Gentle** (also vignette 0.6, ragdoll → diorama, running ≤ 0.5 ≈ 4 m/s, snap turning). The lit preset is worked out from the settings; none lit = your own mix | Intense (D2) |
| **8g** Scale pop (IP 12) | The measured standing eye height is saved (`FirstPerson.MeasuredEyeHeight`) and used from the first frame. A re-measure within 8 % keeps the saved value: the measure catches the idle sway at any point (4.87–5.01 blocks) | — |
| **IP 7** Scan staggering | Every periodic scan runs on its own frame of its period (`Scans.Due`, the phases are in `Scans.cs`), never twice in one frame. Verbose runs time each scan (`perf: scans` line) | — |

The **Comfort** tab: *Comfort preset* · *Turning* · *Running speed* · *Walk on a half push* (switch and m/s) · *Fall
vignette* · *When knocked down* · *Blackout when unconscious*. Under *More settings*: *Steady eye height* · *Snap
blink* · *Fade inside walls* · *Smooth turn speed* · *Camera smoothing*.

### What the judder was (measured, `--cvr-dbg-pathdump`)

IP 9 guessed 50 Hz physics stepping. The per-frame dump disproves that.

- **No stepping:** the raw target moves on every frame, whether a physics step ran that frame or not. Its speed
  equals the body's velocity, so the game's bodies are already interpolated.
- **What does happen:**
  - every second or two, the position jumps about 15 cm forward in one frame and then stalls for a frame, while the
    velocity stays steady (`18.0/6.1 … 0.0/6.1`: m/s of position / of velocity);
  - these come on landings, steps and other contact corrections;
  - Phase 0's "uneven steps" are these jumps.
- **First attempt, removed:** interpolating between physics steps made walking worse (eye CV 0.12 against the raw
  0.04).
- **The filter used:** the eye follows the velocity and eases each jump out over a few frames. On smooth running it
  adds no lag.

Phase 0's "run bob of 1.4 m per 2 s" was mostly real descent: the test tunnel drops several blocks along its length.
The new *on the ground* figure counts only frames on the ground and not moving vertically.

## Results

### Eye path (`fp-path.ps1`; median, with the worst window in brackets)

| | Filter off (Phase 0) | Intense (default) | Moderate |
|---|---|---|---|
| Standing: up-and-down per 2 s | 18 cm | 18 cm | **0 cm** |
| Running, horizontal evenness (eye speed CV) | 0.15 (0.19) | **0.08** (0.12) | **0.08** (0.14) |
| Walking, horizontal evenness | 0.23 (0.43) | **0.08** (0.23) | **0.08** (0.25) |
| Running, on-the-ground vertical travel per 2 s | 77–152 cm | 47–154 cm | **8–101 cm** |
| Walking, on-the-ground vertical travel per 2 s | 44–137 cm | 24–103 cm | **11–67 cm** |
| Speeds | walk 1.39–1.58, run up to 6.5 m/s | walk 1.42–1.64, run up to 6.4 | same |

What is left on the ground under Moderate is the tunnel's steps, which now glide. The raw target's CV stays
0.24–0.31 running, as expected: the filter is what changed.

### Frame timing (`vr-perf-matrix.ps1`, 2 runs each)

| Mode | p99 (Phase 0 → now) | Missed display slots (Phase 0 → now) | Periodic gaps |
|---|---|---|---|
| Diorama | 21.2 / 17.8 → **15.8 / 18.2 ms** | 14/904, 14/902 → 4/902, 21/870 | One run still shows three 120-frame gaps |
| First person | 19.2 / 19.1 → **15.4 / 16.2 ms** | 15/909, 15/898 → **11/909, 5/900** | **None** (was 60× / 120× repeats) |

**Scan costs** (the slowest run of each, ms):

| Scan | Cost |
|---|---|
| Pop-up depth refresh (every 120 frames) | **4–10** |
| Character thickness | 1.2–4.7 |
| Object depth | 2–4.4 |
| FP lights | 2.2–2.7 |
| FP body | 1.5 |
| FP back wall, billboards | about 1 |
| The rest | < 0.2 |

The pop-up depth refresh (`FindObjectsOfType<Renderer>`) alone can still miss a slot at 90 Hz: the diorama's
120-frame gaps line up with its frame. That is left for Phase 7 (a spatial query or incremental refresh).

### Checks

| Suite | Result |
|---|---|
| `fp-comfort.ps1` | **0 failures** (scenarios below) |
| `vr-menu.ps1` | **0 failures**, including a laser click on *Moderate* that sets its settings |
| `vr-lifecycle.ps1` | **46/46**. The heading check first read the new `perf: scans` line instead of the perf line; its pattern now asks for `fwd` |
| `fp-smoke.ps1` | 11/11 |
| xr-tape | 19/19 (1 skipped, as before) |

`fp-comfort.ps1` scenarios:

- **wall:** leaning 1 m sideways → "head inside a wall" (head z −3.14, tunnel −2..2), capture
  `Working/comfort-phase2/wall_left.png`; back → "back in the open".
- **blink:** the Gentle preset; the flick blinks.
- **scale:** the first run measures and saves the height, the next starts from it, a re-measure keeps it.
- **walk:** half push 1.40 m/s; full push (Gentle) 3.66 m/s.

The dev config was restored after every run (SHA-256 `d1d36925…`).

## Test build

`dist/CasualtyVR-1.3.0-test2.zip` (1018 KB), SHA-256 `9D7D2297B67BCB8D60DE54FD6EF2DA1A23A6E5CC91843937D7E1A689D4FE8D91`.
It contains Phase 1 and Phase 2. `1.3.0-test1` (Phase 1 only) is still in `dist/`. The changelog and README are
updated ([release/CHANGELOG.md](../release/CHANGELOG.md), *First-person mode › Settings*).

## Headset pass

**2026-10-18: passed.** You tested 1.3.0-test2 in the headset: "Both Phases look good."

## For the headset pass

1. **Walking:** push the stick part way. Is 1.4 m/s a usable walk, and is the change to a run (about ¾ push) easy to
   find?
2. **Running:** is it smoother than 1.2.2, with no small jolts on landings and steps?
3. **Walls:** lean into a side wall and put your head into a block. Does the fade come at the right moment? Is it
   too slow or too quick?
4. **Moderate preset** (*Comfort* tab): standing still, the view should no longer breathe. Run over steps and land a
   jump. Do steps glide well, and do the snap turn's blink and the vignette help or annoy?
5. **Spawn:** the world should not resize a moment after you appear (from the second time you play first person).
6. **Gentle:** a quick try. Running at about 4 m/s and the diorama while knocked down.
