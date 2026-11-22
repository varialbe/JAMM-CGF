# Task: sonar state for every JAMM-spawned fish (branch `fix/fish-sonar`, Codex)

## Bug (found by the orchestrator's final matrix, autoplay v2, seeds 11002 and 11005)
Personal package (perf, hd-lite, console, smooth, silo, weather-pack, endless-ocean) built from main 0c6c431:
```
FATAL ERROR in action number 1 of Draw Event for object SonarPanel:
Variable jmk_weather_pack_lowbelly.s5(101204, -2147483648) not set before reading it.
at gml_Object_SonarPanel_Draw_0
```
Evidence: `~/jamm-night/autoplay/finalb-11002/`, `~/jamm-night/autoplay/finalb-11005/` (error.txt, game.log, actions.tsv, shots/).

Same class as the Endless Ocean crash fixed on main (`docs/reports/ocean-sonar-crash.md`,
`src/Jamm.Core/Builtins/Worldgen/jmk_endless_ocean_sonar.gml`): vanilla `SonarPanel` Step subtype 1 sets
`s1`–`s5` on fish, Draw reads them; a fish created by JAMM after that pass reaches Draw without them.

## Do
1. Turn the Endless Ocean sonar initializer into one shared JAMM runtime script (e.g. `jamm_fish_sonar_init(inst)`)
   emitted once per archive whenever any JAMM fish spawner is present; same semantics as vanilla
   `SonarPanel_Step_1` (asset names via `asset_get_index`, `instance_exists` guards, no bare vanilla indices).
2. Call it for every fish JAMM creates:
   - weather population (`src/Jamm.Core/Building/Weather/population.gml`, `instance_create(sx, sy, species)`),
   - the ModKit/fish-catalog spawner in `src/Jamm.Core/Building/FishRuntime.cs` (`jamm_fish_register` spawn loop),
   - Endless Ocean (`jmk_endless_ocean_load.gml`) – switch it to the shared script, keep behaviour identical,
   - any other JAMM path that creates `Fish` descendants (check Submarine, Silo, Harbor Town, ItemsRuntime, FishGrid
     wrappers). Only call it when the returned instance still exists. Never change vanilla fish or vanilla events.
3. Regression tests on synthetic archives: weather population and catalog spawner fish have `s1`–`s5` right after
   spawning; the shared script is emitted once even when several features need it; Endless Ocean tests stay green.
4. `dotnet build -c Release` zero warnings, `dotnet test -c Release` green.
5. In-game (shared game lock, Steam restart via `~/jamm-night/bin/steam-restart.sh` on EXITED):
   `REPO=$HOME/jamm-wt/fish-sonar TAG=fish-sonar bash ~/jamm-night/n21/build-personal.sh`, then
   `JAMM_MODMAKER_DLL=$HOME/jamm-wt/fish-sonar/tools/Jamm.ModMaker/bin/Release/net10.0/jamm-modmaker.dll
   ~/jamm-night/bin/autoplay.sh ~/jamm-night/personal-tge/own-mods-fish-sonar.win fishsonar 20 11002` (and seed 11005).
   Delete `~/jamm-night/runs/*/game` copies afterwards; watch disk (`df -h /`).
6. Short report `docs/reports/fish-sonar-init.md`.

AGENTS.md applies. Commit on this branch; do not push or merge into main. Gemini works in parallel on
`exp/fish-sonar-scene` (tools/Jamm.ModMaker only) – do not edit tools/Jamm.ModMaker in this task.
