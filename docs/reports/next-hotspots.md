# Next all7 hotspots

## Profiling method

The old per-invocation profiler timed every event call and pushed the loop toward 95 ms. The replacement samples every 16th capture frame and instruments only selected code entries in each run. Three grouped captures cover the dock top-25 candidates; five cover the deepwater candidates. Each entry timer starts immediately before its original body and stops immediately after it, including `exit` paths. Values are scaled by the 16-frame sampling interval and divided by captured loop ticks.

Grouped profile loop means were 30.38 ms for dock and 30.94 ms for deepwater. The unprofiled 3-run means were 29.51 ms and 30.23 ms, so the profile runs added 2.9% and 2.4% respectively. The table values below are sampled inclusive event-body times. Their sum is close to the loop budget; the residual includes engine work and GML outside the top 25. No alarm or collision entry appeared in either top 25; those event costs are part of the residual. `Other_n` labels are GameMaker user events.

## All7 dock top 25

| Rank | Event entry | Kind | µs/tick |
|---:|---|---|---:|
| 1 | `gml_Object_Fish_Step_2` | End Step | 11,761 |
| 2 | `gml_Object_FishLarge_Step_0` | Begin Step | 4,146 |
| 3 | `gml_Object_Fish_Other_23` | Other / user event | 1,718 |
| 4 | `gml_Object_Fish_Draw_0` | Draw | 1,292 |
| 5 | `gml_Object_CaveRock_Step_0` | Begin Step | 1,248 |
| 6 | `gml_Object_SonarPanel_Step_1` | Step | 1,235 |
| 7 | `gml_Object_Buoy_Step_0` | Begin Step | 1,026 |
| 8 | `gml_Object_BuoyAbstract_Step_0` | Begin Step | 1,013 |
| 9 | `gml_Object_Cuddlefish_Other_10` | Other / user event | 792 |
| 10 | `gml_Object_FishHuge_Step_0` | Begin Step | 444 |
| 11 | `gml_Object_Nightfish_Other_18` | Other / user event | 363 |
| 12 | `gml_Object_Chaw_Step_0` | Begin Step | 270 |
| 13 | `gml_Object_Lure_Step_0` | Begin Step | 227 |
| 14 | `gml_Object_Fish_Other_22` | Other / user event | 220 |
| 15 | `gml_Object_FishSmallSwarm_Other_10` | Other / user event | 196 |
| 16 | `gml_Object_FishMedium_Other_23` | Other / user event | 183 |
| 17 | `gml_Object_Fish_Other_10` | Other / user event | 174 |
| 18 | `SquiddleTentacleendStep` | End Step | 139 |
| 19 | `Minebombchainstep` | End Step | 123 |
| 20 | `gml_Object_Sea_Step_0` | Begin Step | 89 |
| 21 | `gml_Object_Skrimp_Step_0` | Begin Step | 89 |
| 22 | `gml_Object_SceneItem_Step_2` | End Step | 73 |
| 23 | `gml_Object_Rock_Step_2` | End Step | 55 |
| 24 | `gml_Object_AbstractLight_Draw_0` | Draw | 44 |
| 25 | `MagnapinnaTentaclesAllEndstep` | End Step | 42 |

Top-25 sum: 26.96 ms/tick. The matching unprofiled loop mean was 29.51 ms/tick, leaving an estimated 2.54 ms/tick for engine work and the unprofiled tail.

## All7 deepwater top 25

| Rank | Event entry | Kind | µs/tick |
|---:|---|---|---:|
| 1 | `gml_Object_Fish_Step_2` | End Step | 13,230 |
| 2 | `gml_Object_FishLarge_Step_0` | Begin Step | 4,280 |
| 3 | `gml_Object_Fish_Other_23` | Other / user event | 2,041 |
| 4 | `gml_Object_SonarPanel_Step_1` | Step | 1,272 |
| 5 | `gml_Object_CaveRock_Step_0` | Begin Step | 1,242 |
| 6 | `gml_Object_Buoy_Step_0` | Begin Step | 1,025 |
| 7 | `gml_Object_BuoyAbstract_Step_0` | Begin Step | 1,008 |
| 8 | `gml_Object_FishMedium_Other_23` | Other / user event | 567 |
| 9 | `gml_Object_FishHuge_Step_0` | Begin Step | 446 |
| 10 | `gml_Object_Fish_Draw_0` | Draw | 392 |
| 11 | `gml_Object_Gnawer_Other_10` | Other / user event | 391 |
| 12 | `gml_Object_Chaw_Step_0` | Begin Step | 316 |
| 13 | `gml_Object_Nightfish_Other_18` | Other / user event | 263 |
| 14 | `gml_Object_SceneItem_Step_2` | End Step | 227 |
| 15 | `gml_Object_Lure_Step_0` | Begin Step | 204 |
| 16 | `gml_Object_Fish_Other_22` | Other / user event | 174 |
| 17 | `gml_Object_Gear_Step_2` | End Step | 140 |
| 18 | `Minebombchainstep` | End Step | 139 |
| 19 | `SquiddleTentacleendStep` | End Step | 137 |
| 20 | `gml_Object_Skrimp_Step_0` | Begin Step | 130 |
| 21 | `gml_Object_LightingSystem_Draw_0` | Draw | 127 |
| 22 | `gml_Object_Ray_Other_10` | Other / user event | 115 |
| 23 | `gml_Object_Anglerfish_Step_2` | End Step | 99 |
| 24 | `gml_Object_Fish_Other_10` | Other / user event | 95 |
| 25 | `gml_Object_Josie_Step_2` | End Step | 95 |

Top-25 sum: 28.16 ms/tick. The matching unprofiled loop mean was 30.23 ms/tick, leaving an estimated 2.08 ms/tick for engine work and the unprofiled tail.

## Changes selected from the hotspots

| Candidate | Change | Default | Measurement |
|---|---|---|---|
| `Fish_Other_23` and hot reads in `Fish_Step_2` / `SonarPanel_Step_1` | Cache event-local values and inline the cooldown-only `fish_lunge` path. This preserves the per-tick cooldown decrement and avoids changing update order. | On (`CacheHot`) | No ≥3% pack/context win. Keep as a behavior-preserving cleanup, not a counted speedup. |
| `CaveRock_Step_0` | Compare squared distance to the 5,000 px cutoff; call the original distance function within 0.1 squared units of the boundary. | On with core Perf | No ≥3% win. Pub5 has no matching CaveRock event. |
| `Fish_Step_2` | Skip far-fish state AI on alternate End Steps while keeping engine motion per frame and catching alarms up on AI frames. Custom state counters/interactions can differ. | Off (`FishLod`) | Consistent ≥3% win in all four pack/context pairs; behavior-changing, so leave default-off. |

## A/B results

The initial matrix used three interleaved 60-second captures for each baseline and isolated treatment in every pack/context. After fixing CacheHot's Camera snapshot scope, that candidate was captured three more times in each context; those post-fix means replace the CacheHot values below and are paired with the original recorded baseline means. Positive percentages mean lower loop time than baseline. A candidate counts only when the win is at least 3% and consistent.

| Pack / scene | Baseline µs | CaveRock µs (change) | CacheHot µs (change) | FishLod µs (change) |
|---|---:|---:|---:|---:|
| all7 dock | 29,506 | 29,558 (−0.18%) | 29,563 (−0.19%) | 28,178 (+4.50%) |
| all7 deepwater | 30,232 | 30,607 (−1.24%) | 30,224 (+0.03%) | 27,986 (+7.43%) |
| pub5 dock | 32,383 | 32,285 (+0.30%) | 33,077 (−2.14%) | 28,605 (+11.67%) |
| pub5 deepwater | 34,018 | 34,088 (−0.21%) | 34,666 (−1.91%) | 29,819 (+12.34%) |

The CaveRock pub5 archive has no target event, so its unchanged result is a no-op. Only FishLod met the 3% consistency rule. The two behavior-preserving edits remain enabled as requested; their measured effect is neutral or within noise, and should not be presented as speedups.

## Crash and visual checks

Crash smoke checks used the combined archive (CacheHot + CaveRock distance + FishLod), for 180 seconds per scene on all7 and pub5. All six runs recorded without Code Error or early exit: sonar, deepwater, and sonar with deep sonar enabled on both packs. The first combined-build attempt exposed an uninitialized cached Camera Y local in `Fish_Step_2` when a guarded block was skipped. The snapshot was moved to the event entry and the six smoke checks were rerun successfully.

The CaveRock visibility change affects which instances reach drawing, so baseline and CaveRock-only all7 builds were recorded with `REC=1` for dock and deepwater. One frame at 20 seconds was extracted from each video with ffmpeg. The sampled dock and deepwater frames show no visible difference between the baseline and candidate.

## Verification

- `dotnet build -c Release -m:1`: passed with zero warnings.
- `dotnet test -m:1`: passed; 694 Core tests and 131 GUI tests passed, 20 skipped.
- `bash tools/run-real-tests.sh`: passed; 1 real-file test passed.
