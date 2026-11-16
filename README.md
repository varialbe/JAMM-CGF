# JAMM-CGF — Just Another Mod Manager for Cat Goes Fishing

![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![GPL-3.0](https://img.shields.io/badge/license-GPL--3.0-blue)

Cat Goes Fishing mods usually replace the entire `data.win`, so only one can run at a
time. JAMM-CGF aims to compare each mod against the original, identify conflicts and
build one merged `data.win`. Phase 2 provides read-only inspection, reusable semantic JSON patches and conflict checks with three-way GML previews.

> **Important: you need the legacy bytecode version.** The inspected August 2024 Steam
> build is YYC and contains no bytecode. Known mods target GameMaker 1.4 bytecode 16 and
> need the matching old executable too. The right base for the known mods is the 16 Aug 2023 build
> (depot 343782, manifest `9039707068725406600`), downloadable from Steam if you own the game:
> [Getting the bytecode version](docs/legacy-version.md).

## Status and roadmap

| Stage | Status | Scope |
| --- | --- | --- |
| Phase 0 | Done | Inspect files, fingerprint assets, compare bases and report feasibility |
| Phase 1 | Done | GML/asset diffs, cached JSON patches and unified diffs |
| Phase 2 | Done | Conflict detection, three-way code previews and reusable resolution choices |
| Phase 3 | Planned | Build a merged `data.win` |
| Phase 4 | Planned | Install with backups and launch |
| GUI | Later | Graphical mod management |

See the [Phase 0 findings](docs/reports/phase0.md), [Phase 0 verification](docs/reports/phase0-verification.md)
and [Phase 2 verification](docs/reports/phase2-verification.md).
Writing a merged game archive and installation are not available yet.

## Requirements and build

- Linux and the **.NET 10 SDK**.
- Git with submodule support.
- Your own legal game copy and mod files, stored outside the repository.

```sh
git clone --recursive <repository-url> JAMM-CGF
cd JAMM-CGF
# If already cloned without submodules:
git submodule update --init --recursive
dotnet build -c Release
dotnet test
```

UndertaleModLib is a project reference to the unmodified UndertaleModTool submodule,
pinned to release **0.9.2.0**. Current UndertaleModLib requires .NET 10; .NET 8 is not supported.

## Commands

The CLI executable/assembly is named **`cgfmm`**. These examples run it from the checkout;
`cgfmm <command>` works when the built executable is on your command path.

Inspect metadata, asset counts, SHA-256, GameMaker version and VM/YYC compilation:

```sh
dotnet run --project src/Jamm.Cli -- inspect "$HOME/Games/CatGoesFishing-legacy/data.win"
```

Compare one original against one or more mods; optionally write full JSON classifications:

```sh
dotnet run --project src/Jamm.Cli -- compare \
  "$HOME/Games/CatGoesFishing-legacy/data.win" \
  "$HOME/Cat-Goes-Fishing-Mods/the-abyss-beta.win" \
  "$HOME/Cat-Goes-Fishing-Mods/cave-expansion.win" \
  --json out/comparison.json
```

Diff a mod against the legacy base, save a reusable patch, and inspect one changed asset:

```sh
dotnet run --project src/Jamm.Cli -- diff \
  "$HOME/Games/CatGoesFishing-legacy/data.win" \
  "$HOME/Cat-Goes-Fishing-Mods/the-abyss-beta.win" \
  --name "The Abyss" --out /tmp/cgfmm-patches/the-abyss-beta.cgfmm-patch.json

# Repeat the command to reuse its matching cache; --force recomputes.
# Choose an asset name from the patch's changes.code.changed array:
dotnet run --project src/Jamm.Cli -- diff \
  "$HOME/Games/CatGoesFishing-legacy/data.win" \
  "$HOME/Cat-Goes-Fishing-Mods/the-abyss-beta.win" \
  --out /tmp/cgfmm-patches/the-abyss-beta.cgfmm-patch.json --show "<asset name>"
```

The default output is `<mod stem>.cgfmm-patch.json` in the current directory.
`--json-summary` emits machine-readable counts and timings; progress goes to stderr.
Code is compared as decompiled GML, with warned disassembly fallback for failures.
GameMaker/bytecode version mismatches are rejected. Patches contain game code and
string values: **keep them local and never commit or redistribute them**. Binary
payloads are content hashes referencing the mod archive. See [patch format](docs/patch-format.md)
for field meanings, identities, caching and future merge/build requirements.

Check mods in load order (later entries win by default in the future builder):

```sh
dotnet run --project src/Jamm.Cli -- check \
  "$HOME/Cat-Goes-Fishing-Mods/the-abyss-beta.win" \
  "$HOME/Cat-Goes-Fishing-Mods/cave-expansion.win" \
  "$HOME/Cat-Goes-Fishing-Mods/colour-expansion.win" \
  --base "$HOME/Games/CatGoesFishing-legacy/data.win" --json out/check.json

# Existing patches also work; choose an asset from the findings table:
dotnet run --project src/Jamm.Cli -- check \
  out/patches/the-abyss-beta.cgfmm-patch.json out/patches/cave-expansion.cgfmm-patch.json \
  --base "$HOME/Games/CatGoesFishing-legacy/data.win" --details '<asset name>'
```

Example output (illustrative synthetic names/counts):

```text
Load order (later mod wins by default when building):
  1. Mod A: +2 changed 1 removed 0; warnings 0
  2. Mod B: +1 changed 1 removed 0; warnings 0
CONFLICT (1 findings)
list         code           fish_catalog       Mod A + Mod B: different insertions at the same position.
AUTO (1 findings)
field        objects        fish               Mod A + Mod B: changes to different fields form a union.
INFO (0 findings)
WARNING (0 findings)
Decisions required. Use --details <asset> to inspect a finding. No game files were written.
```

`check` returns **0** when there are no conflicts, **3** when conflicts need decisions,
and **1** for an error. It generates/reuses patches in `./patches/`, or beside the
JSON report in `patches/`; `--patches-dir` overrides this. `--details` prints GML
conflict markers or base/mod field values. All patches must match the selected
base's complete SHA-256. Reports include merged text and per-hunk choices for Phase 3.
Keep reports local: they can include game code and strings. See [conflict rules and
limitations](docs/conflicts.md). Checking does not build or install a merged archive.

Run inspection and comparisons together and write a Markdown report:

```sh
dotnet run --project src/Jamm.Cli -- phase0 \
  --original "$HOME/Games/CatGoesFishing-legacy/data.win" \
  --mods "$HOME/Cat-Goes-Fishing-Mods" \
  --out out/phase0.md
```

`--mods` accepts a single file or a directory of `.win` files. The default report path is
`docs/reports/phase0.md`. Phase 0 reports and comparison JSON contain file names, metadata, counts and hashes;
input paths and extracted game content are omitted. Inputs are opened read-only and
cannot be selected as output destinations. Reports cannot use a `.win` destination.

```sh
dotnet run --project src/Jamm.Cli -- --help
dotnet run --project src/Jamm.Cli -- inspect missing.win --verbose
```

`--verbose` works with every command and includes exception details on failure. Normal
errors explain which file failed and what to check. A failed mod does not prevent analysis
of the remaining readable mods.

| Exit code | Meaning |
| --- | --- |
| 0 | Analysis completed, including different-base or YYC results from `compare` / `phase0` |
| 1 | Read, analysis, output or usage failure |
| 2 | `inspect`: YYC / empty CODE or unsupported VM bytecode; `diff`: YYC / empty CODE |
| 3 | `check`: analysis succeeded, conflicts need decisions |

Comparison matches named assets by ordinal name; strings use value hashes and texture
pages use indices. The overall unchanged percentage covers code, scripts, sprites, objects,
rooms, sounds, fonts and backgrounds. Strings and texture pages are reported separately.
The base verdict uses identical original code: at least 80% = likely same base; 40% to
below 80% = possibly different version; below 40% = different base. Similarity does not
guarantee a merge will work.

When either input is YYC, code, scripts, variables and functions are skipped. The result
says `NOT COMPARABLE: <file> is YYC (no bytecode)` and omits unchanged percentages.
Unsupported textures, Spine/SWF sprites and invalid texture regions warn and use
`unsupported:<reason>` markers; identical markers do not prove identical content.

## Tests

Synthetic tests run without game files. Opt into the read-only real-file test by setting
both environment variables:

```sh
JAMM_ORIGINAL="$HOME/Games/CatGoesFishing-legacy/2023-08-16_manifest-9039707068725406600/data.win" \
JAMM_MODS_DIR="$HOME/Cat-Goes-Fishing-Mods" \
dotnet test
```

Never commit game archives, extracted game content or personal absolute paths.

## Project layout and contributing

| Path | Purpose |
| --- | --- |
| `src/Jamm.Core/` | Read-only loader, fingerprints, semantic patches, line/field merges and conflict analysis; no console output |
| `src/Jamm.Cli/` | `cgfmm` commands and text/JSON reports |
| `tests/Jamm.Core.Tests/` | Synthetic regressions and opt-in real-file checks |
| `external/UndertaleModTool/` | Pinned dependency; do not edit |
| `docs/` | Design notes, task specs, reports and legacy-version instructions |

Read [AGENTS.md](AGENTS.md) before contributing. Keep classes small, document public types,
make errors useful to non-developers, preserve input files and run the build and tests.
Our projects must build with zero warnings.

## License and credits

JAMM-CGF is licensed under [GPL-3.0](LICENSE). It links
[UndertaleModLib / UndertaleModTool](https://github.com/UnderminersTeam/UndertaleModTool),
also licensed under GPL-3.0. Thanks to its contributors for the GameMaker archive tooling.
