# Task: bench experiments to isolate the real bottleneck (fish AI off, fish count scaling, CPU vs GPU)

Branch `exp/bench-fish-experiments`. Read AGENTS.md, docs/benchmarking.md and `tools/Jamm.ModMaker/BenchmarkMod.cs`,
`StressScene.cs`, `PerfMod` (Perf builtin) first. Test-only tooling: nothing here goes into player-facing builtins.

## Why
Heavy packs reach ~9 FPS on the user's PC (Ryzen 7 5700X, RTX 2070 Super). VPS profiling: Fish End Step ≈ 50 % of the
loop in all-7; Weak-PC preset saved 22 % of Fish End Step but only 4.1 % of the tick. We need hard answers before more
micro-patches: (1) how much FPS does fish simulation really cost, (2) does cost scale linearly or super-linearly with
fish count, (3) where does the other ~50 % go (other events, engine, draw, present/GPU).

## Required: new `jamm-modmaker bench` options (dock/sonar/deepwater scenes), also as `[Bench]` ini keys
1. `--fish-mode full|nostep|nodraw|deactivate` (ini `FishMode`), applied after warmup, before capture, to every
   instance of the fish parent hierarchy (`Fish` and all children; detect parents generically, also modded fish):
   * `full` – unchanged (default).
   * `nostep` – fish keep drawing but skip their Begin/Step/End Step logic (early-exit guard injected at the top of each
     fish step event incl. inherited parents; a global flag so it can be toggled at runtime).
   * `nodraw` – fish simulate normally but skip drawing (`visible = 0` is NOT enough if Draw events exist: guard Draw too).
   * `deactivate` – `instance_deactivate_object` for fish (measures the engine cost of the instances existing at all).
   Guard every reference other objects make to fish so nostep/deactivate never crash (use the existing instance guards).
2. `--fish-cap <n>` (ini `FishCap`): after warmup destroy (deterministically, by instance order, keeping the hooked fish
   and any fish within the view) fish until at most n remain; also stop/disable respawn (FishSpawner/FishThinner
   equivalents in vanilla and in modded packs) during capture so the count stays at n. Record the real count per sample.
3. `--fish-scale <k>` (ini `FishScale`, e.g. 2): optionally duplicate ordinary fish k times (instance_copy semantics or
   re-run the spawner) to reach 2000/4000 fish on vanilla for scaling curves.
4. CSV: add per-sample `fish_count`, `fish_step_us` (sum of fish step events, cheap get_timer() bracket around the
   fish events via begin/end hooks), `draw_us` (Draw..DrawGUIEnd) and `step_us` (BeginStep..EndStep) columns so
   CPU-logic vs CPU-draw vs present/GPU (`present_sleep_us`) are separated per frame. Summary lines for each.
5. A local matrix script `tools/bench/fish-experiments.sh <build.win>...` that, given `~/jamm-bench/run.sh`
   (exists, arguments: `<build.win> <label> <scene> <seconds> [bench flags...]`), runs per build: full, nostep, nodraw,
   deactivate, fish-cap 2000/1000/500/200, and writes a Markdown table (avg FPS, p95/p99 frame, loop/step/draw/present means,
   fish count) to `~/jamm-bench/results/fish-experiments-<date>.md`.
6. Tests (option parsing, injection compiles on vanilla legacy and on a modded archive), full suite green, zero warnings.
   Docs: extend docs/benchmarking.md. Report: docs/reports/bench-fish-experiments.md.

## Inputs on this PC
Builds: `~/jamm-bench/builds/{vanilla,builtins,own7,pub5,pub5-builtins,own7-pub5}.win` (no TGE/PWB on this PC).
Do NOT touch `~/.var/app/com.valvesoftware.Steam/data/CGF-Personal` or compatdata `cgf-personal`/`343780`.
Do not start the game yourself while `systemctl --user is-active jamm-bench-m1` is active (the orchestrator runs a matrix);
build + unit test only; the orchestrator runs the in-game matrix. Commit on the branch; no merge, no push.
