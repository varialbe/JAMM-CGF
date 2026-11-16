# Phase 9b — Simple browsing and automatic setup

Implemented on `ui/browse-mods`, 16 November 2026. No questions or user decisions.
No commits, pushes, git configuration changes, tools changes, submodule source
changes or modifications to found game originals.

## Download failure and regression

Phase 9 resolved Colour Expansion to one file but left `SelectedFile` null.
`CanDownload` required that property and a configured base, so both actions stayed
disabled until the player discovered the explicit release picker and configured
Settings. Download-and-install additionally required an existing selected profile.
There was no automatic first-run setup or default-profile creation. These GUI
preconditions, rather than a broken Game Jolt transfer endpoint, prevented the
normal-player workflow.

The actual Avalonia MainWindow was exercised with the real catalog, real Game Jolt
resolution, a temporary `CGFMM_DATA_DIR=/tmp/jamm-phase9b-live` and the read-only
August 16 legacy base. Automatic selection enabled Download without using the
picker. The live regression also clears the selected file, reproduces the disabled
button condition, restores the automatic choice, and confirms Download imports
and enables it. The first live run created the default profile and imported Colour
Expansion; subsequent runs verified hash deduplication and cached-byte reuse.
`SearchFiltersAndAutomaticSingleFileSelectionDriveButtons` covers the selection
regression deterministically with injected providers.

The live render exposed a second concrete problem: an oversized optional catalog
thumbnail threw `InvalidDataException` outside the optional-image catch, causing a
misleading “Could not load catalog” status even though rows loaded. Thumbnail and
gallery failures now remain optional. A dedicated oversized-thumbnail regression
proves the successful catalog state survives. Transfer progress is also synchronized
so delayed callbacks cannot overwrite Failed/Cancelled with Downloading.

This uses the real window under Avalonia Headless, not a physical desktop mouse
session. The render was inspected locally; native desktop checks remain below.

## Implemented flow and files

- `Browsing/ModFacts.cs`: read-only inspection, semantic diff, complete temporary
  build and reload/semantic verification. Facts cache keys include mod SHA-256,
  base SHA-256 and analyzer revision. Counts come from patch additions/changes;
  fish require parent ancestry to the exact `Fish` object, with cycle protection.
  Counts for boats/catalog categories are omitted because this implementation
  cannot prove those categories. No descriptions or name guesses become facts.
- `Browsing/ModDownloadCache.cs`: existing bounded/cancellable streaming and ZIP
  validation/extraction, SHA-256 archive storage, hashed provider/file/release
  indexes, rehash-before-reuse and deterministic newest-release selection. A
  single supported file is automatic; multiple files in the newest release remain
  an explicit advanced choice. Existing resolver release IDs determine release
  order; files are never selected by arbitrary enumeration order.
- `Management/ModLibrary.cs`: exposes its existing guarded ZIP extraction within
  Core so selection analysis shares import rules.
- `Management/SteamGameDetection.cs`: standard Windows, Linux, Flatpak, Snap and
  macOS Steam roots, old/modern VDF paths, common game and downloaded depot
  candidates, read-only classification, and safe separate-game setup. Setup
  copies into manager `game/` and retains `base/data.win`, skips links, refuses
  existing destinations and rejects storage nested inside the original game.
- `ManagerModels.cs` / `ManagerStore.cs`: backward-compatible persisted advanced
  preference and destination-specific one-time installation confirmation.
- GUI XAML, code-behind and view models: simple cards with proven counts,
  compatibility symbol/color/tooltip and accessible name, Litterbox link and
  Download; selected image gallery; cancellable selection checks; automatic
  import + enable + default profile; green/grey Enable/Disable actions; prominent
  Play. First run offers detected-legacy setup or a copyable Steam depot command.
- Advanced mode restores descriptions, release/file picker, sort/release filters,
  library/profile management, load order, conflicts and existing install actions.
  Later-mod-wins build behavior and explicit conflict choices remain intact.
- Play retains the installer's exact-signature shortcut. Changed selections build
  and verify before installation; approved destination paths retain confirmation.
  Disabling all mods restores the verified original before launch. Every apply
  retains existing game recognition, backup and input/output safeguards.
- README and data-layout documentation describe the new workflow and storage.

Compatibility is green only after a successful verified build with matching source
build/engine information against the exact configured base. Unsupported engines,
missing bytecode or an inapplicable patch are red. Unchecked/failed analysis and
source timestamp differences remain yellow, with the reason in the tooltip.
Analysis alone does not import, enable or install anything.

## Live Colour Expansion result

Public sources: [Litterbox](https://litterbox.fyi/index.html),
[Colour Expansion ID 40](https://litterbox.fyi/mod.html?id=40), and its
[Game Jolt project](https://gamejolt.com/games/cgfCOLexpansion/842548).
The existing public adapters returned 24 catalog entries and selected build
1831326, release 1385965, version 5.0.0, `data.win` without a picker.
The fresh download succeeded and the resulting archive has SHA-256
`49c7bc3226c1aaf7f49aed8ddd9ca31b251e2c509250a2d29032bb77dde0b822`.
These identities are observations, not hardcoded download configuration.

Analysis against manifest **9039707068725406600** proved:

- **42 fish species** (added objects descending from Fish).
- **51 sprites**.
- **53 game objects**, including the fish objects; these are overlapping categories.
- **20 changed game code entries**.

The patch built and verified successfully against the configured August 16 base.
Compatibility deliberately remains **Unsure**: Colour Expansion's GEN8 timestamp
1692057761 identifies its August 15 source, while this base is 1692207312 (August
16). This is a successful build with a documented source-build caveat, not a
claim of gameplay compatibility. Gallery images were loaded and asserted present.
Download enabled the imported library identity; no real game was installed or launched.
The final inspected screenshot is `/tmp/jamm-phase9b-final.png` (not repository data).

## Validation

Required commands use a writable HTTP cache, without disabling NuGet auditing:

```sh
NUGET_HTTP_CACHE_PATH=/tmp/jamm-nuget-http dotnet build -c Release
NUGET_HTTP_CACHE_PATH=/tmp/jamm-nuget-http dotnet test
```

Release build: **0 warnings, 0 errors**.
Core: **236 passed, 5 skipped**. GUI: **49 passed, 1 skipped**.
Overall: **285 passed, 6 skipped, 0 failures**. The real-file opt-in tests skip
normally when their environment is unset. The live-window test is explicitly
opt-in via `JAMM_LIVE_BROWSE=1`; without it its body returns without network work
(one of the 49 reported passes). It was additionally run explicitly and passed
with real content and gallery assertions. VSTest sockets worked in this sandbox.
`git diff --check` is clean.

New synthetic coverage includes fish ancestry/resource counts, exact cache keys,
matching/differing source builds and YYC classification, eight Steam root layouts
with secondary VDF libraries and depot paths, independent setup copies, persisted
advanced preference, ambiguous/newest release choice, default-profile toggles,
installed-signature invalidation and unchanged-install reuse, automatic single
file selection, thumbnail failure isolation, import/enable/dedupe, cancellation,
operation exclusion and existing verified installation/restore tests.

## Limits and remaining desktop checks

- Litterbox/Game Jolt website interfaces remain changeable public dependencies;
  no new provider or private access was added. Unsupported providers need the
  advanced source-page fallback. Ambiguous files require Advanced mode.
- Detection searches standard roots and their VDF libraries, not an exhaustive
  disk scan or Windows registry. Nonstandard Steam root locations can still
  require Settings. macOS native app-bundle resource layouts are not supported;
  downloaded Windows legacy depots are recognized.
- Automatic setup creates the game copy/base, but Wine/Proton runner selection
  can still need Settings. It does not acquire a legacy depot for the user.
- Download and fact caches have no automatic eviction; deleting the two cache
  directories while idle is safe. Transfers retain the existing 512 MiB limits,
  public-network policy, ZIP limits, timeout and cancellation behavior.
- Physical Linux/Windows/macOS interaction, browser/clipboard launching, DPI and
  native screen-reader behavior, runner startup and real gameplay remain desktop
  checks. Only temporary synthetic installation tests wrote game copies here;
  real Colour Expansion validation stopped after verified analysis and import.
