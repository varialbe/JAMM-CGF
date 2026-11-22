# Performance status — 2026-11-22

## Fish-Sleep — merged to `main` (8a64c74)

Far-away fish sleep; sleeping fish are re-checked only every 8 ticks (staggered). Local PC measurements,
unprofiled, mean of 2 interleaved runs:

| Build | Scene | Before | After | Gain | Frame time |
|---|---|---|---|---|---|
| vanilla | dock | 49.1 FPS | 89.0 FPS | +81% | 19.8 → 10.7 ms (−46%) |
| vanilla | deepwater | 49.5 FPS | 96.6 FPS | +95% | 19.7 → 9.9 ms (−50%) |
| public-5 | dock | 12.1 FPS | 21.1 FPS | +74% | 81.1 → 45.8 ms (−44%) |
| public-5 | deepwater | 12.7 FPS | 20.2 FPS | +59% | 77.1 → 48.1 ms (−38%) |
| all-7 | dock | 16.8 FPS | 19.8 FPS | +18% | 57.9 → 48.8 ms (−16%) |
| all-7 | deepwater | 16.1 FPS | 19.1 FPS | +19% | 60.2 → 50.5 ms (−16%) |

- Fish AI time per frame (bench frame timings): vanilla dock 14.2 → 6.4 ms (−55%), public-5 dock 41.3 → 19.9 ms (−52%).
- Versus the previous VPS version (3fc3b67): vanilla 68 → 89 FPS (+31%), from the 8-tick fast path and a population
  fix. Vanilla FishThinner culls only below 60 FPS, so faster frames let the population grow from ~940 to ~1,460
  (+55%); it is back to ~940.
- Own-mods profile (perf, hd-lite, console, smooth, silo, weather-pack, endless-ocean): 160 FPS dock and deepwater,
  94 FPS sonar.
- Installed but disabled: negligible overhead.

**Stability:** weather (all 8 states) and Endless Ocean (to x ≈ 1.1M incl. save and reload) with `sonar_missing = 0`;
silo and stress (300 s, 59 cycles) clean; autoplay 2 × 20 min (seeds 11002/11005, 165/157 actions), 0 crashes,
0 code errors; 667 core + 130 GUI tests green, 0 build warnings.

**Why all-7 gains only +18%:** ~1,800 fish are asleep there, but the remaining cost comes from modded/boss fish that
are excluded or run custom AI (large/huge custom fish, Cuddlefish, Kingfish, Cusper family) plus decor
(CaveRock, SceneItem, Weed). Next target.

## Other tracks

| Track | Status |
|---|---|
| Multicore via native DLL | **No-go.** `buffer_get_address` apparently returns 0 in the GM 1.4 runner; offloadable hotspot only 4.2–4.9% of frame time. |
| Fish AI clustering | **Negative.** No fish type is a suitable candidate. Closed. |
| Fish_Step micro-optimizations | Code done, default off, byte-identical when off, tests green. Needs 3 on/off profile pairs (vanilla + heavy pack) and 2 × 15 min autoplay. Small expected gain. |
| Fish virtualization (2b), engine/build opt (phase 3) | Code and exclusive event profiler exist; no confirmed FPS numbers yet. In validation queue. |
| Crash hunt (submarine, Harbor Town) | Committed, 645 core + 130 GUI tests green, 0 warnings. Remaining in-game seeds pending. |

## Main blocker

The VPS test box runs one game at a time (up to 9 runs queued), and its Steam session drops every 5–6 min, so ~30%
of runs end as EXITED and are retried. VPS games also pause while the same Steam account plays on the PC.
