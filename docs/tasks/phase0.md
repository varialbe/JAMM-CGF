# Task: Phase 0 — feasibility check

Goal: find out whether merging mods is possible with the user's game files, before anything else
is built. Read `AGENTS.md` first and follow it.

## Deliverables
1. Solution `JAMM-CGF.sln` with `src/Jamm.Core`, `src/Jamm.Cli` (assembly/executable name `cgfmm`),
   `tests/Jamm.Core.Tests` (xUnit), all `net10.0`, Core references
   `external/UndertaleModTool/UndertaleModLib/UndertaleModLib.csproj`.
2. `.gitignore` (dotnet + `*.win` + `*.zip` + `/out/`), `LICENSE` (GPL-3.0 full text is fine to fetch from
   the UndertaleModTool submodule's LICENSE.txt — same license).
3. Command `cgfmm inspect <data.win>`:
   - file size, SHA-256
   - from GEN8: game name, display name, GameMaker version (Major.Minor.Release.Build as UTML reports it,
     plus UTML's detected version if it differs, e.g. "2022.1+"), bytecode version, build/timestamp if available
   - counts: code entries, scripts, sprites, objects, rooms, sounds, backgrounds/tilesets, texture pages,
     texture page items, strings, variables, functions, fonts, shaders, paths, timelines, extensions
   - **VM vs YYC**: use UTML's YYC detection (e.g. `UndertaleData.IsYYC()` or an empty/missing CODE chunk).
     If YYC: print a clear message that merging is impossible (no bytecode) and exit with code 2.
4. Command `cgfmm compare <original> <mod> [<mod> ...]`:
   For each mod vs original, per asset type (code, scripts, sprites, objects, rooms, sounds,
   texture pages, strings, variables, functions, fonts, backgrounds):
   - matched by **name** (strings: by value; texture pages: by index + content hash)
   - classify: identical / changed / added (only in mod) / removed (only in original)
   - "identical" means a content fingerprint matches. Fingerprints (keep simple, document them in code):
     code = hash of the bytecode instruction stream as disassembled text (do NOT decompile in Phase 0, too slow);
     sprite = width, height, origin, frame count + hash of each frame's texture-page-item region bytes;
     object = sprite name, parent name, flags, and the list of event code names;
     room = size, and lists of instance object names + positions; sound = flags + audio bytes hash;
     texture page = hash of the embedded texture data; others: sensible equivalent.
   - same GameMaker version and bytecode version as original? same GEN8 game name?
   - overall "% of original assets unchanged" and a verdict:
     LIKELY SAME BASE (>= ~80% of original code entries identical), POSSIBLY DIFFERENT VERSION (40–80%),
     DIFFERENT BASE (< 40%) — print the numbers that led to it.
   - output: readable terminal tables; `--json <file>` writes the full result as JSON.
5. Command `cgfmm phase0 --original <data.win> --mods <file or dir> [--out <report.md>]`: runs inspect on
   all files and compare for every mod, writes a Markdown report (default `docs/reports/phase0.md`) and
   prints a short summary. The report contains no game content other than asset *names* and counts.
6. Loading must be robust: catch UTML load exceptions per file and report "could not read <file>: <reason>".
   Show load progress for big files if cheap. UTML may print warnings via a callback — collect them into the report.
7. Unit tests for the pure logic (classification, verdict thresholds, fingerprint stability on a tiny
   in-memory `UndertaleData` if UTML allows constructing one; otherwise test the comparer on fake models).
8. `README.md` stub (title, one-paragraph description, build + phase0 usage). A polished README comes later.

## Verify before finishing
- `dotnet build -c Release` (zero warnings in our projects) and `dotnet test` pass.
- Run `cgfmm phase0` against the real files listed in AGENTS.md and make sure the report is produced.
  Include the printed summary in your final message.
- `git add` everything relevant and `git commit` with a clear message (do not push).
  Make sure `git status` shows no `*.win`, `bin/`, `obj/`.
