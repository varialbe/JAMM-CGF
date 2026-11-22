# Fish Sleep population and counter check

## Cause and change

Vanilla `FishThinner_Step_0` accumulates a cull timer below 60 FPS and resets it at 60 FPS or
above. Fish Sleep pushed the vanilla dock scene above that threshold, so thinning stopped and the
population rose from roughly 956 to 1,459 fish. This was an interaction with the adaptive
population controller, not fish respawn or sleeping fish being omitted from population counts.

When Fish Sleep is enabled and at least one fish is asleep, the high-FPS reset branch now advances
the cull timer at the vanilla 45–60 FPS rate. The branch is gated by both Fish Sleep and the live
asleep count, so sleep-off behavior stays vanilla. Fish destruction and FishThinner deactivation
also release that fish's asleep counter slot; culling deactivates instances without running their
Destroy event.

## Local bench

Four 30-second dock captures used seed 1337, the local `decor/vanilla.win` and `decor/pub5.win`
inputs, `--fish-sleep`, and runtime `[Perf] FishSleep=0`/`1`. `fish_count` is the CSV mean across
captured samples; `asleep` is the benchmark's final Fish Sleep counter snapshot.

| Build | Fish Sleep | Fish count (mean) | Asleep (end) |
| --- | ---: | ---: | ---: |
| vanilla | off | 915 | 0 |
| vanilla | on | 932 | 604 |
| pub5 | off | 2,161 | 0 |
| pub5 | on | 2,065 | 1,660 |

Vanilla sleep-on now differs from sleep-off by 1.9% in mean fish count; pub5 differs by 4.4%.
The affected run logs are local under `bench-runs/fsb-{vanilla,pub5}-{off,on}-dock` and were not
added to the repository.

## Verification

- Added a regression test for the sleep-gated FishThinner cadence and asleep-count release on
  cull/destruction.
- `dotnet build -c Release`: passed with zero warnings.
- `dotnet test -c Release`: 660 Core tests passed, 18 skipped; 130 GUI tests passed, 1 skipped.
