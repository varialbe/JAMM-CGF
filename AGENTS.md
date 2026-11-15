# AGENTS.md — working on JAMM-CGF

Rules for any agent (Codex, Gemini, Claude) or human contributor. Read this first.

## What this is
JAMM-CGF ("Just Another Mod Manager" for Cat Goes Fishing) combines several Cat Goes Fishing
mods into one `data.win`. CGF is a GameMaker game; mods ship as full replacement `data.win`
files, so normally only one can run at a time. JAMM diffs each mod against the original,
detects conflicts, and builds a merged `data.win`.

## Layout
```
src/Jamm.Core/        library: loading, inspecting, diffing, merging data.win (no console I/O)
src/Jamm.Cli/         console app, executable name `cgfmm`; thin layer over Jamm.Core
tests/Jamm.Core.Tests xUnit tests (no game files required; real-file tests are opt-in, see below)
external/UndertaleModTool   git submodule, pinned to release 0.9.2.0 — DO NOT EDIT
docs/                 design notes, phase reports, task specs (docs/tasks/)
```

## Tech
- .NET 10 (`net10.0`). UndertaleModLib 0.9.x requires it; .NET 8 is not possible with a current UTML.
- UndertaleModLib is referenced as a ProjectReference to the submodule, never copied.
- License: GPL-3.0 (required because we link UndertaleModLib, which is GPL-3.0).

## Hard rules
1. **Never modify input game files.** Open `data.win` files read-only. Anything that writes a
   `data.win` must write to a new path; the original is backed up before any install step.
2. **Never commit game data.** No `*.win`, no extracted sprites/sounds/code from the game.
   Game files are copyrighted. `.gitignore` enforces `*.win`.
3. Errors must be clear for a non-developer: say what failed, which file, and what to do next.
   No raw stack traces unless `--verbose` is given.
4. Keep Jamm.Core free of console output; return result objects, let the CLI format them.
5. Small, readable classes; XML doc comments on public types. Prefer clarity over cleverness.
6. Build must pass with zero warnings in our projects: `dotnet build -c Release`.
7. Do not run `git push`, create releases, or change GitHub settings — the orchestrator does that.

## Real test files (local only, never commit)
- Original / merge base: the legacy 2023 build (see docs/legacy-version.md), stored outside the repo, e.g.
  `~/Games/CatGoesFishing-legacy/2023-08-16_manifest-9039707068725406600/data.win`
- Current Steam build (YYC, only useful as a negative test): `<Steam>/steamapps/common/Cat Goes Fishing/data.win`
- Mods: `~/Cat-Goes-Fishing-Mods/the-abyss-beta.win`, `cave-expansion.win`, `colour-expansion.win`
Real-file tests read the env var `JAMM_ORIGINAL` / `JAMM_MODS_DIR` and are skipped when unset.

## Commands
```
dotnet build -c Release
dotnet test
dotnet run --project src/Jamm.Cli -- <command> ...
```
