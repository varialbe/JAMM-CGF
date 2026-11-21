# Task: Harbor Town card + detail page on the mod site (branch `exp/harbor-site`)

Harbor Town 0.1.0 (`examples/modkit/harbor-town`, design `docs/design/harbor-town.md`, report
`docs/reports/harbor-town.md`) is merged and reviewed in-game, but has no card/detail page yet.

## Do
1. Add a `harbor-town` entry to `site/cgf-mods/make-packages.py` exactly like `submarine` (id, name, version from the
   package recipe, description, source_dir, zip_name, icon `assets/harbor-town.svg` – draw a simple flat SVG icon in the
   style of the existing icons in `site/cgf-mods/assets/`).
2. Write `site/cgf-mods/content/harbor-town.json` with the same keys as `content/submarine.json` (cardPackageType,
   cardSummary, summary, controls, apis, assets, saves, compatibility, how, gallery, diagram, changelog, faq). Facts
   only from the design doc, report and package sources (buildings, stages, costs, console command, save format).
3. Gallery: copy a few in-game images into `site/cgf-mods/images/harbor-town/` (downscale to ≤1280 px wide, JPG/PNG,
   small files): `~/jamm-night/runs/harbor-clean-harbor/t60.png` (dock), `~/jamm-night/harbor-town/concept/*-stages.png`
   and 2–3 frames from `~/jamm-night/harbor-town/art-review/solo-final/`. Look at each image first. Never use anything
   from all7/TGE/PWB builds or `~/cgf-files/private-vel`.
4. Run `python3 make-packages.py` and `python3 make-pages.py` in `site/cgf-mods/`, check the generated
   `mods/harbor-town.html` and the index card.
AGENTS.md applies. Foreground only; simple single commands (no `&&`, `;`, pipes, redirections, no `rm`); if denied,
write a small script file and run it. Do not commit; the orchestrator reviews and deploys.
