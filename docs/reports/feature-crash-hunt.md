# Feature crash hunt: Submarine and Harbor Town

This report records 20-minute autoplay runs on the legacy 2023 base. Profile archives were built outside the repository with this worktree's ModMaker and CLI. Game archives and captured screenshots remain under `~/jamm-night/feature-crash-hunt/` and `~/jamm-night/autoplay/`.

## Run matrix

| Profile | Seed | Result | Feature coverage | Run evidence |
| --- | ---: | --- | --- | --- |
| Submarine solo | 23001 | PLAYED, 20 min | Bought/equipped Submarine; dive, light toggle, cast, surface | [final screenshot](</home/varialbe/jamm-night/autoplay/sub-solo-23001/final.png>); [actions](</home/varialbe/jamm-night/autoplay/sub-solo-23001/actions.tsv>) |
| Submarine solo | 23002 | PLAYED, 20 min (passing rerun) | Bought/equipped Submarine; dive, light toggle, cast, surface | [final screenshot](</home/varialbe/jamm-night/autoplay/sub-solo-23002/final.png>); [actions](</home/varialbe/jamm-night/autoplay/sub-solo-23002/actions.tsv>) |
| Submarine solo | 23003 | PLAYED, 20 min (rerun) | Bought/equipped Submarine; dive, light toggle, cast, surface | [final screenshot](</home/varialbe/jamm-night/autoplay/sub-solo-23003/final.png>); [actions](</home/varialbe/jamm-night/autoplay/sub-solo-23003/actions.tsv>) |
| Harbor Town solo | 23101 | PLAYED, 20 min | Harbor Town menu opened in-game | [menu screenshot](</home/varialbe/jamm-night/autoplay/harbor-solo-23101/harbor-menu.png>); [final screenshot](</home/varialbe/jamm-night/autoplay/harbor-solo-23101/final.png>); [actions](</home/varialbe/jamm-night/autoplay/harbor-solo-23101/actions.tsv>) |
| Harbor Town solo | 23102 | PLAYED, 20 min | Harbor Town menu opened in-game | [menu screenshot](</home/varialbe/jamm-night/autoplay/harbor-solo-23102/harbor-menu.png>); [final screenshot](</home/varialbe/jamm-night/autoplay/harbor-solo-23102/final.png>); [actions](</home/varialbe/jamm-night/autoplay/harbor-solo-23102/actions.tsv>) |
| Harbor Town solo | 23103 | PLAYED, 20 min (retry after Steam startup failure) | Harbor Town menu opened in-game | [menu screenshot](</home/varialbe/jamm-night/autoplay/harbor-solo-23103/harbor-menu.png>); [final screenshot](</home/varialbe/jamm-night/autoplay/harbor-solo-23103/final.png>); [actions](</home/varialbe/jamm-night/autoplay/harbor-solo-23103/actions.tsv>) |
| Submarine + Harbor Town + Endless Ocean + Weather Pack + Silo | 23201 | Submarine and Harbor benches RECORDED; no Code Error | Submarine purchase, dive at three depths, light toggle, cast/reel, and surface all passed. Harbor bench completed all 15 building/level commands (0, 1, 5, 20, 100) after the initial Steam failure was retried; no Code Error. | [Submarine telemetry](</home/varialbe/jamm-night/runs/c23201-submarine/jamm_bench_submarine.csv>); [underwater screenshot](</home/varialbe/jamm-night/feature-crash-hunt/c23201-submarine-t60.png>); [Harbor telemetry](</home/varialbe/jamm-night/runs/c23201-harbor/jamm_bench_harbor.csv>); [Harbor screenshot](</home/varialbe/jamm-night/feature-crash-hunt/c23201-harbor-t60.png>); [initial Harbor Steam failure log](</home/varialbe/jamm-night/feature-crash-hunt/c23201-harbor-initial-steam-failure.log>) |
| Submarine + Harbor Town + Endless Ocean + Weather Pack + Silo | 23202 | Pending | Pending | Pending |
| Submarine + Harbor Town + Endless Ocean + Weather Pack + Silo | 23203 | Pending | Pending | Pending |
| Seven-pack + Submarine + Harbor Town | 23301 | Pending | Pending | Pending |
| Seven-pack + Submarine + Harbor Town | 23302 | Pending | Pending | Pending |
| Seven-pack + Submarine + Harbor Town | 23303 | Pending | Pending | Pending |

The first 23002 run missed feature coverage because the bot could not find the boat shop control. A screenshot showed the runner typing `close` into JAMM Console, where it is not a supported command, leaving console/menu state in the way. The runner now closes the console with a held F12 key, waits through startup, retries Submarine equipment, avoids random `catalog all`, and can open the native boat menu through a test-only F11 trigger when the icon probe is unavailable. The passing 23002 and 23003 reruns both completed with the required Submarine actions and no Code Error. The first 23003 run exited early without a fatal error; it was discarded and rerun. Harbor seed 23103 had one `SteamInit failed` launch (`STEAM`); the runner restarted Steam, and the retry completed with Harbor menu coverage.

## Known sonar Code Error and fix

Earlier runs produced these Code Errors:

> Variable jmk_weather_pack_lowbelly.s5(101204, -2147483648) not set before reading it. at gml_Object_SonarPanel_Draw_0

> Variable Dijon.s5(101204, -2147483648) not set before reading it. at gml_Object_SonarPanel_Draw_0

The SonarPanel draw event reads each fish's `s5` sonar scale. JAMM-spawned fish from Weather Pack and Endless Ocean had not initialized the sonar state expected by the vanilla panel. Commit `3db05c6` added the shared `FishSonarRuntime` initializer and invokes it for those generated fish before they can be drawn. Regression coverage is in `FishRuntimeTests`, `WeatherRuntimeTests`, and `EndlessOceanTests`, including checks that `s5` is initialized and the shared helper is emitted once. The 20-minute reruns `fishsonar/11002` and `fishsonar/11005` completed PLAYED without a Code Error.

## Verification

- `dotnet build -c Release`: passed, zero warnings and zero errors.
- `dotnet test -c Release`: core 645 passed / 17 skipped; GUI 130 passed / 1 skipped; no failures.
- `bash -n tools/vps/autoplay.sh` and `git diff --check`: passed.
