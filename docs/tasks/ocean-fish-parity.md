# Task: generated-fish variable parity audit (branch `exp/ocean-fish-parity`)

Context: autoplay v2 crashed far east in Endless Ocean with
`Variable Dijon.s5 not set before reading it. at gml_Object_SonarPanel_Draw_0` (evidence:
`~/jamm-night/autoplay/apv2b-own-11002/`). Codex fixes that specific crash on another branch
(`fix/ocean-sonar-crash`) – do NOT edit `EndlessOceanMod.cs` or the chunk streamer.

## Do (analysis + test tooling only)
1. Using UndertaleModLib on the legacy base (`~/cgf-files/legacy/2023-08-16_manifest-9039707068725406600/data.win`),
   list every vanilla code entry that reads instance variables of fish objects through another instance
   (`with`, `other.`, `<fish>.var`, `instance_find`, sonar/HUD/catch/collection/save code). For each variable,
   record where vanilla sets it (Create, Step, spawner, Alarm, ...).
2. Compare with how Endless Ocean creates and restores its fish (`src/Jamm.Core/Builtins/EndlessOceanMod.cs`,
   worldgen GML sources): which variables could be missing for a generated or restreamed fish, and in which
   order/situation (first frame after creation, after release/reload, under Perf deactivation, ...).
3. Write `docs/reports/ocean-fish-parity.md` with a table (reader, variable, vanilla setter, risk for generated
   fish, suggested fix) and add an xUnit test helper in `tests/Jamm.Core.Tests` that can assert "every variable the
   sonar/HUD readers use is assigned in a fish's Create event or in the generator" on a synthetic archive.
4. `dotnet build -c Release` zero warnings, `dotnet test` green.

## Rules
AGENTS.md applies. Every command in the foreground (no background tasks). Simple single commands (no `&&`, `;`,
pipes, redirections, no `rm`); if a command is denied, write a small script file and run it instead. Do not commit.
