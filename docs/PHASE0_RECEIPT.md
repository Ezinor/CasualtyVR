# PHASE0_RECEIPT — baseline and housekeeping (2026-10-18)

Phase 0 of [BUILD_PLAN.md](BUILD_PLAN.md). All measurements are headless under xr-sim (Quest 3 / VDXR profile,
90 Hz), on the dev copy, seed 1234. These are the numbers later phases are compared against.

## Housekeeping

- **Review documents:** committed (`26aac33`).
- **Folder case:** the tracked scripts moved from `Tools/` to `tools/` in git, matching the docs. References in
  CLAUDE.md, the scripts and `.gitignore` are now lowercase. Windows is unaffected; a case-sensitive clone now
  matches the docs.
- **Playbook:** `VR Modding Playbook/` is now a git clone of `phunkaeg/vr-modding-playbook`. The downloaded copy
  was identical to `main` at `654c911`, so nothing changed on disk. Future updates: `git pull` there, and
  `git log` shows what changed.

## Measurement added

| Where | What |
|---|---|
| `VrRig.LogPerf` (verbose perf line, every 2 s) | `frame p50 / p99 ms`, `over 1.5x period n/N` (frames that missed their display slot) and the period itself; a `perf: slow frames …` line with the frame numbers of slow frames |
| `FirstPerson.PathStats` (verbose `fp path:` line) | Steady-speed evenness of the eye and of the head limb (coefficient of variation of per-frame speed at over 80 % of top speed); vertical steps: largest single-frame step, total travel, net change |
| `tools/vr-perf-matrix.ps1` | Also reports the median p99, missed/total frames, and the most common gap between slow frames |
| `tools/fp-path.ps1` (new) | First person in the open test tunnel (`--cvr-dbg-cave`): standing, four runs between the walls (flick-turning at each), and jumps; prints the eye-path lines per segment |

## Baseline

### Frame timing (`vr-perf-matrix.ps1`, 2 runs each, steady state after the world is ready)

| Mode | Median fps | p99 frame time | Missed display slots | Most common gap between slow frames |
|---|---|---|---|---|
| Diorama (`--cvr-dbg-nofp`) | 90 / 90 | 21.2 / 17.8 ms | 14/904, 14/902 (1.6 %) | 1 (slow frames come in pairs) |
| First person (`--cvr-dbg-fp`) | 90 / 89.9 | 19.2 / 19.1 ms | 15/909, 15/898 (1.6 %) | 1 |

### The periodic hitch (IP 7): confirmed

In the longer first-person runs, the gaps between slow-frame clusters after loading are **60 frames** (22× and 12×)
and **120 frames** (9× and 4×). For example: 1564, 1624, 1684, 1744, 1804, 1924…

That is the 60-frame scans (FP lights and body refresh) and the 120-frame ones. The short diorama runs show
30 / 60 / 120 gaps too. As the build plan says for this case, **scan staggering moves into Phase 2**.

### First-person eye path (`fp-path.ps1`)

| Segment | Result |
|---|---|
| Standing still | Vertical travel **18 cm per 2 s**, net 0: a steady idle sway of the eye (max step 0.2 cm a frame) |
| Running at top speed (6.5 m/s) | Head-limb speed varies **25–44 %** frame to frame; the smoothed eye **14–19 %**. Head-limb stalls are rare (0–2 per 40–80 frames), so the steps are uneven rather than missing: a moderate judder |
| Running, vertical | About 1.4 m of up-and-down travel per 2 s with a net change of about 0.2 m: a strong run bob. Single-frame steps up to 18 cm (that one window included a drop) |
| A jump | About 2.5 m of vertical travel; steps up to 10 cm a frame |

**For Phase 2 (FP 8d, IP 9):** the idle sway and the run bob are the biggest vertical-comfort problems. Meta
advises against head-bob; the guidance is in FIRSTPERSON_REVIEW. The horizontal judder is moderate. Smoothing
already halves it; interpolating the anchor between physics steps would remove the rest.

Limit: the test tunnel is about 15 blocks long, so each run covers only about 0.7 s at top speed. The evenness
figures come from 40–80 frames per window.

## Checks

- **Build:** clean.
- **xr-tape:** 19/19 on this build.
- **Dev config:** restored to the original (BepInEx re-saves the header and BOM; the settings were identical).
- **Backup:** `Working/backups/pre-phase0-20261018`.
