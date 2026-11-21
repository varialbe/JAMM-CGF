# Task: Harbor Town – separate endgame money-sink mod (design doc + v0.1)

Branch `exp/harbor-town`, worktree `~/jamm-wt/harbor-town`. Own package `examples/modkit/harbor-town`, version
**0.1.0**, optional (never enabled by default), not built into any other mod. Read AGENTS.md, the ModKit/API docs
(`docs/modding/*`), the silo package (area pieces, sprite recipes, hooks) and `docs/tasks/silo-v2-area-api.md` first.

## User idea
"Wo man unendlich viel Geld versenken kann." A harbor town at the dock that the player expands with money. Every
building has **unlimited levels** with exponentially rising cost (e.g. base × 1.6^level) and visibly grows.
* **Lighthouse** – larger light radius at night / in the depth.
* **Aquarium** – shows caught species alive; each level adds tanks (collection motivation).
* **Research station** – small, **logarithmically diminishing** bonuses (sell price, bite rate, lure luck), hard capped
  so it never becomes OP (fair-play guard stays intact).
* **Shipyard** – boat colours/designs (cosmetic).
* **Golden cat statue** – purely cosmetic, bigger/more golden per level; prestige display.
* **Expeditions** into Endless Ocean: fuel/equipment costs scale with distance/depth; rare fish / POIs as reward.
* **Donation milestones** (1M, 1B, 1T, 1Qa …) unlock hats, titles, decor.
* Costs shown in the compact money format (3.4M, 9.5T – `jamm_money_format` from exp/big-money once merged; until
  then a local formatter with the same rules: <10,000 full number, else one decimal truncated, K/M/B/T/Qa).
* Levels saved per save slot; console command `harbor` for testing (set levels / give money).
* Balancing target: a player with 1T $ has meaningful goals but nothing game-breaking.

## Phase 1 – design doc (DONE by the orchestrator: docs/design/harbor-town.md – review and refine it, keep the numbers unless you find a real problem)
`docs/design/harbor-town.md`: vision, buildings, cost curves + balancing tables up to 1Qa, bonus formulas with caps,
required JAMM APIs (Building/Upgrade API, aquarium display, expedition API – what exists already in Items/Area/Weather
APIs and what must be added), save format, UI (how the player interacts: walk the cat to a building sign at the dock,
menu like the vanilla shops), phase plan v0.1 → v1.0, risks. Include 3–5 concept images (mockups composed from the
player's vanilla sprites at build/dev time, saved only to `~/jamm-night/harbor-town/concept/`, never committed).

## Phase 2 – v0.1 (only what is stable tonight: 2–3 buildings done properly beats everything half-done)
Suggested v0.1: Lighthouse + Golden statue + Aquarium (simple: one tank showing up to N caught species), build menu,
unlimited levels, costs, save/load, console `harbor`. Use/extend the Area API for placement on land near the dock
(check where vanilla buildings are and keep the dock walkable, no collision with the cat's path).

## Art – VERY vanilla-looking (same rules as the custom fish)
Composed at build time from the player's own vanilla sprites (parts, recolours, shapes from vanilla building blocks:
the dock houses, shop buildings, lighthouse-like shapes, rocks, signs). Flat CGF style, same outline width and palette,
no seams, no foreign colours, no AI look, **no shipped vanilla pixels** (sprite recipes / SVG details only, like
`examples/modkit/silo/parts`). Art review rounds: screenshot each building at every growth stage **next to vanilla
buildings at the dock**, critique honestly (outline, palette, perspective, scale vs. the cat), fix, repeat ≥ 2×.
Images to `~/jamm-night/harbor-town/art-review/`.

## Verification
Unit tests (cost curves, caps, save format, package validation), vanilla byte-identical, full suite green, zero
warnings. In-game on the VPS (`~/jamm-night/bin/run.sh`, shared game lock; add a bench scene `harbor` that builds
levels via the console path and takes screenshots of each building at levels 0/1/5/20/100): solo and personal build
(perf, hd-lite, console, smooth, silo, weather-pack, endless-ocean, harbor-town). No CODE ERROR. Build the site package
with `site/cgf-mods/make-packages.py` but do not deploy and do not add it to the site catalog yet.
Report `docs/reports/harbor-town.md`. Commit on `exp/harbor-town`; do not merge into main or push main.
