# Phase 14 — Manager polish

Implemented on `ui/polish-2`; no commits, pushes, git configuration changes, `tools/` changes, or artwork files added.

## Changes

- Added a bulk shared-index API retaining validation, bounded requests, opt-out, conditional caching and offline cached-index fallback. Startup/library reload, settings/base changes and catalog refresh automatically apply compatibility verdicts and Adds facts. Local rows match SHA-256 or provider/file provenance; Browse rows match published catalog identity or resolved provider/file identity. Cached local facts remain available with sharing disabled. Automatic loading never downloads or analyzes mod archives. Facts verified locally against downloaded bytes take precedence over a server preview for different bytes.
- Replaced external Enable/Disable buttons in both Manager layouts with native accessible ToggleSwitch controls inside expanded summaries. Green/grey tracks, white animated knobs, keyboard Space and `Enable <mod>` names are supported. Pending markers appear in row headers and summaries. Switches stage changes in memory; Undo restores saved enablement. Reordering preserves saved enablement while retaining staged switches. Play validates the runner, saves staged selection (creating a default profile if needed), then uses the existing verified build/install or original-restoration path. Advanced Check/Install are disabled while switches are pending, directing the player to Play.
- Added SVG icon resolution and rendering using pinned `Avalonia.Svg.Skia` **11.2.7**, compatible with the existing Avalonia 11.3.23 / SkiaSharp 2.88.9 dependencies. Catalog IDs resolve `litterbox-<id>.svg`; known local names resolve abyss, cave, smooth, hd, hdlite and perf. Missing specific artwork uses `default.svg`; absent artwork retains the geometric placeholder. Browse uses the same resolver. SVG content is copied to output when the parallel artwork branch is merged. No files were created under `Assets/ModIcons/`.
- The normal status panel now shows plain headings such as “Ready to play”, “1 change waiting”, “Working…” and “Installing…” with a single next-step explanation. Profile/build identifiers and processing details remain in Advanced mode and the existing Show details log.

## Validation

- `dotnet build -c Release`: succeeded with **0 warnings, 0 errors**.
- `dotnet test -c Release --no-build`: **358 passed, 7 skipped, 0 failed** (Core: 264 passed / 6 skipped; GUI: 94 passed / 1 skipped). Skips are the opt-in real-game cases.
- Headless rendering and visual inspection: `/tmp/jamm-phase14.png`, showing an expanded card, green switch, pending markers and plain status panel without clipping.
- `git diff --check`: clean. Branch remains `ui/polish-2`.

The pinned SVG dependency was exercised by loading a synthetic SVG under Avalonia headless rendering. No parallel artwork assets or copyrighted game assets are included. Actual catalog artwork will appear after the artwork branch is merged.

Tests cover automatic hash/provenance matching and opt-out, cached local facts, pending/Undo semantics, runner validation before applying, Play installation and disabling/restoring, icon resolution/fallback, status texts, SVG loading, and a headless expanded-switch keyboard/accessibility interaction. Existing immediate-toggle assertions were updated for staged behavior.

NuGet's default cache is read-only in this environment. Checks use `NUGET_PACKAGES=/tmp/jamm-nuget-packages` and `NUGET_HTTP_CACHE_PATH=/tmp/jamm-nuget-http`; existing cached packages are linked into the writable temporary cache. No git or user configuration was changed. Tests use generated synthetic archives; real-game tests remain opt-in.
