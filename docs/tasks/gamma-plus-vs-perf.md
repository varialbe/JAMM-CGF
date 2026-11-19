# Task: Gamma+ vs JAMM Perf/Smooth — what does Gamma+ do better/worse? Adopt the good ideas.

## Context
Gamma+ (by Niko, Game Jolt, a performance/"smoothness" mod for CGF) overlaps with our built-in mods `perf` and `smooth`
(`src/Jamm.Core/Builtins/PerfMod.cs`, `PerfMode.cs`, `SmoothMod.cs`, docs/tasks/phase6a-perf-smooth.md, phase6c-perf-mode.md).
User-PC frametime measurements (ms, median/p95/p99):
| Profile | Dock | Sonar | Deep sea |
|---|---:|---:|---:|
| Vanilla replacement build | 18.04/20.90/23.16 | 19.09/21.93/23.32 | 18.09/21.41/24.77 |
| JAMM Perf | 17.25/31.85/54.49 | 19.66/23.22/24.61 | 20.07/32.15/40.14 |
| Gamma+ 3.1.0 | 35.03/36.41/36.50 | 35.65/36.40/36.45 | 35.84/36.40/36.49 |
Gamma+ runs at ~30 FPS (cap/pacing) but extremely even; Perf has spikes. Heavy pack (3 big mods) is CPU-bound in mod GML (~40 ms/tick),
Perf/Smooth bring nothing measurable there.
IMPORTANT game fact found tonight: vanilla `FishThinner_Step_0` counts `fps < 60` frames and then culls 10% of fish
(`FishThinner_Other_10`, deactivation). At 30 FPS caps (Gamma+) or slow PCs this culling fires constantly — check whether Gamma+
changes FishThinner/room_speed/fps logic and what that means for the fish population and for our perf mod.

## Material (read-only, outside the repo — never copy game data or decompiled game code into the repo)
`~/jamm-handover/2026-11-19/gamma/`: `gamma-plus-3.1.0.focus-code.txt`, `jammp.focus-code.txt`, `gamma-nearest-public.focus-code.txt`,
`gamma-plus-diff.log`, `lights.gml`, `explicit-light.gml`, `fixtures/*.win` (Gamma+ 3.1.0, JAMM-perf builds), profile dirs with CSVs.
You can decompile archives with JAMM itself: `dotnet run --project src/Jamm.Cli -c Release -- diff <base> <mod> --show <asset>`
(base: `~/cgf-files/legacy/2023-08-16_manifest-9039707068725406600/data.win`).

## Deliverables
1. `docs/reports/gamma-plus-vs-perf.md`: per technique (frame pacing/room_speed/delta time, draw batching, surface/light handling,
   culling/activation, particle/effect limits, GC/array churn, texture swaps, etc.): what Gamma+ does, what Perf/Smooth do,
   expected effect (CPU vs GPU bound), risk, recommendation (adopt / don't / already covered). Mark every FPS claim that needs
   GPU measurement as "on user PC to verify" — this VPS has no GPU (llvmpipe), so never present VPS FPS as meaningful.
2. If there are clear low-risk wins (e.g. a cheaper light/surface path, avoiding per-step allocations, smarter culling that does not
   destroy lights), implement them in the perf/smooth builtins behind the existing structure, with xUnit tests on the generated GML.
   Bump the builtin revision if the code has one. Anything bigger: describe as a follow-up with exact code locations.
3. Optional: a relative CPU tick-time comparison (GML profiler / `loop_us` instrumentation via tools/Jamm.ModMaker if it supports it)
   — label results "relative, CPU only".

## Rules
AGENTS.md applies. Zero warnings; `dotnet test -c Release` green if you change code. Do NOT commit or push. Work only in this worktree;
read-only access to ~/jamm-handover and ~/cgf-files; scratch under /tmp. Finish with a short summary.
