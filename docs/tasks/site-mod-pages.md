# Task: Mod detail pages for varialbe.xyz/cgf-mods

Branch `exp/site-mod-pages`, worktree `~/jamm-wt/site-pages`. Static site only: `site/cgf-mods/`.

## Goal (user request, 2026-11-21)
1. Every mod card on `site/cgf-mods/index.html` is **clickable** (whole card, keyboard-accessible; the
   download button keeps working on its own and does not open the detail page).
2. Each mod gets its **own detail page** `site/cgf-mods/mods/<id>.html` (ids from `catalog.json`:
   builtin-perf, builtin-smooth, builtin-hd-lite, builtin-console, example-fish, jamm-silo,
   weather-pack, endless-ocean, example-rod).
3. Where images exist, the detail page shows a **gallery** of them (see image list below).
4. Each detail page has a **detailed technical datasheet**: how the mod works internally.

## Datasheet content (per mod) – must be factually correct
Derive every statement from the repository (source under `src/`, `examples/modkit/<mod>/`,
`site/cgf-mods/sources/`, `docs/modding/*.md`, `docs/*.md`, `docs/reports/*.md`). Do NOT invent
features, numbers or keys. If something is unknown, leave it out.
Per page:
* Header: name, version, package type (ModKit / recipe), default or optional, download button with
  SHA-256 and size (from `catalog.json`), link back to the overview.
* Short "What it does" for players (2–4 sentences), controls/hotkeys/console commands if any.
* **Datasheet table**: package type, manifest file, generator (for recipes), APIs used (Items/Fish/Area/
  Weather/Rod/ModKit sprite recipes …), objects/sprites/scripts added (count from the manifest/build),
  save-file behaviour (what is persisted, where), compatibility notes (works with which mods, conflicts),
  minimum JAMM version if derivable.
* **How it works** section (technical, several paragraphs, may use lists/code snippets): e.g. for the
  Silo: Area API placement in NewWorld, follow-anchor on MissileSilo, opening animation and passage
  blocking, sprites composed at build time from the player's own vanilla sprites (no vanilla pixels
  shipped); Weather: controller object, per-slot forecasts, weather console command, schools,
  sky layers and tiling; Endless Ocean: seeded value-noise floor, 2,048 px chunks streamed around the
  camera, instance caps, seed-only persistence; Perf/Smooth/HD Lite/Console: what the generator patches.
* "Files in the package" list (from the zip contents / source directory).

## Images (copy into `site/cgf-mods/images/<id>/`, optimise)
Source directory (read-only): `~/jamm-night/review-2026-11-20/`
* jamm-silo: `silo-v2/chamber-solo.png`, `silo-v2/opening-sequence-solo.png`, `silo-v2/art-review.png`,
  `silo-v2/opening.gif` (only if ≤ 4 MB after optimisation; otherwise convert to a short MP4/WebM
  `<video>` from `opening.mp4`).
* weather-pack: `weather/art-review.png`, `weather/art-review-gameplay.png`, `weather/fog-wide.png`.
* endless-ocean: `worldgen/vanilla-1..3.png` and a selection (max 4) from `worldgen/showcase/`.
* example-rod: `vel/velrod-shop.png`.
* **Never** use any file with `all7` in its name (contains private third-party mods) and never any
  file from `~/cgf-files/private-vel`.
* Mods without images (builtins, example-fish): no gallery; datasheet only (the SVG icon may be shown large).
* Convert PNG screenshots to WebP or optimised PNG, max 1600 px wide, plus a thumbnail; lazy-load.
  Total added size for the site ≤ 25 MB.

## Design / constraints
* Keep the existing look (colours, fonts, card style in `index.html`); share CSS via one
  `site/cgf-mods/style.css` (move the inline styles there). No external CDNs, no JS frameworks;
  small vanilla JS only if needed (e.g. lightbox). Must work without JS (cards are links).
* Responsive (phone width 360 px, no horizontal scroll), dark background as now, alt texts on images.
* Generate the pages from data so they stay in sync: extend `make-packages.py` (or add
  `make-pages.py`) to render `mods/<id>.html` and the cards from `catalog.json` + a per-mod content
  file `site/cgf-mods/content/<id>.md` or `.json` (hand-written datasheet text). Output must be
  deterministic. `catalog.json` format must stay unchanged (it is read by the cgfmm client).
* The overview cards keep the SHA-256 and download button.
* Package zips must NOT change (do not touch `sources/` or `examples/`).

## Acceptance
* `python3 site/cgf-mods/make-packages.py` (and page generator) runs cleanly; zips' hashes unchanged.
* All links/images resolve locally: run a link checker (e.g. small python script over the html files).
* HTML valid enough (no unclosed tags; check with `python3 -m html.parser`-based script).
* Write a short report `docs/reports/site-mod-pages.md` listing pages, images used and sources of
  each datasheet fact (file paths).
* Do not commit or push; the orchestrator reviews, commits and deploys.
