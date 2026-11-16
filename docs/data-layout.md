# Managed data and profiles

Management commands select storage in this order: `--data-dir <directory>` anywhere
on the command line, `CGFMM_DATA_DIR`, then `$XDG_DATA_HOME/cgfmm` (default
`~/.local/share/cgfmm`). The directory is created on first use. Managed storage
refuses symbolic links and unsafe mod/profile names. Traditional `diff`, `check`,
`build`, and `restore --game` keep their explicit path behavior.

```text
cgfmm/
  config.json
  mods/<mod-id>/data.win
  mods/<mod-id>/mod.json
  patches/<mod-id>.cgfmm-patch.json
  profiles/<name>.json
  backups/<base-sha256>.win
  builds/<unique-build-id>.win
  installed.json
  proton/<steam-app-id>/              # default Proton compatibility directory
```

A mod ID is its full lowercase archive SHA-256, independent of display name or
source filename. Commands accept that ID, a unique case-sensitive display name,
or a unique source filename stem. Display names must be unique and contain no commas or control characters; spaces
are supported. IDs are safest
for automation. Re-adding identical bytes retains the existing name and metadata.
Changing the configured base invalidates cached patches targeting another hash;
remove affected profiles/mods and re-add against the new base, or select their
original base again. Patches are not silently retargeted.

`mod.json` records `id`, `name`, `sourceFileName`, `sha256`, `addedUtc`,
`baseSha256`, `gen8Timestamp`, `gmVersion`, and `bytecode`. An import stages the
archive, generates its patch against the configured base, and publishes the
library entry only after success. `.zip` imports validate every entry, reject
absolute/parent-traversal paths and symbolic links, and copy only the single
`data.win`, including one inside nested folders. Ambiguous/missing `data.win`
files are errors. Executables and other zip contents are never extracted or run.

`profiles/<name>.json` contains `name`, ordered `mods` (IDs), and `resolutions`.
Later participating mods win overlaps by default; disjoint changes combine.
Profile names allow letters, digits, dots, hyphens, underscores, up to 100
characters. `profile resolve` validates the asset and choice with the same plan
used to build. Mod choices can be library IDs/names; stored choices use patch
display names. `keep-both` has Phase 3's insertion-only restriction. Code hunk keys use
`code/<entry>#<zero-based ancestor line>`; per-hunk choices override entry choices. A mod cannot
be removed while any profile references it. Deleting the active profile clears
the selection; it does not alter the installed game or delete backups.

`config.json` uses these CLI keys (paths are stored absolute):

| CLI key | JSON field | Default / purpose |
| --- | --- | --- |
| `base` | `base` | Required, read-only original archive used for all patches/builds |
| `gameDir` | `gameDir` | Required, complete legacy game folder to install/run |
| `wine.mode` | `wine.mode` | `wine`; alternatively `proton` |
| `wine.prefix` | `wine.prefix` | Optional `WINEPREFIX`; otherwise runner default |
| `wine.binary` | `wine.binary` | `wine` on PATH, or a runner executable path |
| `proton.path` | `proton.path` | Folder containing the executable `proton` script |
| `proton.compatDataPath` | `proton.compatDataPath` | Default `<data-dir>/proton/<app-id>`; a custom existing compatibility directory |
| `proton.clientInstallPath` | `proton.clientInstallPath` | Steam directory; defaults to `STEAM_COMPAT_CLIENT_INSTALL_PATH`, then an existing `~/.steam/steam` or `~/.local/share/Steam` |
| `steam.appId` | `steam.appId` | `343780`; positive application ID |

`activeProfile` is set by `profile use` or a successful apply/play. Keep the base
outside the installed game folder so installing cannot replace the ancestor
needed for subsequent patch/build checks. If they were the same file, restore
first or set `base` to the verified `data.win.cgfmm-original` backup.

`apply` checks sources and resolutions, builds/reloads/verifies a fresh output,
prints decisions, and asks for installation confirmation. Only `y`/`yes` accepts;
EOF or any other answer cancels with exit 1. `--yes` accepts automatically.
Cancellation removes the temporary verified build and leaves the game unchanged. Backups
are made before installing: a permanent exact-base copy under `backups/`, plus
Phase 3's `data.win.cgfmm-original` in the game folder. Both are SHA-256 verified,
never overwritten and never automatically deleted. Temporary builds are removed after successful installation and when the CLI
cancels or installation fails. Core callers must call `Install` or `Discard` after
`Prepare`; `Discard` also releases a build after a failed install. Builds left by
an interrupted process or earlier versions may be removed manually. See [building.md](building.md)
for installation eligibility and crash recovery.

`installed.json` records the profile/patch/resolution/base signature, game directory,
and installed archive hash. Repeated `apply` or `play` skips rebuilding only when
that selection, all mod hashes, the installed archive, backup and Phase 3 marker
still match. Stale installation markers or mismatched backups return false from
the installed-state check; installation still refuses them with a corrective error.
A changed profile triggers another build and confirmation. A changed
installed archive is refused by Phase 3 installation safeguards; restore first.
`restore` restores the configured game, invalidates this record and retains every
backup. It keeps the selected profile so a future `play` can apply it again.

`play` validates the runner and legacy executable before building/installing,
ensures the requested profile is installed, then starts a process without waiting
for the game to finish. It prints the working directory, command and explicit
environment. Wine receives the absolute executable path and optional `WINEPREFIX`.
Proton receives `proton run <absolute executable>`, `STEAM_COMPAT_DATA_PATH` and
`STEAM_COMPAT_CLIENT_INSTALL_PATH`. Both receive `SteamAppId` and `SteamGameId`.
Arguments are passed directly without a shell. The default Proton compatibility
directory is created in guarded storage; custom directories should already exist.
The runners themselves manage their configured prefixes and Steam state.

`list --json`, `profile show <name> --json`, `profile list --json`, and
`config show --json` produce one JSON value on stdout without progress text.
Without `--json`, `config show` prints `key = value` lines using the config-set keys.
List entries expose `baseHashMatches`, `timestampMatches`, `versionMatches`,
`patchCached`, and a readable `compatibility` message alongside mod metadata.
Timestamp agreement alone does not establish archive identity; patches always
bind to the entire configured base SHA-256. A known older mod build warns with
its expected Steam manifest; timestamp differences remain represented in its
patch against the selected common base. Version/YYC incompatibilities fail import.

Library archives, patches, backups, builds and runner prefixes are local user
state. Patches contain game code and strings; do not commit or redistribute them.
The manager does not download legacy builds or distribute game files.

## Desktop profile editing

The GUI uses the same profile JSON, with optional `disabledMods` (an array of library
IDs, default empty). `mods` retains the complete profile order; only IDs absent from
`disabledMods` participate in check/build/play. Empty profiles can be created in the
GUI and must have an enabled mod before building. CLI-created profiles enable every
mod. Changing GUI order/enablement clears resolutions for a fresh conflict review.
Renaming or deleting a profile leaves the installed archive and all backups alone.

When first-run GUI settings default `base` to the game folder's own archive, a
successful GUI install selects the retained, hash-verified manager backup as the
base for future checks and restore. A separately configured original base stays
selected. CLI apply retains its explicit base configuration behavior.
