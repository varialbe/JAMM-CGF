# Site progress page implementation report

Implemented the generated Progress page at `site/cgf-mods/progress.html`. `make-pages.py` now adds it to the navigation and delegates its branch snapshot and optimisation chart to `make_progress.py`. The branch list reads local and `origin/*` refs, deduplicates names, filters to branches updated in the last 14 days or not merged into `main`, skips private-name patterns, and redacts email-like metadata. The SVG uses a main trunk, branch lanes, and return paths for merged branches; a table lists dates, subjects, ahead/behind counts, and merge status.

Added sample multi-scenario data to `site/cgf-mods/content/perf-history.json`. The inline SVG chart shows loop milliseconds, the capped FPS equivalent, and percentage change from each scenario's first stage, with a visible method note. Values are explicitly marked as placeholders pending measured data.

Added `tools/site/auto-publish.sh` and user service/timer unit templates. The script fetches, clones `origin/main` into an isolated temporary checkout, regenerates pages, and uses rsync only when generated site files differ. `tools/site/README.md` documents the timer and installation path. The timer was not installed or enabled, and no deployment was performed.

Extended `SitePackageTests` to check the generated SVGs, navigation link, and absence of email addresses. The dependency submodule was empty in this worktree. With a temporary copy of the provided targets file adjusted to this worktree, the CLI Release build succeeded with zero warnings, and the Core test suite passed (643 passed, 17 real-file tests skipped, 660 total). The solution-level build cannot load the absent submodule projects, so the CLI and test projects were built directly.
