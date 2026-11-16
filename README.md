# JAMM-CGF — Just Another Mod Manager for Cat Goes Fishing

![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![GPL-3.0](https://img.shields.io/badge/license-GPL--3.0-blue)

Cat Goes Fishing mods usually replace the entire `data.win`, so only one can run at a
time. JAMM-CGF aims to compare each mod against the original, identify conflicts and
build one merged `data.win`. Phase 4 provides a local mod library, ordered profiles, conflict choices, verified builds, guarded installation/restore, and Wine/Proton launch commands.

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
| Phase 3 | Done | Build and verify merged archives; install with verified backups and restore |
| Phase 4 | Done | Mod library, configuration, profiles, apply/restore and Wine/Proton launch |
| GUI | Later | Graphical mod management |

See the [Phase 0 findings](docs/reports/phase0.md), [Phase 0 verification](docs/reports/phase0-verification.md),
[Phase 2 verification](docs/reports/phase2-verification.md) and
[Phase 3 verification](docs/reports/phase3-verification.md) and
[Phase 4 verification](docs/reports/phase4-verification.md).
Archive verification passes locally; an in-game desktop test is still required.

## Requirements and build

- Linux and the **.NET 10 SDK**.
- Git with submodule support.
- Your own legal game copy and mod files, stored outside the repository.
- For desktop play: Steam and an installed Wine or Proton runner.

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

## From Steam purchase to a modded game in 6 steps

1. Buy/install Cat Goes Fishing on Steam. Keep your game and mod files outside this checkout.
2. Download the complete legacy **16 August 2023** folder through Steam's console:
   `download_depot 343780 343782 9039707068725406600`.
   Follow [the legacy build guide](docs/legacy-version.md) for download locations and ownership requirements.
3. Keep that folder as your untouched original, and copy the **whole folder**, including
   `Cat Goes Fishing.exe`, DLLs and music, to a separate location for modded play:

   ```sh
   cp -a "$HOME/Games/CGF-legacy-original" "$HOME/Games/CGF-modded"
   ```

4. Build with the instructions above and configure the manager. Here `cgfmm` means
   the built executable (`src/Jamm.Cli/bin/Release/net10.0/cgfmm`), or use
   `dotnet run --project src/Jamm.Cli --` before each command:

   ```sh
   cgfmm config set base "$HOME/Games/CGF-legacy-original/data.win"
   cgfmm config set gameDir "$HOME/Games/CGF-modded"
   cgfmm config set wine.mode wine
   cgfmm config set wine.binary wine
   # Optional isolated Wine prefix:
   cgfmm config set wine.prefix "$HOME/.local/share/cgfmm/wine-prefix"
   ```

5. Add your mods (`.win` or a zip containing one nested `data.win`), create their
   load order, and select the profile. Each import computes its semantic patch:

   ```sh
   cgfmm add "$HOME/Cat-Goes-Fishing-Mods/the-abyss-beta.win" --name Abyss
   cgfmm add "$HOME/Cat-Goes-Fishing-Mods/cave-expansion.win" --name Cave
   cgfmm add "$HOME/Cat-Goes-Fishing-Mods/colour-expansion.win" --name Colour
   cgfmm list
   cgfmm profile create combined --mods Abyss,Cave,Colour
   cgfmm profile use combined
   ```

6. Apply, review the printed conflict choices, and accept installation. Then launch:

   ```sh
   cgfmm apply                  # prompts after the verified build; --yes automates acceptance
   cgfmm play                   # skips rebuilding if this exact profile is still installed
   # Return to the original later:
   cgfmm restore
   ```

   Later participating mods win overlaps by default. `profile resolve` records explicit
   asset choices; static verification still needs an in-game desktop check.

For Proton instead of Wine, configure these existing installation directories:

```sh
cgfmm config set wine.mode proton
cgfmm config set proton.path "$HOME/.steam/steam/steamapps/common/Proton - Experimental"
# Optional override; otherwise use STEAM_COMPAT_CLIENT_INSTALL_PATH or a standard Steam folder:
cgfmm config set proton.clientInstallPath "$HOME/.steam/steam"
# Optional existing directory; default compatibility data stays under the manager data directory:
cgfmm config set proton.compatDataPath /path/to/compatdata/343780
```

## Management command reference

All commands support `--verbose`. Select isolated storage with `--data-dir <dir>`
anywhere on the command line, or `CGFMM_DATA_DIR`; the default is
`$XDG_DATA_HOME/cgfmm` (`~/.local/share/cgfmm`). See [data layout](docs/data-layout.md)
for schemas, compatibility reporting, backups and all launch settings.

| Command | Behavior |
| --- | --- |
| `config show [--json]` | Show configuration |
| `config set <key> <value>` | Set `base`, `gameDir`, `wine.mode`, `wine.prefix`, `wine.binary`, `proton.path`, `proton.compatDataPath`, `proton.clientInstallPath`, or `steam.appId` |
| `add <file.win or file.zip> [--name <name>]` | Copy into the library and immediately compute a patch; warn about source build differences |
| `list [--json]` | Show IDs, names, base hash/timestamp/version compatibility and patch cache validity |
| `remove <mod>` | Remove an unused library mod and its patch; profiles using it must be deleted first |
| `profile create <name> --mods a,b,c` | Save ordered mod IDs/names; later mods win overlaps |
| `profile list [--json]` | List profiles and mark the active selection in text output |
| `profile show <name> [--json]` | Show order and resolution choices |
| `profile use <name>` | Select a profile for apply/play |
| `profile delete <name>` | Delete a profile, clearing the active selection if needed |
| `profile resolve <name> <asset> <mod or keep-both>` | Save an explicit `family/name` or unambiguous asset choice; insertion-only `keep-both` |
| `apply [--profile <name>] [--yes]` | Check/build/verify/install the selected profile, confirming installation unless `--yes` |
| `play [--profile <name>] [--yes]` | Ensure that profile is installed, then launch Wine/Proton without waiting |
| `restore` | Restore the configured game directory and retain all original backups |

Use full IDs or unique case-sensitive names/source stems for `<mod>`. Names containing
spaces must be quoted, including the entire comma-separated `--mods` value.
Management commands return 0 on success, 1 for errors or declined installation.

## Archive and analysis commands

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

Check mods in load order (later participating entries win by default when building):

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

Build, verify, install into a separate copy of the legacy game folder, and restore:

```sh
cgfmm build --base original.win --order abyss.win,cave.win,colour.win --out build/merged.win
cgfmm build --base original.win --order abyss.win,cave.win --dry-run
cgfmm verify build/merged.win
cgfmm build --base original.win --order abyss.win,cave.win --out build/installed.win \
  --resolve resolutions.json --install legacy-game-copy
cgfmm restore --game legacy-game-copy
```

`--order` also accepts patch files, provided the exact mod `.win` payloads are available
beside the patches or in `JAMM_MODS_DIR`. `--out` defaults to `./build/data.win`; a previous output can be replaced. Inputs,
installed archives and original backups are protected. Normal
builds publish only after the temporary archive reloads and passes reference and GML
decompile/compile checks. A dry run compiles in memory and writes nothing.

Resolutions are a JSON object mapping `family/name` (or an unambiguous bare asset name) to a winning mod display name, or
`"keep-both"` for code insertion conflicts, for example
`{"example_object":"cave-expansion","example_code_entry":"keep-both"}`. Later mods
win other overlaps; disjoint changes combine. Every conflict decision is printed.
Installation verifies a permanent `data.win.cgfmm-original` backup before replacement
and accepts only the exact base or a previous build with matching marker hashes.
Restore verifies its copy and retains the backup. See [building details, safety rules
and known limitations](docs/building.md).

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

## FAQ

**Why does my current Steam game not work?** The inspected modern build is YYC;
its code lives in the executable. Known mods need the legacy VM archive **and its
matching legacy executable**. See [legacy-version.md](docs/legacy-version.md).

**Why does Colour warn about its base?** It targets the 15 August 2023 build,
while the common merge base is 16 August. Import reports its expected manifest
and includes the differences against your configured base in the patch. Version
incompatibility or unavailable bytecode is an error.

**Where are my backups?** Installation retains `data.win.cgfmm-original` in the
game directory and an exact-base copy under the manager's `backups/` directory.
Both are verified by SHA-256 before replacement and never automatically deleted.
Keep `base` pointing to an untouched original outside the modded game folder.

**How do I undo the installation?** Run `cgfmm restore`, or
`cgfmm restore --game <legacy game folder>` for the explicit Phase 3 command.
The original is verified and restored; backups stay. A subsequent `play` can
apply the selected profile again. Modified installed archives are refused during
apply; restore first. See [building.md](docs/building.md) for recovery details.

**Does a verified merge guarantee compatible gameplay?** No. Verification reloads,
checks references and decompiles/compiles code. Dynamic resource lookups, runtime
catalog conflicts and inherited texture warnings still need desktop testing.
Launch command construction is tested here; actual Wine/Proton gameplay is not.

## Tests

Synthetic tests run without game files. Opt into real-file tests by setting both
environment variables. Inputs stay read-only; builds and installation tests use temporary directories:

```sh
JAMM_ORIGINAL="$HOME/Games/CatGoesFishing-legacy/2023-08-16_manifest-9039707068725406600/data.win" \
JAMM_MODS_DIR="$HOME/Cat-Goes-Fishing-Mods" \
dotnet test
```

Never commit game archives, extracted game content or personal absolute paths.

## Project layout and contributing

| Path | Purpose |
| --- | --- |
| `src/Jamm.Core/` | Loading, patches, conflict analysis, building, verification and guarded installation; no console output |
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
