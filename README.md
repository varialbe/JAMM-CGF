# JAMM-CGF — Just Another Mod Manager for Cat Goes Fishing

![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![GPL-3.0](https://img.shields.io/badge/license-GPL--3.0-blue)

Cat Goes Fishing mods usually replace the entire `data.win`, so only one can run at a
time. JAMM-CGF aims to compare each mod against the original, identify conflicts and
build one merged `data.win`. Phase 0 provides read-only inspection and content comparisons.

> **Important: you need the legacy bytecode version.** The inspected August 2024 Steam
> build is YYC and contains no bytecode. Known mods target GameMaker 1.4 bytecode 16 and
> need the matching old executable too. The right base for the known mods is the 16 Aug 2023 build
> (depot 343782, manifest `9039707068725406600`), downloadable from Steam if you own the game:
> [Getting the bytecode version](docs/legacy-version.md).

## Status and roadmap

| Stage | Status | Scope |
| --- | --- | --- |
| Phase 0 | Done | Inspect files, fingerprint assets, compare bases and report feasibility |
| Phase 1 | Planned | Asset diffing against the legacy base |
| Phase 2 | Planned | Conflict detection |
| Phase 3 | Planned | Build a merged `data.win` |
| Phase 4 | Planned | Install with backups and launch |
| GUI | Later | Graphical mod management |

See the [Phase 0 findings](docs/reports/phase0.md) and [verification](docs/reports/phase0-verification.md).
Merging and installation are not available yet.

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

Run inspection and comparisons together and write a Markdown report:

```sh
dotnet run --project src/Jamm.Cli -- phase0 \
  --original "$HOME/Games/CatGoesFishing-legacy/data.win" \
  --mods "$HOME/Cat-Goes-Fishing-Mods" \
  --out out/phase0.md
```

`--mods` accepts a single file or a directory of `.win` files. The default report path is
`docs/reports/phase0.md`. Reports and JSON contain file names, metadata, counts and hashes;
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
| 2 | `inspect` only: YYC / empty CODE or unsupported VM bytecode |

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
JAMM_ORIGINAL="$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Cat Goes Fishing/data.win" \
JAMM_MODS_DIR="$HOME/Cat-Goes-Fishing-Mods" \
dotnet test
```

Never commit game archives, extracted game content or personal absolute paths.

## Project layout and contributing

| Path | Purpose |
| --- | --- |
| `src/Jamm.Core/` | Read-only loader, metadata, fingerprints and comparison; no console output |
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
