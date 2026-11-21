# Benchmark Fish Isolation and Scaling Experiments

Implemented on branch `exp/bench-fish-experiments` in test-only tooling (`tools/Jamm.ModMaker` and `tools/bench`). Nothing in player-facing builtins or `cgfmm` was altered.

## Summary of Changes

1. **Fish simulation/render isolation (`--fish-mode`, INI `FishMode`)**
   - Added `BenchmarkFishMode` (`full`, `nostep`, `nodraw`, `deactivate`) to `BenchmarkMod` (`tools/Jamm.ModMaker/BenchmarkMod.cs`), `BenchmarkFishExperiments` (`tools/Jamm.ModMaker/BenchmarkFishExperiments.cs`), and `Program.cs`.
   - `Fish` hierarchy discovery walks `ParentId` from `Fish` generically so both vanilla and modded fish species are covered.
   - `nostep` prepends `if (global.jamm_b_fish_nostep) exit;` bytecode guards at the entry of every `EventType.Step` (`BeginStep`, `Step`, `EndStep`) code entry across the `Fish` hierarchy (including inherited parents) while keeping drawing intact.
   - `nodraw` prepends `if (global.jamm_b_fish_nodraw) exit;` bytecode guards at the entry of every `EventType.Draw` code entry across the `Fish` hierarchy (because `Fish` step events reset `visible = true`).
   - `deactivate` clears any active `Lure.fish` / `CatSwim.holding` reference, freezes spawners, and runs `instance_deactivate_object(Fish)` after warmup (re-enforcing in `EndStep` during capture).
   - `BenchmarkFishExperiments.GuardFishReferences` runs `InstanceGuard.Apply(data, data, ...)` on `Fish` references and guards `FishSmallSwarm_Other_10`'s array-indexed `instance_nearest` checks so `nostep`, `deactivate`, and `fish-cap` do not crash on missing fish instances.

2. **Deterministic fish capping & spawner freeze (`--fish-cap`, INI `FishCap`)**
   - `jamm_b_apply_fish_cap(cap)` runs after warmup (and enforces the ceiling in `EndStep` during capture), destroying excess active `Fish` instances in deterministic `with (Fish)` instance order while preserving hooked fish (`Lure.fish`, `CatSwim.holding`) and fish inside active views first, and destroying owned `light`/`light2` instances before `instance_destroy()`.
   - Sets `global.jamm_b_freeze_spawners = 1` during capture and guards `gml_Script_fish_respawn`, `FishThinner` (`Step_0` and `Other_10`), `*Spawner*` / `*Thinner*` / `Swarm` events, and modded Weather Pack population scripts (`jamm_weather_population`, `jamm_weather_gate`, `jamm_weather_restore_fish`).

3. **Fish scaling (`--fish-scale`, INI `FishScale`)**
   - Duplicates ordinary `FishSmall`/`FishMedium` instances (excluding multi-part/boss/light-owning fish) before warmup using `instance_copy(false)` with slight deterministic coordinate jitter, clearing shared `light`/`light2` handles and re-initializing paths (`path_init()`).
   - Sets `global.jamm_b_no_thin = 1` when `FishScale > 1` so `FishThinner_Other_10` does not cull scaled fish during warmup.

4. **Per-phase CSV breakdown and fish step timing**
   - Added `PreDraw` (`Draw_76`) to `obj_jamm_bench` to split `loop_us` into `step_us` (`BeginStep..PreDraw`) and `draw_us` (`PreDraw..DrawGUIEnd`).
   - Bracketed all `Fish` hierarchy `EventType.Step` entries with `jamm_b_fish_step_enter()` / `jamm_b_fish_step_leave()` via `LegacyCodeHooks.Insert` using a nesting-depth counter (`global.jamm_b_fish_depth`) so inherited `action_inherited()` calls are not double-counted.
   - Extended CSV columns to `frame_index,frame_us,loop_us,present_sleep_us,step_us,draw_us,fish_step_us,fish_count` and added `#` summary lines (`samples`, `min`, `p50`, `p95`, `p99`, `max`, `mean`) for `present_sleep_us`, `step_us`, `draw_us`, `fish_step_us`, and `fish_count` via `jamm_b_stats_line`.

5. **Matrix script and documentation**
   - Added `tools/bench/fish-experiments.sh <build.win>...` to invoke `~/jamm-bench/run.sh` across `full`, `nostep`, `nodraw`, `deactivate`, and `fish-cap 2000/1000/500/200` per build and emit a Markdown table to `~/jamm-bench/results/fish-experiments-<date>.md` (supports `--report-only` for re-summarizing existing runs).
   - Updated [docs/benchmarking.md](../benchmarking.md).

## Verification

- `dotnet build -c Release`: succeeded with **0 warnings** and **0 errors**.
- `dotnet test -c Release`:
  - `Jamm.Core.Tests`: 585 passed, 0 failed, 17 skipped (opt-in real-file tests).
  - `Jamm.Gui.Tests`: 130 passed, 0 failed, 1 skipped (opt-in real-file test).
- Opt-in real-archive test (`FishExperimentsCompileOnRealLegacyAndModdedArchives` with `JAMM_ORIGINAL=~/jamm-bench/builds/vanilla.win` and `JAMM_MODS_DIR=~/jamm-bench/inputs`): passed.
- Verified `jamm-modmaker bench` instrumentation on all six local builds in `~/jamm-bench/builds/` (`vanilla`, `builtins`, `own7`, `pub5`, `pub5-builtins`, `own7-pub5`) plus `--profile --fish-mode nostep` on `vanilla.win` (outputs written to `/tmp/` and removed after verification; game was not launched).
- Checked `tools/bench/fish-experiments.sh` syntax (`bash -n`) and Markdown table generation (`--report-only` against a synthetic CSV fixture in `/tmp/`).

In-game matrix execution (`tools/bench/fish-experiments.sh`) is left to the orchestrator when `jamm-bench-m1` is idle.
