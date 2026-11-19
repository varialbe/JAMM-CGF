# Gamma+ 3.1.0 vs JAMM Perf/Smooth

## Scope and evidence

This comparison uses the read-only material in `~/jamm-handover/2026-11-19/gamma/`, the
Gamma+ 3.1.0 and JAMM focused GML extracts, and the existing generators in
`src/Jamm.Core/Builtins/PerfMod.cs`, `PerfMode.cs`, and `SmoothMod.cs`. No game archive,
decompiled game code, or extracted game asset was copied into the repository.

The supplied user-PC frame-time sample is:

| Profile | Dock median / p95 / p99 (ms) | Sonar median / p95 / p99 (ms) | Deep sea median / p95 / p99 (ms) |
|---|---:|---:|---:|
| Vanilla replacement build | 18.04 / 20.90 / 23.16 | 19.09 / 21.93 / 23.32 | 18.09 / 21.41 / 24.77 |
| JAMM Perf | 17.25 / 31.85 / 54.49 | 19.66 / 23.22 / 24.61 | 20.07 / 32.15 / 40.14 |
| Gamma+ 3.1.0 | 35.03 / 36.41 / 36.50 | 35.65 / 36.40 / 36.45 | 35.84 / 36.40 / 36.49 |

These are user-PC frame-time measurements, not CPU tick timings. Gamma+ is paced around
30 FPS and has notably even frame times in this sample; JAMM Perf has lower medians in
some scenes but higher tails. A frame-time/FPS comparison alone cannot say whether a
change reduced game logic work or changed pacing. Any FPS or rendered-frame-rate claim
below that depends on GPU/render throughput is **on user PC to verify**. This VPS uses
llvmpipe and its FPS is not meaningful evidence.

The heavy three-mod pack is reported as CPU-bound in mod GML at about 40 ms per tick;
Perf/Smooth did not measurably improve that pack. This is a supplied relative context,
not a new benchmark performed for this report.

## Technique comparison

| Technique | Gamma+ 3.1.0 | JAMM Perf / Smooth | Expected effect and risk | Recommendation |
|---|---|---|---|---|
| Frame pacing, `room_speed`, and delta time | The inspected Gamma+ GML does not show a replacement logic scheduler or a `room_speed`/`delta_time` rewrite. The stable ~35 ms samples are consistent with a 30 FPS cap/pacing, but the focused GML does not establish how that cap is configured. | Perf leaves the game scheduler alone. Smooth keeps logic on the game’s original tick schedule, schedules from elapsed time, feeds accumulated tick delta to the three Girl step uses, and draws extra frames between ticks. It defaults to 120 and F7 cycles 60/120/off. | Pacing can improve consistency without reducing CPU work. Smooth’s extra frames can help perceived motion when the tick fits in the frame budget; when ticks overrun, it bypasses extra rendering. Rendering gains depend on GPU and are **on user PC to verify**. Altering `room_speed` or `delta_time` risks changing movement, timers, day/night, and fish behavior. | **Already covered** in Smooth. Do not copy a 30 FPS cap into Perf; measure tick time separately from frame time. Keep the frame cap/scheduler as an explicit future Smooth policy only if user-PC measurements and gameplay checks support it. |
| Fish scheduling, visibility, and activation | Gamma+ leaves the inspected Fish Step scheduling largely intact. Its Fish Step retains normal per-step logic and the stock off-view state transitions. | Perf mode adds an opt-in, configurable interval for structurally standard, distant, paused fish at the top of Fish End Step. It caches runtime ancestry, excludes special/custom behavior, keeps instances active, and preserves instance iteration. Smooth freezes/wakes instances for render-only frames, interpolates selected state, and cautiously culls draw work. | These are CPU opportunities when many eligible fish exist. Early exit can make unseen fish timers/state less current; activation can break other instances that read frozen objects. In the reported mixed-mod pack, this did not yield a measurable win. | **Already covered**, with existing safety limits. Preserve Perf’s fish exceptions and do not broaden the eligibility set without profiling merged mods and checking fish population/respawn behavior. |
| `FishThinner`, FPS thresholds, and population | Gamma+ changes `FishThinner_Step_0`’s `cull_trigger` increments: `+5` below 30 FPS, `+1` from 30 to below 45, and `+100` from 45 to below 60; it resets at 60+. At a 30 FPS cap, the `+1` branch reaches the `>120` threshold about every 121 frames (about 4 seconds), invoking the existing cull event repeatedly. Below 30 FPS the threshold is reached about every 25 frames. The supplied game analysis says that cull removes 10% of fish. | Perf does not alter FishThinner or change the game’s population policy. Its skipped fish remain active and visible to FishThinner’s `with (Fish)` traversal. | This is CPU relief only insofar as fewer fish subsequently run/render, but it is achieved by deleting population and can affect catalog/live counts, fish encounters, and gameplay. At Gamma+’s nominal 30 FPS, thinning continues and can systematically reduce fish population; it is not a free pacing improvement. A lower fish count may also change GPU/render load, which is **on user PC to verify**. | **Do not adopt.** Preserve stock thinning semantics in Perf. Any alternative needs population/live-count, catalog, save, and recovery checks. If investigating, instrument `FishThinner_Step_0` and the User 0 cull event it dispatches in a separate opt-in experiment. |
| Draw batching and per-fish draw overrides | Gamma+ has custom `SonarPanel_Draw_0` rendering and specialized draw code for Bonefish, Swordfish, Sodafish, Maskfish, and Fish. It uses direct sprite/primitive calls; the inspected changes do not show a general sprite batching or vertex-buffer system. | Smooth culls some default-drawn objects and pure fish draw events offscreen, while tracking fish draw side effects and rendering full instance sets where other draw code may read them. Perf has no general draw batching path. | Fewer draw calls may help CPU submission overhead; reducing pixels/overdraw may help GPU. Draw ordering, blend modes, inherited draw effects, and shared instance reads make broad replacements risky. GPU effects are **on user PC to verify**. | **Do not adopt wholesale.** Existing Smooth culling is already selective. Profile `SonarPanel_Draw_0` and specific fish Draw events on user hardware before changing them; exact follow-up locations are `SmoothMod.PreDraw`, `PureFish`, and the relevant object Draw assets. |
| Surface and light handling | Gamma+’s focused object set does not show a general surface resize/reuse strategy. Its supplied explicit light implementation recreates missing light surfaces and draws lights through subtractive blending; light objects set/reset blend mode. | Perf already guards Magmer/Volteen light writes against destroyed light instances. Smooth preserves the game’s normal draw work for unsafe/deactivated fish rather than forcing a light recreation or cull. | Surface allocation and blend passes can be GPU/driver-bound; avoiding repeated allocation can reduce CPU/driver overhead. A cheaper light path could change lighting or destroy/recreate semantics. GPU gains are **on user PC to verify**. | **Already covered** for stale light references. **Follow up** with runtime surface lifetime and light pass profiling before changing rendering. Exact locations: `PerfMod.Apply` light guards and Gamma’s `LightingSystem` Draw/Create events from `explicit-light.gml`; no safe general replacement is evident from the supplied material. |
| Particle and effect limits | No broad particle/effect budget change appears in the inspected Gamma+ focused code. | Perf/Smooth do not impose a global effect budget; Smooth suppresses only selected offscreen draw work. | A cap can reduce CPU and GPU work, but drops visible feedback or changes effects. GPU impact is **on user PC to verify**. | **Do not adopt** without a measured particle/effect hotspot. If one is found, patch that emitter’s Create/Step event with a reversible setting rather than a global cap. |
| Allocation, arrays, and garbage collection | The inspected Gamma+ excerpts contain ordinary local variables, arrays in sonar drawing, and no general allocation/GC policy. They do not establish a reusable buffer or reduced-allocation implementation. | Smooth allocates persistent ds lists/maps once in controller Create and clears them on room start; it uses arrays for view interpolation. Perf caches object ancestry in a ds_map and hoists invariant arithmetic in `fish_avoid_fish`. | Reuse can reduce CPU allocator/GC churn if hot code currently allocates. Persistent collections add lifecycle/leak risk; moving allocations can affect compatibility. | **Already partly covered**. Avoid adding per-step collections. **Follow up** only if profiler attribution points to allocation-heavy GML; inspect Smooth collection lifecycle and the specific hot script before editing. |
| Texture swaps / texture-page locality | No explicit texture-page or texture-swap optimization is present in the inspected Gamma+ GML. | JAMM does not reorder draws or move assets between pages. | Fewer texture binds may help CPU submission and GPU throughput, but asset relocation can affect packaging and draw order. FPS effects are **on user PC to verify**. | **Do not adopt** without renderer profiling and a safe asset/page pipeline. No narrow GML change is established here. |
| Collision and repeated CPU work | The inspected Gamma+ Fish Step does not guard the deep CaveRockBig collision query before calling it; the state predicate follows the collision call. | Perf adds a state guard before that query, skips impossible trawler collision checks, hoists invariant terms in `fish_avoid_fish`, memoizes the pure SceneItem depth transform, and offers opt-in distant-fish scheduling. | These can reduce CPU work without a rendering dependency. The state guard and arithmetic hoists preserve the relevant behavior; fish scheduling is deliberately opt-in because it changes update cadence. | **Already covered** by Perf. Keep narrow, guarded patches; profile user mod packs before adding further Fish Step transformations. |

## Decision and follow-ups

No new code change was made. The inspected Gamma+ changes overlap with optimizations
already present in Perf, while its distinctive `FishThinner` behavior is destructive
population control rather than a behavior-preserving optimization. The drawing changes
are asset-specific and need CPU/GPU profiling before they can be evaluated as JAMM
improvements. There is no clear low-risk GML win to copy based on the available evidence.

Recommended follow-ups:

1. Add relative CPU-only per-tick instrumentation around `FishThinner_Step_0` and the
   Fish Step/draw hotspots, comparing the same scene and fish population. Label the
   result **relative, CPU only**; do not use it as an FPS claim.
2. For FishThinner experiments, record live counts and the number of cull events along
   with tick time. Keep the original behavior as the default and test catalog, save,
   respawn, and population recovery.
3. Profile Gamma+’s sonar and lighting draw paths on a real GPU. Any resulting FPS claim
   is **on user PC to verify**. Avoid using the llvmpipe VPS for render-performance
   conclusions.
4. Keep the heavy three-mod pack as a separate CPU workload: supplied measurements say
   its roughly 40 ms/tick mod GML cost masks Perf/Smooth benefits. Optimize the profiled
   mod GML call sites before expecting a pacing or rendering option to fix its tick.
