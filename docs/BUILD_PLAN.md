# BUILD_PLAN — unified build order (2026-10-18)

One order for everything still open in the three plans:

- [IMPROVEMENT_PLAN.md](IMPROVEMENT_PLAN.md) — code review; items 1–16. Group A is done (1.2.1).
- [FIRSTPERSON_REVIEW.md](FIRSTPERSON_REVIEW.md) — first person, FP-8 to FP-12, with decisions D1–D6.
- [VRMENU_REVIEW.md](VRMENU_REVIEW.md) — the VR settings menu, M1 to M5, with decisions V1–V7.

Those documents keep the details; this one only orders, merges and gates the work. Item references are as written
there, for example *IP 7*, *FP 8c*, *M2c*.

## Merged or dropped

| Here | Same as | Note |
|---|---|---|
| FP 8c head in walls | IP 11 | Built once |
| FP 8g scale pop | IP 12 | — |
| FP 10a sound at the head | IP 10 | — |
| FP 8d vertical steadiness | IP 9 (judder) | Measured together, fixed together |
| M5a grab to move | — | Dropped (V6: keep the number rows) |
| IP 1, 2, 4, 5, 6, 8 (cap), 15 | — | Done in 1.2.1 |

## Principles

1. **Measure before adding per-frame work.** Phase 0 records a frame-time baseline; every later phase is compared
   with it.
2. **The menu framework comes before the features that add settings.** FP-8 to FP-12 add many settings; building
   them into the new menu once avoids building every row twice.
3. **One phase, one test build.** Each phase follows the routine already used:
   - backup;
   - build and deploy;
   - headless checks: `vr-lifecycle.ps1`, `fp-smoke.ps1`, xr-tape 19/19, plus the phase's new checks;
   - a receipt and a test build (`1.3.0-testN`);
   - your headset pass;
   - commit.
4. **Your headset verdict gates the next phase** where feel is the point (comfort, hands, menu look).

## Phases

### Phase 0 — Baseline and quick housekeeping (S)

**Done 2026-10-18:** see [PHASE0_RECEIPT.md](PHASE0_RECEIPT.md). The 60/120-frame hitch is confirmed, so scan staggering is in
Phase 2; the idle sway, the run bob and a moderate judder were measured for FP 8d.

| Work | From |
|---|---|
| Frame-time histogram in `fp-perf.ps1` / `vr-perf-matrix.ps1` (p50, p99, frames over budget), plus the bridge's skipped waits and submit-interval spread | IP 7 and 8 (the measuring half) |
| Log the first-person eye path while walking, jumping and landing (vertical steps, judder) | IP 9 / FP 8d (measure only) |
| Commit the review documents; fix the `tools/` vs `Tools/` folder name; clone the playbook as a git repo | IP 16 |

**Done when:** baseline numbers for diorama and first person are recorded in a receipt.

**Branch point:** if the histogram shows the 1.33 s scan hitch (IP 7), the scan staggering moves into Phase 2.

### Phase 1 — VR menu framework (M)

**Built 2026-10-18, test build `1.3.0-test1`:** see [PHASE1_RECEIPT.md](PHASE1_RECEIPT.md). Headless checks pass. **Headset
gate passed** (2026-10-18). V7 is still open.

| Work | From |
|---|---|
| The menu on its own enlarged panel, filling it | M1a (V2) |
| Fewer, bigger rows with whole-row hit areas; scrolling where needed | M1b |
| A description line (the setting's own config text) | M1c |
| Switches, segmented choices and sliders (drag) | M2a–c (V3) |
| Hover and click feedback, haptic ticks and the game's click sound | M2d |
| Row reset ↺ | M2e |
| Tabs by task (Comfort, View, First person, Hands & body, Controls, Menus & HUD, Passthrough, About); tuning rows behind **More settings** | M4a, M4b (V4) |
| Opens in front of you (uses the recenter heading) | M5b |

**Checks:** angular size ≥ 2.5° for every control (computed headless); scripted laser use of a switch, a segment, a
slider and a tab.

**Headset gate:** readability and ease of hitting. **V7** (also enlarge the game's own menus) is decided here.

### Phase 2 — First-person comfort (M)

**Built 2026-10-18, test build `1.3.0-test2`:** see [PHASE2_RECEIPT.md](PHASE2_RECEIPT.md). The judder turned out to
be position corrections, not 50 Hz stepping (the bodies are already interpolated). The diorama's pop-up depth refresh
(4–10 ms every 120 frames) is moved to Phase 7. **Headset gate passed** (2026-10-18).

| Work | From |
|---|---|
| Analog walking (half push = walk); the run speed shown in m/s, default unchanged | FP 8a, 8b (D1) |
| Fade when the head is inside a block | FP 8c = IP 11 |
| Vertical steadiness: idle sway and run bob (measured), interpolate the anchor between physics steps (judder) | FP 8d, IP 9 |
| Snap blink | FP 8e |
| Comfort presets on the Comfort tab, *Intense* default | FP 8f (D2) |
| Scale pop | FP 8g = IP 12 |
| Scan staggering (Phase 0 confirmed the 60/120-frame hitch) | IP 7 |

**Headset gate:** comfort, including walking at half push, landings, turning, and leaning into walls.

### Phase 3 — Menu look and controls picture (M)

**Built 2026-10-18, test build `1.3.0-test7`:** see [PHASE3_RECEIPT.md](PHASE3_RECEIPT.md). The game's own frame
sprites are used (V1: full). V7 decided yes and built: the game's crafting, wound view, trade and (first person)
inventory are zoomed too. Headset pass of test4 (2026-10-23): look and pause button good; two fixes in test5
(Controls lines on hover only, the health screen's bottom buttons); test5 pass: two more in test6 (two-line Controls boxes,
zoomed menus at most 40 degrees tall); test6 pass: tab names fitted in test7.

| Work | From |
|---|---|
| The game's frames, glow and translucent panels, from its own sprites ("full if practical"; report if not) | M3a (V1) |
| Row dividers and accent colours; tab icons | M3b, M3c |
| A round VR button in the pause menu, matching the game's | M3d |
| The Controls page as a controller picture | M4c (V5) |
| Enlarge the game's own menus on the dashboard (V7 = yes, built) | V7 |

**Headset gate:** the look.

### Phase 4 — Hands (L; split into 4a and 4b if large)

**4a built 2026-10-23, test build `1.3.0-test8`:** see [PHASE4A_RECEIPT.md](PHASE4A_RECEIPT.md). Swinging a gun did
fire it (the attack key uses the held item's left-button action); swings are now gated by the game's own item data. The
motion takes are recorded in the headset (*Hands & body › More settings*); generated takes stand in until then.

**4b built 2026-10-23, test build `1.3.0-test9`:** see [PHASE4B_RECEIPT.md](PHASE4B_RECEIPT.md). The pull went through
the game's own pickup rules (`DoPickupCheck`, `PickUpItem`) and passed in the headset. After that pass, `1.3.0-test10`:
voxel hands in the arm's style instead of the minigame drawings, and a glowing hand on the item instead of the glow;
`1.3.0-test11`: the hands follow the aim pose, with three fingers, a thumb and claws. **Headset gate passed
2026-10-24** (hands, claws, the marker, the pull). Swings and throws were tested in 4a; record the motion takes when
convenient.

| Work | From |
|---|---|
| **4a:** the OpenXR grip pose in the bridge; hands, arms, held items and throw velocity on it | FP 9a |
| **4a:** the swing recipe, with the held-item gate (first check whether swinging a gun fires it) | FP 9e |
| **4a:** the throw recipe | FP 9d |
| **4a:** motion takes recorded in the headset, replayed headless | FP 9f |
| **4b:** hand poses (open / grip / point) | FP 9b |
| **4b:** grab affordance (hand pose, haptic tick and faint glow) | FP 9c (D5) |
| **4b:** the Alyx-style distance pull, using 9c's selection | FP 9g (D4) |

**Headset gate:** swings, throws and grabs feel deliberate. Record the motion takes during this pass.

### Phase 5 — Senses and movement verbs (M)

| Work | From |
|---|---|
| Sound at the head | FP 10a = IP 10 |
| Haptics from game events: damage, hits that landed, optional low-health heartbeat | FP 10b |
| Real crouch, on by default | FP 12a (D6) |
| The ladder and rope on/off check | FP 12b |

**Headset gate:** sound direction while facing both ways, and crouching.

### Phase 6 — Information within reach (M–L)

| Work | From |
|---|---|
| A wrist HUD as the default; the floating panel as an option (Menus & HUD tab) | FP 11a (D3) |
| Body inventory zones with hover ticks | FP 11b |
| Touch for the menu in first person | M2f |

**Headset gate:** reading the wrist, finding the zones, and touching the menu.

### Phase 7 — Robustness and performance (M)

| Work | From |
|---|---|
| Native teardown on the render thread | IP 3 |
| Depth layer submission; eye resolution scale | IP 13 |
| Re-run the Phase 0 histogram and compare | IP 7/8 |
| The pop-up depth refresh (`FindObjectsOfType<Renderer>`, 4–10 ms every 120 frames): a spatial query or an incremental refresh (Phase 2 measurement) | IP 7 |

### Phase 8 — Release 1.3.0 (S–M)

| Work | From |
|---|---|
| Hardware coverage: ask testers for Index / Vive / WMR / G2 and Proton; co-op re-check | IP 14 |
| Housekeeping: self-tests for the maths, optional CI build of the bridge, the config-key consumer check (playbook ch. 03) | IP 16 |
| Release notes, README, receipt, package | — |

## At a glance

| Phase | Theme | Size | Your gate |
|---|---|---|---|
| 0 | Baseline, housekeeping | S | — |
| 1 | Menu framework | M | Readability; V7 |
| 2 | First-person comfort | M | Comfort |
| 3 | Menu look, controls picture | M | Look |
| 4 | Hands | L | Feel; motion takes |
| 5 | Senses, crouch, ladders | M | Sound, crouch |
| 6 | Wrist HUD, body zones, menu touch | M–L | Use |
| 7 | Robustness, performance | M | — |
| 8 | Release 1.3.0 | S–M | Final pass |

## Open decision

- **V7** — decided 2026-10-18: yes (built in Phase 3).
