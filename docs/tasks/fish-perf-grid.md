# Task: fish neighbour queries via a spatial grid (CPU, behaviour-identical)

Measured 2026-11-19 (VPS profiler, dock scene, see docs/reports/overnight-2026-11-19.md §5):
`Fish_Step_2` (fish End Step incl. state events) is ~40 % of the loop: vanilla ~1090 fish 56 ms/tick (51 µs/fish),
heavy pack (TGE, Antiquity, PWB) ~2006 fish 143 ms/tick (71 µs/fish). Per-fish cost grows with the fish count, which points
at the `with (Fish)` / `instance_nearest` / collision neighbour loops (`fish_avoid_fish`, flocking/schooling, `fish_lunge` →
`check_lunge`, predators looking for prey, …) being O(n²).

## Step 0: tooling onto the branch
Branch `exp/fish-perf` in `~/jamm-wt/fish-perf`, from main. Merge `exp/gml-profiler` (it contains `exp/jamm-gamma-bench`,
both ~60 commits behind main) and resolve conflicts cleanly so `jamm-modmaker bench … --profile [--profile-scripts]` works on
current main. Keep that as its own merge commit (the orchestrator may merge the tooling to main separately).

## Step 1: find every quadratic neighbour pattern
Decompile the fish code of vanilla and of the heavy/all-7 builds (TGE, Antiquity, PWB, Abyss, Colour, Verdant, Cave; real files in
`~/cgf-files/mods/pack-inputs`, `~/cgf-files/private-vel`; never copy TGE/PWB off the VPS). List each script/event that iterates
over other fish (`with (Fish)`/`with (<parent>)`, `instance_nearest`, `instance_place`/`collision_*` against fish, `distance_to_object`
on fish, ds-list loops), with its radius / early-exit / accumulation semantics and per-tick call count. Put the table in the report
with profiler shares (`--profile-scripts`) for vanilla, heavy and all-7.

## Step 2: grid
* A builtin runtime helper (part of the Perf builtin, own switch `jamm_perf.ini [Perf] FishGrid=1`, default ON when Perf is enabled,
  independent of the F8 mode) that rebuilds a uniform grid of fish once per tick before the first consumer runs (Begin Step of a
  controller with a guaranteed early order, or lazily on first query per tick with a tick counter).
* Queries must return **the same candidate set in the same order** as the original loop would visit them (GML `with` order =
  instance order). Store per-instance an order key captured at build time and merge cell lists by that key. Accumulations
  (sums of avoidance vectors, "first match wins", `break`) must therefore produce bit-identical results.
* Cell size ≥ the largest query radius actually used by the rewritten call site; a call site whose radius is unbounded or
  data-dependent beyond the cell size falls back to the original loop.
* Instances created or destroyed during the tick (spawns, catches, `FishThinner`, Perf scheduling, Endless Ocean streaming,
  new-fish API objects): either keep the grid consistent (insert/remove hooks) or detect `instance_number(Fish)` / a generation
  counter change and fall back to the original loop for the rest of that tick. Correctness beats speed.
* Rewrite only call sites where equivalence is provable; leave the rest and list them as skipped with the reason.
* Mods that replace the fish scripts: apply the rewrite by pattern on the merged code (after merging), and skip with a decision
  line `[CGF-PERF-GRID] <script>: skipped (<reason>)` when the pattern differs. Never break a build.

## Step 3: prove equivalence
* New bench option (e.g. `--trace-fish N`): with a fixed seed, write per tick for N ticks (≥ 1800) a hash of all fish states
  (id, x, y, hspeed/vspeed or speed/direction, state variables the scripts use, image_xscale) plus `random` call count if reachable.
  Run grid OFF vs ON on the same build and seed: the trace files must be identical for vanilla, heavy and all-7 (dock + deepwater).
  If an RNG/timing difference makes this impossible, find out why and document it; do not claim equivalence without identical traces.
* Synthetic tests (.NET + the existing GML test harness under tests/): grid queries vs brute-force loop on random fish layouts,
  including fish at cell borders, negative coordinates, huge coordinates (Endless Ocean x up to ~1.2 M), creation/destruction mid-tick.

## Step 4: measure
Profiler numbers before/after (`Fish_Step_2` ms/tick, per fish, whole loop) for vanilla, heavy and all-7, dock + deepwater, one game
at a time. Target: heavy and all-7 clearly less fish CPU per tick at identical traces. Also report the grid build cost per tick.

## Rules
AGENTS.md. Zero warnings, all tests green. In-game runs only via `~/jamm-night/bin/run.sh` (600 s budget for big packs), one game at a
time: other agents share the VPS, check `pgrep -f "Cat Goes Fishing.exe"` and wait. Commit on the branch, no push, no merge to main.
Report `docs/reports/fish-perf-grid.md` (English, tables with before/after numbers and the trace comparison).
