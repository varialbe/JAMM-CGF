# Phase 11b — Browse polish

Implemented in the existing `ui/browse-mods` worktree; no commits, pushes, or git configuration changes.

## Changes

- Browse defaults to hiding previews, missing download URLs, unsupported hosts/pages and unsupported archive extensions. Supported public HTTPS .win/.zip links and recognized Game Jolt project/build/package URLs remain visible without resolving every project over the network.
- A small “Show mods without download” checkbox persists through the existing manager configuration (default false). Search, category, release and sort still compose with this preference.
- A separate “N of M shown” count reflects all active filters against the full catalog; fetch timestamp/offline status remains visible.
- Both catalog refresh and advanced source retry buttons now say exactly “Refresh”.
- Catalog cards and selected details show a small “info” button beside negative/uncertain compatibility symbols. Hover exposes the full reason; click or keyboard activation opens a flyout with focusable, selectable/copyable text. The existing full ModFacts reason, including newlines and failing entry details, is preserved along with analysis provenance.

## Tests

Added coverage for default hiding, supported/unsupported source recognition, counts, combined filtering and preference persistence across view-model recreation. Added shared-facts cases for incompatible, unsure and tool-limitation reasons including a multiline failing-entry detail. Extended the native headless Browse test to check exact Refresh labels, focusability and selectable flyout content; adapted existing selection/filter tests to the new default.

## Validation

- `git diff --check`: passed.
- `dotnet build -c Release`: could not complete in this sandbox. Default MSBuild worker startup encountered denied named-pipe/socket permissions.
- Retried with `-m:1 -nr:false -p:UseSharedCompilation=false`: restore failed with NU1301 because access to api.nuget.org:443 is denied.
- Attempted restore from the local package cache: Microsoft.Data.Sqlite and SQLitePCLRaw.bundle_e_sqlite3 are missing (NU1101). A no-restore Release build and targeted GUI test run remain blocked by these missing packages.
- Build success, zero-warning status and test pass counts are therefore unverified. No game files were used or modified; no tracked submodule sources were changed.

Recognized Game Jolt sources are treated as potential downloads until selected, as before; catalog filtering does not crawl remote project releases or infer availability from transient network failures.
