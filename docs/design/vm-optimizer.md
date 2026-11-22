# Fish step micro-optimisation and VM optimiser

## Decision and measurement plan

This document records the baseline review before implementation. The runner and archive tooling are shared with other work, so game measurements use the existing serial run scripts and their `game-run.lock`. No benchmark game process was started for this initial review because another shared-runner task was active. The starting data below is from the archived runs dated 2026-11-20; it is useful for prioritisation, but not a before/after claim for this branch.

### Baseline profiler readings

The saved `--profile-scripts` captures report inclusive microseconds per simulation tick. These are relative CPU measurements; Draw entries are render-affected. Runs used the benchmark harness's deterministic dock/deepwater scenes, but capture lengths differ and the profiler has measurable timer overhead.

| Build | Scene | Ticks | `Fish_Step_2` µs/tick (share) | `fish_lunge` µs/tick | `fish_avoid_fish` µs/tick | `fish_standard_step2` µs/tick | Loop µs/tick |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Vanilla | Dock | 85 | 159,046 (55%) | 34,277 | 12,129 | 345 | 287,843 |
| Heavy | Dock | 41 | 322,260 (53%) | 59,201 | 27,923 | 6,904 | 603,208 |
| All-7 | Dock | 21 | 666,853 (56%) | 95,589 | 58,876 | 56,228 | 1,192,600 |
| Vanilla | Deepwater | 99 | 143,319 (58%) | 31,720 | 11,674 | not in top rows | 247,225 |
| Heavy | Deepwater | 30 | 404,859 (48%) | 77,224 | 35,149 | not in top rows | 836,134 |
| All-7 | Deepwater | 20 | 687,421 (56%) | 99,849 | 60,252 | 57,702 | 1,238,337 |

Sources: `~/jamm-night/runs/fish-grid-baseline-{vanilla,heavy,all7}-{dock,deepwater}/jamm_profile_*.csv`. Fish counts/capture lengths vary substantially, so these values establish hot paths, not a controlled comparison. `Fish_Step_2` is the dominant entry at 48–58% of the measured loop. The script profiler points to `fish_lunge`, `fish_avoid_fish`, and, in modded profiles, `fish_standard_step2` as candidate call paths. The full fish step event dispatches state-specific events and scripts, so event time is not attributable to individual GML lines from this CSV alone.

The available vanilla decompile at `~/jamm-night/dumps/vanilla/gml_Object_Fish_Step_2.gml` has 104 lines. The top-level event contains `speed = spd`, a state switch that dispatches `Other` events, two `is_it_night()` calls on conditional paths, a deep-water collision query, lure-distance arithmetic, the catalog reveal check, `fish_check_trawler()`, and the final speed/time-scale update. `fish_lunge` computes `distance_to_lure`, then calls `fish_set_lunge_mod()` and `check_lunge()`, which recalculates the same distance. The staged mod decompiles showed no additional direct call, and `fish_set_lunge_mod()` does not change either position. However, the full real archive also contains `gml_Script_fish_default_follow`, which the runtime rewrite detects as another possible caller. The post-merge rewrite therefore requires `fish_lunge` to be the sole code entry mentioning `check_lunge`; when any other entry exists, only this rewrite is skipped. `fish_avoid_fish` already uses squared distance before direction/vector work and accumulates in `with` iteration order; changing that loop or candidate order is outside this micro-optimisation scope.

The source-level event review maps the measured work to these blocks:

| Vanilla lines | Work | Assessment |
| --- | --- | --- |
| 1–10 | Speed initialization, life reset, deactivation, state-independent Other 22 dispatch | `event_perform` is required for Fish variants; no duplicate pure query to remove. |
| 11–58 | State switch dispatches state-specific Other events or helper scripts | This is most of the aggregate event cost, but script profiling attributes the hot state paths separately (`fish_lunge`, avoid-fish, wall checks). Removing dispatch/context changes risks mod hooks. |
| 59–62 | Speed clamp | One read and possible write; no repeated lookup. |
| 63–82 | Night transitions, path end, random wake-up | The second night query is control-dependent on possible state changes in the first branch; caching across them is not safe. RNG call order must remain untouched. |
| 83–91 | Conditional deep-water collision-point query and reset | Query is guarded by depth and fish state; no safe duplicate query. Preserve order. |
| 92–96 | Squared distance from fish to `Lure`, then catalog reveal eligibility | Squared arithmetic already avoids `point_distance`; cache coordinates only after runtime parity confirms object lookup behavior. |
| 97–104 | State 11 speed stop, frame counter, trawler check, speed/time-scale update | Trawler check is separately profiled; move/skip logic only with exact state proof. Final Girl reads are single-use. |

The top-level CSV cannot time each source line. `--profile-scripts` gives inclusive per-entry timing for called scripts, and the source map above identifies their dispatch sites. A true line-level profiler would add instrumentation overhead and should only be pursued if the script/event profile leaves a concrete unresolved hotspot.

## Candidate work and acceptance gates

1. Capture a fresh vanilla baseline and fish-focused `--profile-scripts` capture serially through `~/jamm-night/bin/run.sh`, then inspect same-scene autoplay catch and sonar CSVs. Re-run after each isolated patch. Keep a patch only when the output CSVs match and repeat captures show a stable CPU improvement.
2. Prioritise local invariant caching only where an expression is demonstrably repeated in a hot function and object mutation cannot intervene. Prefer GML locals for repeated instance members within a single call. Do not cache `instance_nearest` across a mutation or frame; do not reorder `with` iterations or aggregate floating-point results differently. Verify candidate source patterns on vanilla and all locally available mod decompiles before applying a patch. Skip overridden or unmatched code with a diagnostic.
3. Evaluate a bytecode optimiser as an experimental, profile-controlled pass, disabled by default. Start with instruction counts and semantics-preserving constant folding where types/operations are statically certain. Do not auto-hoist instance-variable reads, rewrite dynamic `variable_instance_exists`/string lookups, remove `with` contexts, or pool instances until a GML/VM semantic proof and parity fixture exist. Each transformation needs bytecode instruction counts, compile/reload validation, autoplay catch/sonar parity, and paired tick timings on vanilla and modded builds. Plain Perf and vanilla output must remain byte-identical when the profile option is off.
4. Take separate memory and rendering measurements. Check the runner executable's PE architecture and Large-Address-Aware bit without editing the source executable; only test patched copies in disposable runner folders. Count texture-page switches per frame with instrumentation that records texture-page changes at draw boundaries, then compare the same scene/profile. Neither metric is currently emitted by `jamm-modmaker bench`.

### Initial platform checks

`~/jamm-night/games/clean/Cat Goes Fishing.exe` is PE32/i386, 4,480,512 bytes, and already has the `IMAGE_FILE_LARGE_ADDRESS_AWARE` flag (`objdump -p` reports “large address aware”). Therefore changing that flag is not a candidate benefit for this runner copy; first confirm the exact executable selected by `run.sh` before any memory conclusion. The game remains 32-bit, so LAA only changes the address-space ceiling where the OS/Wine configuration permits it; it does not increase physical memory or solve fragmentation. No executable was modified.

No texture-page switch counter was present in the existing benchmark CSV schema or the `tools/Jamm.ModMaker` instrumentation. There is therefore no measured switch count yet; add it only if the runtime exposes a reliable page-change signal, and document any proxy instead of labeling draw calls as switches.

## Experimental interface and rollout

The bytecode pass should be behind an explicit benchmark/profile setting such as `--vm-optimizer` and remain off for ordinary profile builds until the gates above pass. The default build path must preserve vanilla `data.win` bytes when no feature is enabled. The implementation should report transformations considered/applied/skipped and before/after instruction totals, without console output from `Jamm.Core`.

Keep the first implementation narrow: a measurement harness and the smallest locally provable micro-optimisations. Treat broad all-mod rewriting, instance pooling, `with` removal, and dynamic existence-lookup rewriting as research items unless controlled results establish both parity and a useful win. Never commit game archives, runner copies, save data, or extracted assets.

## First implementation result

JAMM Perf's experimental profile option runs after merge and stays off by default. In the synthetic GM1.4 fixture, `fish_flock` computes its repeated direction once inside the original `if (pal > 0)` body: the compiled script drops from **54 to 48 VM instructions**, and `point_direction` Call instructions go from **2 to 1**. The fixture also confirms the guarded `fish_lunge`/`check_lunge` rewrite compiles and reduces their combined instruction count. That rewrite passes the already-calculated lure distance only when `fish_lunge` is the sole visible caller. The real legacy profile build found a second caller, `gml_Script_fish_default_follow`, so it correctly skipped the distance rewrite and kept `check_lunge`'s zero-argument behavior. The real build applied only the `fish_flock` rewrite and passed archive verification.

The option is `PerfSettings.FishMicro`, exposed as `cgfmm profile perf <name> fish-micro on|off` and in **Performance settings**. It applies only to profile builds selecting the built-in Perf mod, after merged source has been prepared. The generated Perf patch itself retains both original `fish_flock` direction calls; a regression test checks that, so unrelated builds cannot inherit the experimental edit. A missing, changed, ambiguous, or colliding source pattern is skipped with a decision. The real legacy base and plain Perf profile output have identical SHA-256 hashes when the option is off (`dbb2e5c4…286d2c1`).

No automatic optimizer is enabled or shipped. Constant folding requires GM1.4 numeric/string coercion rules; variable hoisting can change dynamic instance lookup or read timing; string existence lookup and `with` removal can change object ancestry/order; and pooling changes creation/destruction lifecycle. Current captures do not establish a win for those transformations. The correct current decision is to keep the proposed all-mod optimizer unimplemented and off by default, and revisit it only with an optimizer mode that can be explicitly enabled for profile builds and a transformation-specific parity corpus.

Matched profile captures are queued through the shared runner to measure before/after tick timing. The autoplay probe now appends `lure_fish`, `hooked_fish_size`, `sonar_active`, and `advanced_sonar_active` columns to its CSV; the original columns used by its controller remain in the same order. Same-seed before/after captures can compare fish hooks and sonar state alongside the existing room, position/depth, and lure state. No texture-page switch counter is exposed by the benchmark. The shared lock is currently contended; no fish-micro game run has started yet. Keep runtime and gameplay-parity claims pending until those captures finish.
