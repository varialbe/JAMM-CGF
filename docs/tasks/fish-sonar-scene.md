# Task: sonar coverage scene + fish-spawn audit (branch `exp/fish-sonar-scene`, Gemini)

Context: autoplay crashed with `Variable jmk_weather_pack_lowbelly.s5 not set before reading it at
gml_Object_SonarPanel_Draw_0` (evidence `~/jamm-night/autoplay/finalb-11002/error.txt`). Fish created by JAMM after
vanilla `SonarPanel` Step subtype 1 miss `s1`–`s5`. Codex fixes the runtime on `fix/fish-sonar` (src/Jamm.Core).
You must NOT edit src/Jamm.Core. Your part:

1. ModMaker (`tools/Jamm.ModMaker`): extend the existing `weather` scene (look at how the `worldgen` scene
   records `sonar_missing`) so it enables the sonar view, forces each JAMM weather that spawns fish in turn
   (rainy, super-sunny, windy, fog – use the existing weather runtime globals/scripts), waits until weather fish
   exist and records per checkpoint: weather id, fish count, and `sonar_missing` = number of `Fish` instances
   without `s5` (use `variable_instance_exists`). Keep the scene working on archives without weather-pack.
2. Audit: list every code path in src/Jamm.Core and examples/modkit that creates instances of `Fish` descendants
   (file:line, which feature, does it set s1–s5 today). Write it to `docs/reports/fish-spawn-audit.md`.
3. `dotnet build -c Release` zero warnings, `dotnet test -c Release` green.
The orchestrator runs the game. Every command in the foreground; one simple command per tool call (no &&, ;,
pipes, redirections, no rm, no subshells); if a command is denied, write a small python3 script and run it.
Do not commit.

## Round 2 (Gemini): apply your own review findings
Your review `docs/reports/fish-sonar-review.md` approved Codex's fix with minor findings. This branch now
contains `fix/fish-sonar`. Implement findings 1 (integration test: Fish API + Weather API via ArchiveBuilder, then
EndlessOceanMod.Apply, single script/code) and 2 (`FishSonarRuntime.Ensure` reuses `data.Code.ByName` like
`FishRuntime.EnsureScript`), plus any further Low finding you listed if it is cheap and safe.
`dotnet build -c Release` zero warnings, `dotnet test -c Release` green. Same command rules as before. Do not commit.

## Round 3 (Codex Luna): ModMaker harness fixes from the 22c matrix
Candidate `~/jamm-night/personal-tge/own-mods-2026-11-22c.win` (built from fix/fish-sonar incl. this branch):
1. `--scene weather` CODE ERROR (`~/jamm-night/runs/finalc-weather/game.log`):
   `Variable obj_jamm_bench_weather.weather_count not set before reading it. at gml_Script_jamm_weather_find`
   (Step Event1 of obj_jamm_bench_weather). The new sonar-coverage code in `tools/Jamm.ModMaker/WeatherScene.cs`
   calls `jamm_weather_find` outside the weather controller's scope; call it inside `with (obj_jamm_weather)` (or
   whatever the runtime expects) everywhere, and only after the controller has initialised.
2. `--scene stress` instrumentation fails: "The stress scene needs 'obj_jamm_perf'. Merge Weather Pack and JAMM
   Perf before instrumenting." Since `412a7f2 fix: disable costly Perf hooks by default` the personal package no
   longer contains that controller. Make the stress scene work without it (measure what it can, record that Perf
   hooks are off) while still using it when present.
Add/adjust unit tests. `dotnet build -c Release` zero warnings, `dotnet test -c Release` green. Then verify in-game:
`JAMM_MODMAKER_DLL=$PWD/tools/Jamm.ModMaker/bin/Release/net10.0/jamm-modmaker.dll ~/jamm-night/bin/run.sh
~/jamm-night/personal-tge/own-mods-2026-11-22c.win r3 weather 900` and `DUR=200 … r3 stress 900` (shared game lock,
may wait; on EXITED run `~/jamm-night/bin/steam-restart.sh` and retry; delete `~/jamm-night/runs/r3-*/game`).
Report the weather CSV (`jamm_weather*.csv` in the Wine AppData folder) incl. sonar_missing per weather.
Commit on this branch; do not push.
