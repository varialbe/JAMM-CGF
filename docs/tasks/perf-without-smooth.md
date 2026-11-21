# Bug: JAMM Perf without Smooth makes big packs 2–3× slower (CGF-PERF-REGRESS-001)

Branch `fix/perf-without-smooth`. Read AGENTS.md, docs/benchmarking.md, the Perf builtin (PerfMod.cs, PerfMode.cs, FishGrid,
instance guards), the Smooth builtin and docs/reports/{phase6a,phase6c,fish-perf-grid,weak-pc-preset}.md.

## Measured on the user's PC (dock, 30 s, `~/jamm-bench/run.sh`), all-7 = TGE, Antiquity, PWB, Abyss, Colour, Verdant, Cave
| build | FPS | loop ms |
|---|---|---|
| all7 (no JAMM builtins) | 20 | 48 |
| all7 + Perf | 6–10 | 98–161 |
| all7 + Perf + Smooth | 18–20 | 48–54 |
| all7 + silo / weather / endless / smooth alone | 16–20 | 48–59 |
Perf `[Perf] Mode=0/1` × `FishGrid=0/1` via jamm_perf.ini: all four ≈ 100 ms (the CSV header sometimes reports a different
fish_grid/perf_mode than the ini written – check whether the ini is read from the right place / overridden by a persisted preference).
Profiler (relative, `--profile --profile-scripts`) in `.audit/prof-all7-perf.csv` vs `.audit/prof-all7-perf-smooth.csv` and
`.audit/prof-all7-pure.csv`. Perf without Smooth adds: `gml_Script_jamm_fg_touch` 32 ms/tick (10 %), `gml_Script_fish_disabled`
17 ms/tick, `SceneItem_Step_2` 46 vs 14 ms/tick, `LightingSystem_Draw_0` 21 ms/tick, Fish_Step_2 100 vs 59 ms/tick.
Builds: `~/jamm-bench/builds/all7+perf.win`, `all7+perf+smooth.win`, `all7.win`, `vanilla.win`, `pub5.win`, inputs in
`~/jamm-bench/inputs/` (private TGE/PWB in `inputs/private`, local only – never commit/upload).

## Required
1. Find the root cause: why Perf's code paths (grid touch, fish_disabled, SceneItem/Lighting changes) are so expensive on big packs
   and why Smooth's presence avoids it (composition order? Smooth wrapping/freezing the same events? Perf disabling itself when
   Smooth exists? FishGrid maintenance running even when FishGrid=0?). Explain in the report with evidence (profiler entries).
2. Fix so that Perf alone is never slower than no Perf on vanilla, pub5 and all7 (dock + deepwater), and Perf+Smooth stays as fast.
   Honour the ini switches; make the CSV header report the actually active values.
3. Check the Weak-PC preset (Perf on, HD-Lite off; does it enable Smooth?) and make sure it is a net win on all7 after the fix.
4. Bench before/after with `MM=<this branch jamm-modmaker.dll> ~/jamm-bench/run.sh <win> <label> dock|deepwater 30 [--profile]`,
   rebuilding test archives with this branch's cgfmm (`cgfmm modkit build site/cgf-mods/sources/jamm-perf --base <vanilla> --out ...`
   then `cgfmm build --base <vanilla> --order <perf>,<packs...> --out ...`; see `~/jamm-bench/build.sh`). Only ONE game at a time:
   wait while `pgrep -f "Cat Goes Fishing.exe"` finds a process or any `systemctl --user` unit `jamm-bench-*` is active.
5. Tests (regression test for the guard/grid path where possible), full suite green, zero warnings. Report docs/reports/perf-without-smooth.md
   with the before/after table. Commit on the branch; no merge, no push.

## Round 2 (2026-11-21 17:55, clean serialized re-bench of commit 754eb8a)

Loop µs, mean of 2 runs × dock/deepwater, 30 s, bench tool from this branch:

| build | loop µs |
|---|---|
| all7 | 62 900 |
| all7 + perf | 89 600  ← still +43 % (was +100 %) |
| all7 + perf + smooth | 63 200 |
| pub5 | 85 700 |
| pub5 + perf | 81 300 (ok) |
| vanilla | 20 960 |
| vanilla + perf | 22 390 (+7 %, not ok) |

Headers in all perf runs: `fish_grid=1`, `perf_mode=0` → the remaining cost is the FishGrid path
(and whatever else PerfMod injects) when Smooth is absent. With Smooth the cost vanishes — find out why
(Smooth culls Fish/SceneItem → grid work is skipped?) and make Perf-alone get the same benefit or turn the
expensive part off by default. Acceptance: for all7, pub5 and vanilla, `+perf` loop ≤ base loop (within 3 %).
Verify the INI switches actually reach the game (earlier runs suggested `jamm_perf.ini` is ignored).
Bench with `~/jamm-bench/run.sh` only (it now holds a global lock; other agents share the machine).
Build tools: copy bin output to ~/jamm-bench/tools/cli-fix and mm-fix (path has a space). Disk is limited: delete your own builds in ~/jamm-bench/builds when done.
Write your final report into docs/tasks/perf-without-smooth-report.md and commit.
