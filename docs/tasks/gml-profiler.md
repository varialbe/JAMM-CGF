# Task: per-event GML CPU profiler for benchmark builds (`jamm-modmaker bench --profile`)

## Why
Heavy mod packs run ~40 ms per tick, CPU-bound in mod GML, and our Perf/Smooth builtins don't help. We need to know WHICH code
entries burn the tick. This VPS has no GPU (llvmpipe): draw timings are meaningless here, but Step/Alarm/Other event timings are
pure GML CPU work and are valid as RELATIVE numbers.

## Deliverable
Extend `tools/Jamm.ModMaker` (`bench` command, see `BenchmarkMod`) with `--profile`:
* Instrument every code entry attached to object events (Create excluded by default; Step/Begin/End step, Alarm, Other/User, Draw*
  included but Draw tagged as "render-affected") and optionally scripts (`--profile-scripts`): at entry `var __jt = get_timer();`,
  at every exit path accumulate `global.__jamm_prof_us[k] += get_timer() - __jt; global.__jamm_prof_n[k] += 1;`
  (handle `exit`/`return` inside the code — easiest robust approach: rename the original code into a script/wrapper and call it from a
  tiny instrumented stub, or inject at the top and wrap `exit` statements; choose whatever compiles reliably with UTMT 0.9 GML compiler).
* During the capture window only (respect warmup), and at the end write `jamm_profile_<scene>.csv` next to the bench CSV
  (same save dir): `code_entry,calls,total_us,us_per_tick,share_of_loop` sorted by total_us, plus header lines with ticks and loop_us mean.
* Overhead note in the header (get_timer cost). Keep the existing bench behavior unchanged without `--profile`.
* Unit tests (xUnit, synthetic archives) proving: instrumented code compiles, `exit` paths are counted, non-profiled build identical to before.

## Manual check (VPS, read-only inputs)
`dotnet run --project tools/Jamm.ModMaker -c Release -- bench <in.win> /tmp/prof/data.win --scene dock --warmup 6 --duration 30 --profile`
Input: `~/jamm-night/builds/heavy-main.win` (local build, never copy it into the repo). Don't launch the game yourself — the orchestrator
runs the game. Just make sure the instrumented archive builds and passes `cgfmm verify`.

## Rules
AGENTS.md applies. Zero warnings; tests green. No commit/push. Work only in this worktree and /tmp. Summarize at the end.
