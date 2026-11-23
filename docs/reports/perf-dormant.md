# Dormant fish and decor performance — Round 2

## Changes

- Sleeping fish now switch to a lightweight per-species dormant object with `instance_change`, then switch back on wake. Each proxy inherits from its original fish object and supplies empty Step/Draw handlers, preserving `with(Fish)`, species/tier `instance_number`, collisions, and population scans while bypassing inherited per-frame events. Sprite, mask, depth, and image state are restored across each swap.
- The controller tracks dormant object types and wakes fish when their state becomes active or they enter the relevance zone. Lure targeting wakes immediately; FishThinner wakes a sleeper before deactivating it. The dormant count lets the controller return immediately when no fish are asleep. In enabled mode, it checks the dormant population every eighth tick.
- CaveRock's CameraCullOnly guard now relies on the controller's managed-instance culling and avoids a redundant per-instance `jamm_ds_vis` read. Skipped descendants keep their original Camera logic.

## A/B method

Compared the `main` worktree (`ffe42e9`) with `exp/perf-dormant`, using the same vanilla, pub5, and all7 input builds, `--fish-sleep --decor-sleep`, dock and deepwater scenes, two repeats per row, 8-second warmup, and 20-second unprofiled captures. All runs used the serialized `~/jamm-bench/run.sh` harness. FPS is `1,000,000 / mean frame_us`; loop is mean `loop_us`; p95 is frame `p95`. The table averages the two run-level metrics; times are milliseconds.

The branch captures measured the dormant-proxy implementation before the final scan-cadence, FishThinner wake, and CaveRock guard refinements in the committed source. The table therefore isolates the core proxy change but does not quantify those final refinements; the cadence refinement reduces controller scanning from every tick to every eighth tick when sleep is enabled.

## Results

| Build | Scene | Main FPS | Main loop ms | Main p95 ms | Branch FPS | Branch loop ms | Branch p95 ms |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| vanilla | dock | 67.0 | 13.1 | 17.1 | 69.0 | 12.7 | 18.5 |
| vanilla | deepwater | 85.3 | 10.4 | 12.0 | 80.0 | 11.0 | 15.1 |
| pub5 | dock | 21.5 | 44.4 | 55.1 | 20.3 | 47.0 | 75.2 |
| pub5 | deepwater | 19.9 | 48.0 | 59.2 | 21.0 | 45.4 | 59.6 |
| all7 | dock | 21.4 | 44.4 | 46.1 | 22.1 | 42.9 | 47.8 |
| all7 | deepwater | 18.8 | 50.3 | 53.3 | 20.8 | 45.5 | 50.4 |

The branch improved mean loop time in four of six rows. The largest gains were all7 deepwater (-9.4% loop time) and pub5 deepwater (-5.3%). Vanilla deepwater and pub5 dock regressed; pub5 dock also had a higher p95. The results are mixed rather than a uniform win.

## Stability and profiler

The all7 crash-scene captures were recorded without a code-error outcome:

| Scene | Duration | Result |
| --- | ---: | --- |
| sonar (`--deep-sonar`) | 180 s | Recorded |
| deepwater | 180 s | Recorded |
| dock | 180 s | Recorded |

One profiled all7 dock capture was recorded with a 105.5 ms average loop across 278 ticks. The profile attributed 31% of loop CPU to `gml_Object_Fish_Step_2`, 17% to `CaveRock_Step_0`, and 6% to `FishLarge_Step_0`. The profiler run also predates the final refinements described above.

## Verification

- `dotnet build -c Release -m:1`: passed with 0 warnings and 0 errors.
- `dotnet test -c Release -m:1`: 667 passed, 0 failed, 18 opt-in real-file tests skipped; GUI tests 130 passed, 0 failed, 1 opt-in real-file test skipped.
- FishSleep and DecorSleep tests cover proxy parentage and event overrides, type mapping and swaps, wake paths, population/cull handling, and the CameraCullOnly guard.
