# Investigate: why does one all7 archive run 33 FPS and the normal path 21.5?

Facts (dock, all7, 60 s, same run.sh, same MM, flags `--fish-sleep --decor-sleep`, seed 1337):
- A = ~/jamm-bench/builds/cheap/all7-base.win (made on branch exp/gml-cheap-wins @ older main via
  `jamm-modmaker perf ~/jamm-bench/builds/pd/all7.win out.win --only-cheap-wins`, i.e. PerfMod.Apply with
  applyScheduler:false, applyCoreOptimizations:false, decor+fish sleep baked in). Bench log then says
  "[CGF-PERF-DECOR] runtime already installed / [CGF-PERF-SLEEP] skipped (runtime already installed)".
  → 33.0 FPS, loop 28.1 ms, step 25.5 ms, fish_step 9.6 ms, draw 2.65 ms, decor tracked 8928, deact 3156.
- B = ~/jamm-bench/builds/pd/all7.win with the bench applying the sleeps → 21.5 FPS, loop 44.3 ms, step 40.9 ms,
  fish_step 12.4 ms, draw 3.4 ms, tracked 4032, deact 3156.
- So ~12.5 ms per step is outside fish steps. Round 2 of fix/lights-sleep restored tracking to 8809 but got
  SLOWER (19.3 FPS) → tracking count is not the explanation.
- `--profile` runs equalize at ~95 ms (instrumentation overhead dominates) — not useful as-is; data in
  ~/.var/app/com.valvesoftware.Steam/data/JAMM-CGF/bench-runs/prof-{cheapbase,main}-dock/jamm_profile_dock.csv.
  Notable: CaveRock_Step_0 ~18–20 ms/tick under profiling in both; main shows AbstractLight_Draw 4.2 ms (lights not
  sleeping; partially addressed by 10928e9 now on main).

Task: find the exact cause of the gap (bisect the differences between A and B: what does `perf --only-cheap-wins`
bake that the bench path does not, or vice versa — e.g. different FishSleep/DecorSleep code versions, Rock/CaveRock
guards, sweep cadence, instance_deactivate behaviour, bench-only instrumentation in B, room order, fish_mode). Use
bytecode diffs (cgfmm diff, or UTMT decompile of both instrumented archives — instrumented copies are deleted by run.sh;
regenerate with `dotnet <MM> bench <in> <out> --scene dock --warmup 8 --duration 60 --fish-sleep --decor-sleep`).
Then confirm by building B' = B + the one change and measuring (60 s, 3 interleaved reps vs B).
If the cause is something players also lack (i.e. real player builds via `cgfmm build` are at 21 not 33), implement
the fix on this branch and measure a real player build of the all7 mods too. If it is a bench artifact, say so clearly.
Report docs/reports/all7-gap.md. Commit on inv/all7-gap; do not push.
Rules: never touch CGF-Personal/compatdata cgf-personal/343780; no xdotool; run.sh only; labels `gap-`.
