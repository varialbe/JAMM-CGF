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
  patches/<mod-id>/<ancestor-sha256>-<mod-sha256>.json
  profiles/<name>.json
  backups/<base-sha256>.win
  builds/<unique-build-id>.win
  catalog/litterbox.json              # last successful public catalog snapshot
  download-cache/<sha256>.win         # immutable validated/extracted downloads
  download-cache/<identity-hash>.json # provider/release/file identity -> archive hash
  analysis/<filehash>-<basehash>-1.json # proven counts and compatibility, analyzer v1
  base/<sha256>.win                      # independent ancestor created by automatic setup
  game/                              # optional game copy (GUI defaults to <original>-modded)
  downloads/<random-id>.part          # bounded transfer staging; removed on failure/cancel
  downloads/<random-id>.win or .zip   # validated staging; moved/extracted to cache
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

Imported role metadata is stored in `role`, alongside the optional `packageId`
and package `version`. A declared addon gets a separate patch cache keyed by
both its own archive hash and its base mod's original archive hash. A base
replacement makes JAMM compute the delta against the new archive. The same
cache layout supports legacy addons assigned in profiles.

`profiles/<name>.json` contains `name`, ordered `mods` (IDs), and `resolutions`.
Later participating mods win overlaps by default; disjoint changes combine.
Profile names allow letters, digits, dots, hyphens, underscores, up to 100
characters. `profile resolve` validates the asset and choice with the same plan
used to build. Mod choices can be library IDs/names; stored choices use patch
display names. `keep-both` has Phase 3's insertion-only restriction. Code hunk keys use
`code/<entry>#<zero-based ancestor line>`; per-hunk choices override entry choices. A mod cannot
be removed while any profile references it. Deleting the active profile clears
the selection; it does not alter the installed game or delete backups.

The optional `roles` object maps library IDs to profile overrides. Each value
contains `role` (`base`, `addon` or `none`). An addon override also contains
the base library ID in `base` and can retain the package's `baseVersion` and
`baseSha256` constraints. Addon conflict keys begin with `addons/` so vanilla
and addon conflicts on the same asset can have independent choices.

`config.json` uses these CLI keys (paths are stored absolute):

| CLI key | JSON field | Default / purpose |
| --- | --- | --- |
| `base` | `base` | Required, read-only vanilla ancestor and original archive for backups |
| `gameDir` | `gameDir` | Required, complete legacy game folder to install/run |
| `wine.mode` | `wine.mode` | `wine`; alternatively `proton` or `proton-flatpak` |
| `wine.prefix` | `wine.prefix` | Optional `WINEPREFIX`; otherwise runner default |
| `wine.binary` | `wine.binary` | `wine` on PATH, or a runner executable path |
| `proton.path` | `proton.path` | Folder containing the executable `proton` script |
| `proton.compatDataPath` | `proton.compatDataPath` | Optional; defaults to `<data-dir>/proton/<app-id>` in `proton` mode or `<Flatpak Steam>/steamapps/compatdata/<app-id>` in `proton-flatpak` mode (clear with `""` or blank in Settings) |
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

For Flatpak Steam detection, path overrides and sandbox access, see the
[Flatpak setup](user-guide.md#flatpak-steam).

## Online catalog and downloads

The native Browse mods tab uses the same selected data root (`--data-dir`, then
`CGFMM_DATA_DIR`, then XDG), library import, profile editing and guarded installer.
Catalog reads remain independent from local manager operations; downloads/imports,
profile activation and installation share the existing GUI operation/cancellation guard.
This guard serializes operations within the GUI process, as before; it is not a
cross-process lock for a separately running CLI.

`catalog/litterbox.json` stores `{ mods, fetchedUtc, offlineReason }` with normalized
public metadata. Only successful fetches replace it atomically; offline fallback
preserves the last successful timestamp. The adapter uses Litterbox's public
Supabase query and publishable key observed in `index.html` and `assets/app.js`.
This is a changeable website dependency, not a promised public API. No login,
Litterbox writes, download-click recording, visitor geolocation, or telemetry is used.
The bounded initial catalog fetch accepts at most 500 entries / 4 MiB of metadata.
Images are optional, memory-only: up to 60 list thumbnails within a 4 MiB compressed
budget, each at most 256 KiB and decoded to 72px width; selected details show up to
four 1 MiB images decoded to 600px width. Changing selection releases detail images;
refresh/window close releases thumbnails. Images do not block file actions.

Transfers go into `downloads/` with generated names, never server-supplied paths.
The transport requires public HTTPS on the default port, refuses credentials and
private/local addresses, checks each redirect (maximum six requests), disables
proxies/cookies, and validates/pins public DNS addresses at socket connection time.
Metadata/image requests time out after 45 seconds, connections after 20 seconds,
and a complete file transfer after 15 minutes. User cancellation and timeouts have
different messages. Streaming enforces a 512 MiB download limit even without
Content-Length. Unknown lengths show indeterminate progress. Incomplete files are
removed on error/cancellation; completed staging files are removed after import,
including when a later profile/install step fails. An abrupt process kill can leave
a staging file; it is never automatically imported and can be deleted while idle.

The service checks actual FORM magic/declared length or safe ZIP structure. ZIPs
must have exactly one `data.win`, at most 4096 entries, and at most 512 MiB declared
total expanded content; the selected entry is actually decompressed with cancellation
and a byte limit before import. Traversal, absolute paths and symlinks are refused.
Local ZIP import now applies the same entry count/expanded size limits. Only the
single archive is copied; other contents, including executables, are never run.
Core's full archive/bytecode/base/patch validation still decides import eligibility.
A download passing envelope validation is not yet a verified/imported game archive.

`mod.json` has optional `origins` (defaults to `[]` for existing/manual imports).
Each origin contains `catalog` (`litterbox`), stable `modId`, host `sourcePage`,
`provider` (`gamejolt` or `https`), stable `fileId`, optional `releaseId` and `version`.
Game Jolt file IDs are build IDs selecting the build's `primary_file`; the project,
package, release and file metadata are resolved from the public overview. Signed
file URLs are refreshed by the public read-only build-URL lookup POST for each
attempt and are never cached as provenance. Direct-link file identity is its
published HTTPS URL. Catalog detail pages are derived from the stable Litterbox ID.

Only successful import or exact archive-hash deduplication attaches an origin.
Deduplication retains the existing ID/name and verified patch, appending a missing
origin idempotently. Manual imports with matching names have no online association
until exact downloaded bytes prove it. Distinct bytes keep distinct SHA-256 IDs;
name collisions receive a readable numeric suffix. No update silently replaces a
library archive or changes a profile. Hashes confirm identity, not provenance trust.

Browse distinguishes catalog availability, transfer/import, library membership,
selected-profile enablement and verified installed-profile membership. The last is
shown only when the existing installer can verify the saved profile signature and
installed archive. It does not infer game installation from a successful download.


## Simple browsing and automatic setup (Phase 9b)

`config.json` adds optional `advancedMode` (false) and `confirmedInstallDirectory`
(null). Confirmation is retained only for that exact destination; the installer still
validates the current game, original backups, exact profile signature and build bytes
on every apply. Play skips build/apply when the installed signature still matches.
With all mods disabled, Play restores the verified original before launch.

Selection downloads into the bounded staging area, extracts a validated ZIP's single
archive, and retains bytes by SHA-256 in `download-cache/`. Provider/source/release/file
identities have hashed JSON index names; a cache hit rehashes bytes before reuse.
Signed Game Jolt URLs are never persisted. Analysis is keyed by archive SHA-256,
configured base SHA-256 and analyzer revision. Deleting these two cache directories
while the manager is idle is safe; library/profile state remains independent.
There is currently no automatic cache eviction.

Analysis generates a semantic patch and performs a complete build/reload/verification
into temporary manager storage, deleting its output afterward. Resource additions and
changed code entries come from patch counts. Fish require an actual ancestor named
`Fish`, following parent links with cycle protection; names/descriptions are not hints.
Unprovable boat/catalog counts are omitted. Timestamp differences retain an Unsure
icon even after a successful build. Failures do not import or enable a library mod.
Only the explicit Download action imports through `ModLibrary.Add`, attaches provenance
and enables the result. Its transfer/import and profile writes use the existing
serialized GUI operation guard. Selection analysis is read-only with respect to game,
library and profile inputs and can be cancelled independently.

Steam detection checks default Windows, native Linux, Flatpak, Snap and macOS roots,
reads modern and older `libraryfolders.vdf` path entries, and inspects common game and
legacy depot archive candidates through `GameFileLoader`. Automatic setup copies the
chosen legacy directory to a separate destination (GUI: `<original>-modded`) and retains independent `base/<sha256>.win`.
It refuses an existing game copy and skips filesystem links. The found source is never
written. Steam ownership/download and the configured Wine/Proton runner remain required.


Phase 9c first-run setup defaults the game copy to `<original-folder>-modded`.
The destination is editable in Advanced mode and must be outside the original.
The base is an independent read-only input in `base/<sha256>.win`; source game
files are never changed. Setup creates/selects `default` and chooses `direct`
on Windows or native/Flatpak Proton from detected Steam installations. Add mods
and Download defer their action until setup succeeds; cancelling setup discards it.
