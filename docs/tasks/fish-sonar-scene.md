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
