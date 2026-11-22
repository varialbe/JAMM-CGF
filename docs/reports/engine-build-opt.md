# Engine and build optimisations: Phase 2 evidence

Branch: `exp/engine-build-opt`
Review order: `docs/design/engine-build-opt.md`, Orchestrator review (2026-11-22)

This report records implementation and validation evidence for the approved Phase 2 sequence. Game archives, captures, and screenshots are local under `~/jamm-night/engine-build-opt` and are not committed.

## 1. Final merged empty-event pruning

`EmptyEventOptimizer` runs after merge selection and before output serialization, including vanilla builds. It removes only verified empty/no-op event entries whose effective parent chain cannot expose behavior. Per-event removal/skip reasons are returned in the build report. The pass does not delete objects, room instances, or arbitrary unreachable code.

Synthetic tests cover empty handlers, executable parent overrides, inherited-only events, mod-added handlers, idempotence, and preserving source inputs. Release validation: **656 Core tests passed, 17 skipped; 130 GUI tests passed, 1 skipped**.

The Phase 1 event/profile census recorded `Rock_Step_2` at 55 / 232 / 472 calls per tick and 15 / 737 / 2,064 µs per tick in vanilla / public-five / all-seven respectively (inclusive profile timing). `Rockbed_Step_0` ran 1 / 9 / 15 times per tick and only called `action_inherited()` where the effective parent handler was empty. The exclusive profile run below will replace the inclusive cost figures when available.

No-repack real archive builds were verified for vanilla, public-five, all-seven, and the personal package. The three profile matrix builds report pruning `Rock_Step_2` and `Rockbed_Step_0`; the personal package build also completed and verified. Archive sizes and verifier counts are retained in local logs:

| Archive | Output bytes | Verification |
|---|---:|---|
| Vanilla | 44,569,734 | `vanilla-norepack-build.log` |
| Public-five | 119,387,990 | 2,235 code entries; `pub5-norepack-build.log` |
| All-seven | 217,402,518 | 2,610 code entries; `all7-norepack-build.log` |
| Personal package | 54,668,204 | `personal-build.log` |

Repeated unprofiled `loop_us` comparisons, screenshot parity, and runtime autoplay are tracked below and remain the merge gate for shipping each optimization.

For the all-seven timing control, I also built the same `all7` profile at parent commit `209e30f` (before the optimizer hook), then instrumented it with the current ModMaker using the same dock scene, warmup, duration, and screenshot flags as the optimized archive. Both builds enable the same default DecorSleep pass; the earlier raw `~/jamm-night/games/all7/data.win` lacks that overlay and is excluded from the matched performance comparison. The uninstrumented control is 217,407,126 bytes versus 217,402,518 for the optimized archive (4,608 bytes removed). The repeated matched pair runs are queued separately.

## 2. Exclusive profiler attribution

The ModMaker profiler now tracks nested object-event wrappers separately from script wrappers. Each object-event row reports inclusive and exclusive time; nested object-event elapsed time is subtracted from its parent, while scripts remain within their caller's event time. The summary reports summed exclusive event time and a nonnegative `loop_us` remainder labelled `unattributed_runner_us_per_tick`; that residual includes engine and uninstrumented work and is not an engine-only timer. Profiling overhead remains significant, so performance comparisons use separate unprofiled captures.

The all-seven exclusive-profile archive passed semantic verification (3,983 code entries; output 219,271,190 bytes). Real-run attribution values and event rows will be added after the queued profile run.

## 3. `Water1` tile prototype

Static all-seven inspection found one `Water1` placement in `NewWorld`, at `(13312, 18660)`, room size `140000 x 26000`, scale `(10, 1)`, depth `2`. It has no parent, events, persistence, solidity, or runtime transform state. Its `mysticwaterbegin` sprite is `1000 x 750`; source bounds and tile origin were retained. The only code reference is DecorSleep's static deactivation list (`asset_get_index("Water1")`).

A local throwaway prototype replaced that one room instance with one background tile using the same texture, scale, color, and depth, retaining the object asset and DecorSleep reference. It passed semantic verification (2,624 code entries; BGND count 3). This is a prototype only; it has not been added to the build pipeline. Center, sonar, deepwater, and camera-edge screenshot parity, performance, and autoplay results will determine whether the conversion is retained or dropped.

## 4. Depth-write guards

Not applicable. `SceneItem_Step_2` writes `scene_depth = depth + 18`, then updates parallax position; it does not assign GM `depth` each tick. No recurring real `depth` setter with measurable cost was found in the inspected candidate paths. No guard was added.

## Runtime gate results

Pending serial game runs via `~/jamm-night/bin/run.sh`; captures are queued behind existing shared-lock users. This section will be completed from the recorded CSVs and screenshots before the implementation commits are finalized.
