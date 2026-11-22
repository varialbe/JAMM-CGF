# Task: cgf-mods "Progress" page — live branch tree + optimisation bar chart, built automatically

User request (2026-11-21): on https://varialbe.xyz/cgf-mods/ add
1. a **tree of all active git branches** (graph like `git log --graph`, but readable for non-devs), and
2. a **bar chart of optimisation stages**: where we started, intermediate phases, where we are now.
It must **build automatically**.

## Implementation
- New generator step in `site/cgf-mods/make-pages.py` (or a module it imports, e.g. `make_progress.py`) that writes
  `site/cgf-mods/progress.html` and links it in the site nav. Static HTML + inline SVG only, no external JS/CDN,
  same style.css look, works in light/dark like the rest, mobile width OK.
- **Branch tree**: read from git at build time (`git for-each-ref`, `git log --graph --format=...` or compute
  yourself). "Active" = last commit within 14 days OR not merged into `main`. For each branch: name, last commit
  date, subject (first line), ahead/behind main, merged yes/no. Render as an SVG lane graph (main as trunk, branches
  forking off at their merge-base, merged ones joining back), plus a compact table below. Include remote
  `origin/*` and local branches of the repo the script runs in; dedupe.
  Privacy: never print author e-mails. Skip branches whose name contains `private`, `tge`, `pwb`, `personal`.
- **Optimisation chart**: data from `site/cgf-mods/content/perf-history.json` (schema below; create it with the
  sample data given). Horizontal bars of loop time per tick (ms, lower = better) with FPS equivalent
  (`min(60, 1000/ms)`) and % vs the first stage; group by scenario (series). Show the measurement method note.
- **Automation**: `tools/site/auto-publish.sh` — `git fetch`, regenerate pages in a temp checkout of `origin/main`
  (so local WIP never leaks), and `rsync` to `varialbe@varialbe.xyz:/var/www/html/cgf-mods/` with the same excludes
  as today (`--exclude make-packages.py --exclude make-pages.py --exclude sources`, plus any new generator file),
  only when the generated output changed. Provide a systemd **user** service+timer unit pair in `tools/site/`
  (every 30 min) and a short README section. Do NOT install/enable the timer yourself and do NOT deploy; the
  orchestrator does that after review.
- Tests: extend `tests/Jamm.Core.Tests/SitePackageTests.cs` (or a python test if more natural) so progress.html is
  generated, contains the SVG chart and a branch tree, and has no e-mail addresses.

## perf-history.json schema
```json
{
  "method": "text shown under the chart",
  "series": [
    {"id": "all7", "title": "...", "stages": [
      {"label": "...", "date": "2026-11-21", "loop_ms": 98.0, "note": "..."}
    ]}
  ]
}
```
Use placeholder numbers for now; the orchestrator will fill in real values.

Commit on exp/site-progress, do not merge/push/deploy. Write a short report in docs/tasks/site-progress-report.md.
