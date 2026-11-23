# GML cheap wins: source analysis

Date: 2026-11-23. Scope: task items 3, 4, 6, 7, 8 and 9 from `docs/tasks/gml-cheap-wins.md`.

## Evidence and method

The local legacy clean archive was opened read-only with the existing ModMaker dump tool. Runtime hotspots below use the supplied all-7 profiler capture `ds-all7-prof-mA-dock/jamm_profile_dock.csv` (104 profiled ticks, 141,355 µs mean loop). These profiler shares are relative CPU samples; Draw entries are render-affected and should not be summed as pure simulation time. Source searches covered the checked-in GML under `src/Jamm.Core`, the ModMaker scenes, and the perf/grid/sleep code. No controller or event was changed as part of this analysis.

The clean-archive decompiler snapshot is useful for vanilla events but does not include mod-added code. `CaveRock_Step_0` was not present in this particular clean archive. The task's profiler evidence and current perf reports cover that event in modded runs. Sprite-mask classification requires sprite/object metadata from each input archive; this first pass identifies relevant objects and the mask questions but does not claim every listed sprite uses a precise mask.

## Hotspots and source findings

| Code/event | Capture evidence | Source shape / likely cost | Cheap-win assessment |
|---|---:|---|---|
| `Fish_Step_2` | 37.77 ms/tick; 27%; 2,202 calls/tick | Dispatches state-specific Other events, performs night/state transitions, per-fish `collision_point` against `CaveRockBig`, computes lure distance, then calls `fish_check_trawler`. Work is per-fish and stateful. | Cache only a value whose inputs are unchanged during a tick and prove exact; no general event throttling. Fish LOD changes AI/collision timing and is not behavior-identical. |
| `CaveRock_Step_0` | Task capture: 3,836 calls/tick, ~10% | Many-instance per-frame event in cave-heavy worlds. Exact body depends on the modded archive. | Audit before rewriting; no cached controller aggregate may skip per-instance activation/collision/timer behavior. |
| `SceneItem_Step_2` | 2,880 calls/tick, ~8–9% | Scene-depth/parallax transform uses `Camera`, `home_x/home_y`, `depth`, and `para_scale`; profiler indicates a large bulk cost. | A prior measured report found transform caching cost more than recomputation. No candidate without new measurements. |
| `Weed_Step_0` | 4.59 ms/tick; 3%; 1,197 calls/tick | High-frequency scenery Step. | Inspect full event and remove only exact animation progression if its only effect is sprite animation. |
| `SonarPanel_Step_1` | 1.95 ms/tick; 1%; 1 call/tick | `with (Fish)` traverses all fish and assigns `s1`–`s5`, with a branch for advanced/cave sonar. | O(n) and semantically required for sonar blips. Avoid per-fish helper scans; do not slow sonar updates. |
| `Buoy_Step_0` / `BuoyAbstract_Step_0` | 1.03 / 1.01 ms/tick | Buoy updates a child light and inherited animation; abstract buoy updates a segmented net with loops and CatSwim proximity checks. | Buoy animation could become `image_speed` only if inherited/visibility timing is unchanged. Abstract buoy has simulation and proximity effects; not animation-only. |
| `fish_lunge`, `fish_avoid_wall` | task profile names these among fish work | These are active fish AI paths involving fish/lure/world state. | No cadence change, approximate collision, or result reuse absent trace parity. |

The clean `Fish_Step_2` decompilation contains a `collision_point` against `CaveRockBig` for fish at depth, not a broad neighborhood loop. The checked-in Perf patch already hoists repeated arithmetic in `fish_avoid_fish`; `fish-grid` is independently opt-in and has a dedicated equivalence report. `PerfMode` is a dormant-fish scheduler and `FishSleep`/`DecorSleep` already own their respective sleep policies; this task must not duplicate them.

## Quadratic and repeated-query search

The code search did not find `with (Fish)` in the clean Fish End Step body. It did find the expected O(n) sonar traversal and several one-time/scene-script queries (`instance_nearest` in the Silo and Maize scene code). Candidate quadratic work remains in fish-specific Other events and mod scripts and must be distinguished from one-shot code before changing it. Existing fish-grid analysis explicitly calls out `fish_avoid_fish`, schooling/flocking, lunge checks, and predator prey selection; those are not rewritten here.

In checked-in source, `Weather/population.gml` iterates `with (Fish)` to apply population/weather state; `WeatherScene.cs` also has test-scene fish traversals. The former is runtime controller work and must be measured in a weather-enabled merged build. Silo/Harbor/Endless Ocean controllers use instance queries and/or room streaming, but the source search did not reveal a single controller loop safe to memoize generically: instance creation/destruction and streamed-room changes invalidate simple counters. Do not cache `instance_number` unless its value is known not to change between the read and all consumers in that simulation tick.

## Collision-mask audit targets

Relevant families for precise-mask inspection are fish (`Fish` descendants and fish species), `Lure`/hook objects, and high-count scenery/decor (`CaveRock*`, `SceneItem`, `Weed`, `Dangler`, `Rock`, `Hookable`, `Buoy`). The fish depth test explicitly supplies precise collision (`collision_point(..., CaveRockBig, 1, 0)`). Other fish/lure catch and wall paths use collision helpers and may rely on precise masks. A rectangle or ellipse mask can therefore change catching, wall avoidance, net interaction, or placement even when silhouettes look similar. No mask should be coarsened by a default-on transformation until each consuming collision path is inventoried and seeded interaction parity is demonstrated. For purely decorative objects without collision consumers, mask replacement is a candidate, but unused precise masks may not be a runtime hotspot.

## Step-event animation candidates

An event is eligible for Step-to-`image_speed` conversion only when it writes no state except `image_index` (or an equivalent animation-only field), has no inherited event work, no event ordering dependency, and the sprite's playback speed and pause behavior match GameMaker's native animation semantics. From the profiler/source subset inspected, `Buoy_Step_0` selects a frame based on night and updates a child light, so it is not eligible as a whole. `BuoyAbstract_Step_0` runs rope physics. Sonar and fish Steps are gameplay-critical. The candidate set must be derived by scanning events on the target archive, not guessed from object names.

## Implementation guardrails

1. Keep optimizations behind independent Perf settings. A setting defaults on only after a controlled before/after shows a lower loop time and behavior-identical output; a conservative default-off is required when parity is not proven.
2. Reuse exact per-tick values only where all source inputs and update order are understood; controller cache invalidation must cover object creation and destruction.
3. Restrict particles to objects with no collision, gameplay timing, or ownership semantics.
4. Do not add awake-fish LOD under a behavior-identical claim. Mid/far cadence changes collision, RNG, targeting, and capture timing; `FishSleep` already covers dormant fish.
5. A stack-trace ring buffer is diagnostic only and should be a separate explicit debug option. A per-event marker must avoid allocations and should be dumped only on an explicit hang diagnostic path.

## Open analysis work

- Produce a machine-readable precise-mask census for the exact vanilla/pub5/all7 archives and follow each collision consumer before selecting any sprite.
- Enumerate every fish-neighbor query in the three requested builds and include dynamic-mod controller source, especially weather, silo, harbor and endless-ocean packages.
- Find Step events whose entire body is animation-only from each merged archive and benchmark an isolated transform.
- Establish a local behavior trace for any proposed transform before enabling its default.
