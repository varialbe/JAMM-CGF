# CLI reference

All commands support `--verbose`. Select isolated storage with `--data-dir <dir>`
anywhere on the command line, or `CGFMM_DATA_DIR`; the default is
`$XDG_DATA_HOME/cgfmm` (`~/.local/share/cgfmm`). See [data layout](data-layout.md)
for schemas, compatibility reporting, backups and all launch settings.

| Command | Behavior |
| --- | --- |
| `config show [--json]` | Show configuration |
| `config set <key> <value>` | Set `base`, `gameDir`, `wine.mode`, `wine.prefix`, `wine.binary`, `proton.path`, `proton.compatDataPath`, `proton.clientInstallPath`, or `steam.appId` |
| `add <file.win, file.zip, or modkit dir> [--name <name>]` | Copy or build into the library. Declared addons get their patch when JAMM has the selected base. |
| `builtin list` | List built-in mods, hotkeys, and console commands |
| `builtin add <perf\|smooth\|hd-lite\|console>` | Generate and import a built-in JAMM enhancement mod |
| `modkit new <dir> [--template fish\|upgrade\|boat\|hat]` | Scaffold a starter ModKit folder package (`jamm-mod.json` + PNG + hook) |
| `modkit check <dir>` | Validate `jamm-mod.json`, PNG headers/frames, and hook paths without building |
| `modkit build <dir> [--base <data.win>] [--out <file.win>] [--no-repack]` | Compile a ModKit folder onto the legacy base (defaults to `config.base` and `<dir>/<id>.win`) |
| `check <mod-or-patch> [...] --base <data.win>` | Check conflicts and inspect legacy boat and rod unlock paths |
| `list [--json]` | Show IDs, names, base hash/timestamp/version compatibility and patch cache validity |
| `remove <mod>` | Remove an unused library mod and its patch; profiles using it must be deleted first |
| `profile create <name> --mods a,b,c` | Save ordered mod IDs/names; later mods win overlaps |
| `profile list [--json]` | List profiles and mark the active selection in text output |
| `profile show <name> [--json]` | Show order, enablement, performance settings, and resolution choices |
| `profile use <name>` | Select a profile for apply/play |
| `profile delete <name>` | Delete a profile, clearing the active selection if needed |
| `profile resolve <name> <asset> <mod or keep-both>` | Save an explicit `family/name` or unambiguous asset choice; insertion-only `keep-both` |
| `profile repack <name> on\|off` | Enable or disable texture repacking for builds of this profile |
| `profile bigmoney <name> on\|off` | Allow money above $999,999,999 with compact display (`$3.4M`, `$9.5T`) in modded builds. Defaults to on. Use `build --no-big-money` for one-off `--base` builds. |
| `profile fairplay <name> on\|off` | Guard clear debug stats in the profile's modded builds. Defaults to on. Use `build --no-fair-play` with `--base` for a direct build. |
| `profile valuecap <name> <n\|0>` | Set the profile's fish sale cap. Defaults to 100,000,000. Use 0 to disable it. Use `build --fish-value-cap <n>` with `--base` for a direct build. |
| `profile base <name> <mod>` | Assign the base role to a mod in the profile |
| `profile addon <name> <mod> <base>` | Assign the addon role and select its base library mod |
| `profile role <name> <mod> none` | Assign the vanilla role, overriding any package declaration |
| `check --profile <name> [--strict] [--json <file>] [--details <asset>]` | Check the profile's enabled groups against their respective ancestors |
| `build --profile <name> [--strict] [--out <merged.win>] [--no-repack] [--dry-run]` | Build the profile with saved choices and write a verified archive |
| `profile preset <name> weak-pc\|default` | Apply Weak PC or restore the previous profile settings |
| `profile builtin <name> <id> on\|off` | Enable or disable a bundled mod in the profile |
| `profile perf <name> mode\|grid on\|off` | Set the startup default for F8 scheduling or the fish grid |
| `profile perf <name> fish-micro on\|off` | Opt in to the experimental post-merge Fish micro-optimisation (off by default) |
| `profile perf <name> interval <ticks>` | Set the default distant-fish update interval from 1 to 60 ticks |
| `profile unlockfix <profile> <mod> on\|off` | Opt a mod into legacy boat and rod shop repair for this profile. Defaults to off. |
| `apply [--profile <name>] [--yes]` | Check/build/verify/install the selected profile, confirming installation unless `--yes` |
| `play [--profile <name>] [--yes]` | Ensure that profile is installed, then launch Wine/Proton without waiting |
| `restore` | Restore the configured game directory and retain all original backups |

Use full IDs or unique case-sensitive names/source stems for `<mod>`. Names containing
spaces must be quoted, including the entire comma-separated `--mods` value.
Management commands return 0 on success, 1 for errors or declined installation.
Profile builds sanitize harmful author performance changes by default, while direct
`build --base --order` keeps the mod archives' changes unfiltered. The GUI's Advanced mode
has a per-mod option to keep every author performance edit, including harmful timing and
gameplay changes normally stripped by the sanitizer; see [author performance changes](user-guide.md#author-performance-changes).

For an overhaul with a legacy addon, add both archives and assign their roles.
Package declarations supply the default roles and can make the two assignment
commands unnecessary.

```sh
cgfmm profile create overhaul-pack --mods Overhaul,ReefAddon
cgfmm profile base overhaul-pack Overhaul
cgfmm profile addon overhaul-pack ReefAddon Overhaul
cgfmm check --profile overhaul-pack --strict
cgfmm build --profile overhaul-pack --out build/overhaul-pack.win
```

The profile must have exactly one enabled base for its enabled addons. JAMM
checks addon conflicts against the original archive from the base mod's library
entry. Use `--strict` to fail on a declared base version or SHA-256 mismatch.
Mixed profiles warn with `CGF-BASE-001` when an enabled mod targets vanilla.
Use the displayed `addons/<asset>` with `check --details`. For `profile resolve`,
prefix addon choices with `addons/`, for example
`addons/code/<entry>#<zero-based ancestor line>`. Use `apply --profile <name>`
to install the profile with a vanilla backup. A profile dry run can use temporary
files in the manager's builds directory and leaves the requested output path alone.

`check` reports `CGF-UNLOCK-001` for boats and `CGF-UNLOCK-002` for rods when it finds no unlock path. The finding names the item, slot, and source tables or events. Use `profile unlockfix` to make eligible items buyable on the next build. The build lists each repaired item and its price.

`wine.mode` accepts `wine`, `proton`, or `proton-flatpak`. For Flatpak Steam, start
Steam first; see the [Flatpak Steam setup](user-guide.md#flatpak-steam) for defaults
and sandbox-visible game and compatibility directories. Leave `proton.compatDataPath`
unset for the active runner's default, or clear an existing override with
`cgfmm config set proton.compatDataPath ""`.

## ModKit commands

Create, validate, build, and import folder-based ModKit packages (`jamm-mod.json` + PNGs + optional `.gml` hooks):

```sh
cgfmm modkit new my-reef-fish --template fish
cgfmm modkit check my-reef-fish
cgfmm modkit build my-reef-fish --out my-reef-fish.win
cgfmm add my-reef-fish
```

`modkit new` supports `--template fish`, `upgrade`, `boat`, or `hat` (default `fish`). `modkit build` opens the supplied `--base`, the addon's declared library base, or `config.base` read-only. It compiles and verifies the new `.win` and writes to `--out` (default `<dir>/<id>.win`). `cgfmm add <dir>` builds using the same metadata and imports the mod directly into the library. See the [ModKit getting-started guide](modding/getting-started.md) and [ModKit reference](modding/modkit-reference.md).

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
payloads are content hashes referencing the mod archive. See [patch format](patch-format.md)
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
limitations](conflicts.md). Checking does not build or install a merged archive.

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

Any build with an enabled mod repacks the legacy texture atlas by default. This
includes single-mod, built-in-only, ModKit and JSON patch builds. The
`CGF-TEX-001` decision reports the page count and estimated uncompressed texture
memory before and after repacking. Pass `--no-repack` to `cgfmm build` or
`cgfmm modkit build`, or run `cgfmm profile repack <name> off` for a saved profile
setting. Omit `--order` from `cgfmm build` to copy the original archive byte for byte.

Resolutions are a JSON object mapping `family/name` (or an unambiguous bare asset name) to a winning mod display name, or
`"keep-both"` for code insertion conflicts, for example
`{"example_object":"cave-expansion","example_code_entry":"keep-both"}`. Later mods
win other overlaps; disjoint changes combine. Every conflict decision is printed.
Legacy upgrade slot collisions may offer a group choice such as
`{"code/upgrade-slot[18]":"Alpha"}` when every claimant adds that ID with a distinct
literal name. This selects that mod's version in each listed conflicting code hunk.
When every claimant also changes the shared vanilla `spr_upgradeicons` sprite, the
choice selects the complete sprite asset, including unrelated frames. Save code and
menu positions are not inferred as slot identities. A saved group choice is ignored
while that group is absent from the enabled profile, then becomes active again if the
same group returns.
Installation verifies a permanent `data.win.cgfmm-original` backup before replacement
and accepts only the exact base or a previous build with matching marker hashes.
Restore verifies its copy and retains the backup. See [building details, safety rules
and known limitations](building.md).

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

