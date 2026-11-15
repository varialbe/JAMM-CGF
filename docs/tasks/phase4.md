# Task: Phase 4 — mod management, profiles, play

Read `AGENTS.md`, README and docs/building.md. Branch: `experimental`.

## Data layout
`$XDG_DATA_HOME/cgfmm` (default `~/.local/share/cgfmm/`):
```
mods/<mod-id>/data.win        + mod.json (name, source file name, sha256, added date, base sha256 it targets)
patches/<mod-id>.cgfmm-patch.json
backups/                       (copies made before installs; never auto-deleted)
profiles/<name>.json           (ordered mod list + resolve choices)
config.json                    (base data.win path, game dir, wine/proton launch settings, active profile)
```
Paths are configurable via `--data-dir` and `CGFMM_DATA_DIR`. Create on first use; never touch anything outside
except the configured game dir (and only via the Phase 3 install/backup logic).

## Commands
```
cgfmm config show | set <key> <value>        keys: base, gameDir, wine.mode (proton|wine), wine.prefix, wine.binary, proton.path, steam.appId
cgfmm add <file.win | file.zip> [--name <name>]   (zip: find data.win inside, also nested; reject zips with path traversal; no other files executed)
cgfmm list                                    (mods, base compatibility by GEN8 timestamp/hash, patch cached?)
cgfmm remove <mod>
cgfmm profile create <name> --mods a,b,c | list | show <name> | use <name> | delete <name>
cgfmm profile resolve <name> <asset> <mod|keep-both>
cgfmm apply [--profile <name>]                (= check + build + install for the active profile, with confirmation prompt unless --yes)
cgfmm play [--profile <name>]                 (ensure the active profile is installed, then launch the game)
cgfmm restore                                 (Phase 3 restore for the configured game dir)
```
- `add` computes the patch immediately against the configured base and warns if the mod targets a different
  base (GEN8 timestamp / version), naming the expected Steam manifest if known (see docs/legacy-version.md).
- `play`: launch `Cat Goes Fishing.exe` from `gameDir` with the configured runner: `wine` (WINEPREFIX) or
  Proton (`<proton>/proton run`, with STEAM_COMPAT_DATA_PATH / STEAM_COMPAT_CLIENT_INSTALL_PATH set to
  configurable dirs). Don't wait for the game; print the command used. Clear errors when the runner is missing.
  (Cannot be tested on the headless build machine: unit-test the command construction only.)
- Clear, friendly messages; `--json` output for `list`/`profile show`/`config show` (the future GUI will use it).

## Tests
Temp-dir based tests for add (win + zip incl. traversal rejection), list, profiles, config, apply on a copied
legacy game dir (real files, skipped without env vars), play command construction.

## Docs
README: complete command reference, quick start ("from Steam purchase to modded game in 6 steps"), FAQ
(YYC/legacy version, backups, restore). `docs/data-layout.md`.

## Verify before finishing
build zero warnings, tests green. Walk through the quick start on this machine with a temp data dir and a temp
copy of the legacy game folder (excluding `play`). Report the transcript (shortened). Do NOT commit.
