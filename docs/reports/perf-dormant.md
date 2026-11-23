# Dormant fish and decor performance

## Changes

- Fish Sleep remains instance based so fish continue to participate in counts, sonar, saves,
  collisions, and population logic. The benchmark instrumentation now puts the Fish Step 2
  stagger check before its timing hooks when `--fish-sleep` is enabled. Sleeping fish skip the
  enter/leave script calls on the seven ticks when the existing sleep logic would return early;
  the eighth tick still runs the wake and catch-up logic.
- Decor Sleep may deactivate ordinary `Rock` descendants when its archive-wide reference scan
  finds no gameplay queries. `CaveRock`, `Collidable`, lights, child-creating objects, draw-sync
  objects, and objects with other unsafe references remain protected. The mode remains opt-in.

## Method

Measurements used the local Ryzen 7 5700X system, Steam/Proton benchmark harness, and its isolated
`jamm-bench` prefix.
Vanilla, pub5, and all7 were built from this branch. Each row contains two unprofiled 30-second
captures per mode and scene, with an 8-second warmup. `Old` uses neither sleep flag; `New` uses
`--fish-sleep --decor-sleep`. FPS is `1,000,000 / mean frame_us`; loop is mean `loop_us`; p95 is
`frame_us` p95. Values average the two per-run metrics, so p95 is the mean of the two run-level
p95 values. Times are milliseconds. All runs used the harness lock and no xdotool.

## Results

| Build | Scene | Old FPS | Old loop ms | Old p95 ms | New FPS | New loop ms | New p95 ms |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| vanilla | dock | 43.9 | 22.0 | 27.6 | 95.3 | 9.1 | 11.8 |
| vanilla | deepwater | 45.4 | 21.4 | 26.5 | 93.8 | 9.3 | 15.3 |
| pub5 | dock | 10.4 | 89.8 | 120.1 | 22.2 | 43.9 | 72.2 |
| pub5 | deepwater | 6.1 | 146.6 | 560.9 | 18.3 | 51.2 | 88.9 |
| all7 | dock | 17.2 | 56.7 | 70.3 | 32.7 | 28.4 | 33.4 |
| all7 | deepwater | 13.2 | 79.5 | 97.1 | 25.1 | 39.5 | 46.6 |

All six build/scene pairs improved in both mean frame time and mean loop time. The benchmark's
Fish Sleep counters showed 586–1,873 asleep fish and Decor Sleep deactivated 336–3,156 instances
across the New captures. Fish instances were not deactivated.

## Stability and profiler runs

All 180-second all7 captures were recorded without a code-error outcome:

| Run | Result | Fish asleep | Decor deactivated |
| --- | --- | ---: | ---: |
| sonar (`--deep-sonar`) | RECORDED | 1,698 | 3,070 |
| deepwater | RECORDED | 1,853 | 3,001 |
| dock | RECORDED | 1,799 | 3,156 |

The single profiled all7 dock run was recorded. Its profile averaged 105.5 ms of loop time across
278 ticks. `gml_Object_Fish_Step_2` accounted for 31% of loop CPU, `CaveRock_Step_0` 17%, and
`FishLarge_Step_0` 6%. The benchmark enter/leave timing scripts each ran about 1,384 times per
tick and accounted for 3% and 2% of loop CPU respectively; sleeping fish bypass those hooks on
the seven staggered early-exit ticks.
