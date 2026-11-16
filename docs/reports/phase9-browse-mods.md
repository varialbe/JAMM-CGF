# Phase 9 — Native Browse mods

Implemented in worktree branch `ui/browse-mods`, 16 November 2026. No commits,
pushes, git configuration changes, `tools/` changes, or submodule source changes.
No user decisions were needed.

## Integration and changed files

- `src/Jamm.Core/Browsing/CatalogModels.cs`: public metadata, snapshot, file-selection
  and progress records; small catalog/resolver interfaces.
- `LitterboxCatalogProvider.cs`: maps the currently observed public Supabase query,
  optional relations/fields, previews, screenshots, contributor notes and ratings; atomic timestamped offline cache.
- `GameJoltDownloadResolver.cs`: public project overview → packages/releases/builds →
  supported primary files; fresh build URL lookup per download attempt. Also supports
  explicit direct HTTPS `.win`/`.zip` URLs. Never treats an HTML project as a file.
- `ModHttp.cs`: bounded async HTTPS, explicit redirect validation, public-address
  policy and DNS pinning at connection time, cancellation and timeouts.
- `ModDownloadService.cs`: bounded streaming/progress in the selected manager root,
  generated staging names, FORM/ZIP envelope checks, bounded decompression and cleanup.
- `Management/ManagerModels.cs`, `ModLibrary.cs`: backward-compatible optional
  origins, exact-hash deduplication with idempotent origin attachment, metadata-only
  reads, and bounded/cancellable local ZIP extraction. Existing patch, bytecode,
  base-hash and archive validation remain authoritative.
- `src/Jamm.Gui/MainWindow.axaml`, `.axaml.cs`: Manager/Browse navigation, native
  search/filter/sort/list/details/images/file selection, retry/source links,
  download actions and progress; existing Manager controls and confirmation retained.
- `ViewModels/BrowseViewModel.cs`, `MainViewModel.Browse.cs`, `MainViewModel.cs`:
  independent catalog reads, proven membership, and shared operation serialization.
  Long archive/installed-state checks run off the UI thread.
- `tests/Jamm.Core.Tests/BrowsingTests.cs`,
  `tests/Jamm.Gui.Tests/BrowseViewModelTests.cs`, `HeadlessTests.cs`: deterministic
  HTTP/archive/state/integration tests and native navigation/rendering coverage.
- `README.md`, `docs/data-layout.md`: discovery workflow, storage, limits, provenance,
  compatibility caveats and provider boundaries.

The flow is explicit file selection → streamed download → envelope validation →
existing `ModLibrary.Add` → optional activation in the selected profile → existing
check/build/reload/verify/install workflow. Newly introduced unresolved conflicts
populate the current conflict UI and return to Manager; the user saves choices and
presses Install. No game replacement happens until the original concrete target/
backup-path confirmation. Cancellation/failure after import retains the valid
library entry without claiming that it was installed.

Online identity uses Litterbox ID plus recorded provider/file/release identity and
exact local archive hashes. Manual name matches establish no association. Duplicate
bytes retain the existing name/ID/patch and gain a missing origin only after checks.
Different releases can coexist; adding one never changes a profile implicitly.
Current installed membership is shown only when the existing installer verifies it.

## Public source investigation and actual live result

Inspected these public sources directly with read access:

- [Litterbox index](https://litterbox.fyi/index.html) and
  [shared client](https://litterbox.fyi/assets/app.js): public publishable Supabase
  key and `mods`, `profiles!mods_profiles_fkey`, `mod_categories(categories(...))`.
  The detail page additionally establishes public `mod_screenshots` and `mod_credits`
  relations, which supply ordered gallery images and contributor notes. The
  expanded query was live-checked: 24 mods, 16 screenshots and 27 credit entries.
  No private/admin key, authentication, writes, click counters, location lookup,
  comments, reviews or tracking are used. Displayed click statistics are not
  represented as completed downloads.
- [Colour Expansion catalog detail](https://litterbox.fyi/mod.html?id=40) and
  [Game Jolt project](https://gamejolt.com/games/cgfCOLexpansion/842548).
- Game Jolt's own client source at inspection commit
  `db91c7a4430f9e6bcccb405cddf8a415fb006e10`:
  [overview route](https://github.com/gamejolt/gamejolt/blob/db91c7a4430f9e6bcccb405cddf8a415fb006e10/src/app/views/discover/games/view/overview/RouteDiscoverGamesViewOverview.vue)
  establishes `/web/discover/games/overview/{project}`;
  [build model](https://github.com/gamejolt/gamejolt/blob/db91c7a4430f9e6bcccb405cddf8a415fb006e10/src/_common/game/build/build.model.ts)
  establishes the read-only URL lookup POST
  `/web/discover/games/builds/get-download-url/{build}` with `forceDownload: true`.
  These are website implementation dependencies, not invented or guaranteed APIs.

A separate temporary console harness used the actual new production Core services
(including public DNS/redirect checks), with staging outside this repository:

1. Fetched **24** live Litterbox entries, without offline fallback.
2. Resolved catalog ID **40** to Game Jolt package **987130**, release **1385965**,
   build **1831326**, version **5.0.0**, primary file **data.win**.
3. Obtained a fresh signed HTTPS URL on `download.gamejolt.net`.
4. Downloaded **56,770,470 bytes**, and validated FORM content/declared length.
5. Computed SHA-256
   `49c7bc3226c1aaf7f49aed8ddd9ca31b251e2c509250a2d29032bb77dde0b822`
   for identification only, then deleted the transfer.

Thus the requested example works directly inside the service pipeline, without a
browser/login. No version, build ID, size, signed URL or hash above is hardcoded
into production download configuration. Live download was separate from normal
unit tests, and did not import into or replace any real game installation.

Direct HTTPS `.win`/ZIP handling is covered by synthetic HTTP/content tests;
the live Game Jolt file transfer also proves the concrete HTTPS transfer path.
No other catalog host was live-downloaded. Game Jolt project links are the
supported resolver shape; current catalog entries pointing to `/get/build`,
dashboard URLs, and itch.io receive an honest Open source page fallback.
Private/paid files and executable installers are not bypassed or executed.

## Validation

Final required commands (only a writable NuGet HTTP cache environment override):

```sh
NUGET_HTTP_CACHE_PATH=/tmp/jamm-nuget-http dotnet build -c Release
NUGET_HTTP_CACHE_PATH=/tmp/jamm-nuget-http dotnet test
```

- Release build: **0 warnings, 0 errors**.
- Core tests: **236 passed, 5 skipped**, 241 total.
- GUI/ViewModel/headless tests: **34 passed, 1 skipped**, 35 total.
- Overall: **270 passed, 6 skipped, 0 failed**. Real-file environment variables
  were unset; the six existing opt-in game-file tests skipped normally.
- `git diff --check`: clean.
- VSTest sockets worked in this sandbox. The test runner emitted its existing
  informational fallback about `VSTEST_ARTIFACTS_DIRECTORY`; no test was blocked.
- Initial restore failed because the default NuGet HTTP cache was read-only;
  redirecting that cache to `/tmp` allowed the normal required commands to pass.
  An intermediate build also tested with NuGet auditing disabled; the final
  commands above did not disable auditing.

New deterministic coverage includes optional catalog fields and previews;
package/release/file mapping and multiple selections; unsupported sources;
fresh signed URLs; public/private addresses, redirects and credential stripping;
HTML, incomplete files, advertised/actual size limits, transport failure/timeouts;
cancellation cleanup; ZIP traversal/backslashes/absolute paths/symlinks, missing/
multiple data.win and expansion limits; unknown-size progress; cached offline
snapshots; older origin-free JSON; manual-import deduplication and provenance;
new-release coexistence; add-only/profile activation; full synthetic verified
install/confirmation/backup/restore; conflict deferral; busy-state exclusion of
local import/restore/install/duplicate downloads; and offline catalog isolation.

The native Browse tab was rendered with Avalonia Headless at 1320×860 using
invented metadata, no game assets and no private paths. Inspected the screenshot
outside the repository. Search/filters, scrollable text/details and fixed source/
file/action controls are visible; the Manager navigation and install bar remain.
The headless test verifies explicit file selection drives button enablement.
To regenerate a neutral image locally:

```sh
JAMM_BROWSE_SCREENSHOT=/tmp/jamm-browse.png dotnet test tests/Jamm.Gui.Tests \
  --filter FullyQualifiedName~BrowseTabUsesNativeControls
```

## External limits and remaining desktop checks

- Litterbox and Game Jolt website schemas/endpoints can change. Errors offer retry
  and source links; a catalog outage does not disable local mod management.
- This first adapter bounds the catalog to 500 records / 4 MiB. Images are optional
  and cached only in bounded memory; unsupported/oversized images may be omitted.
- Limits: 512 MiB compressed download/expanded ZIP data, 4096 ZIP entries, six HTTP
  requests per redirect chain; 20-second connection, 45-second metadata/image and
  15-minute transfer timeouts. Downloads respect the chosen manager data root.
- Serialization is the existing in-process GUI operations model. Running a
  separate CLI against the same root concurrently is not a new supported workflow.
- An abrupt process kill may leave inert staging files, which can be deleted
  while idle; normal errors/cancellation clean them automatically.
- Native browser launching, real mouse/keyboard interactions, long author texts,
  missing/large images, dark theme, smaller/high-DPI windows and Windows behavior
  still need desktop checks. The native headless navigation/render smoke passes.
- No configured legacy game/base was available for a real downloaded-mod import,
  real-game install, Wine/Proton launch or gameplay check. Synthetic archives
  exercised the complete installation and restoration path. Local Core validation
  still determines compatibility; neither the catalog badge nor the live successful
  transfer is a claim that Colour Expansion matches a user's chosen base.
