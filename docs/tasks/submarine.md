# Task: Submarine – separate mod: buy a sub and dive yourself (design doc + v0.1)

Branch `exp/submarine`, worktree `~/jamm-wt/submarine`. Own package `examples/modkit/submarine`, version **0.1.0**,
optional, not built into other mods. Read AGENTS.md, `docs/modding/*` (Items/Boat API, rods, areas, weather), the
vanilla boat/camera/Lure logic in the legacy base (`~/cgf-files/legacy/2023-08-16_manifest-9039707068725406600/data.win`,
use `tools/Jamm.ModMaker` dump/names) first.

## User idea
An expensive, buyable submarine (good money goal, fits money > 999M) with which the player **dives himself** instead of
only lowering the lure: dive depth, oxygen/battery, headlight (uses the vanilla light system), hull strength as upgrade
levels; fishing from the sub (lure starts at the sub's position) → deep fish / silo / Endless Ocean become reachable.

## Phase 0 – feasibility (write findings into the design doc)
How do vanilla boats work (Boat object, Girl riding, camera/view follow, speed, water surface clamp), how does the
Lure decide its start point and depth, how do light sources work at depth (Geiger lights, lure light, night darkness
surface), what does the JAMM boat API (`jamm_boat_register`) offer. What is the cleanest way to let a vehicle leave the
surface (new object vs. extended boat), and how to keep vanilla code untouched or minimally hooked.

## Phase 1 – design doc `docs/design/submarine.md` (commit first)
Mechanics, controls (keyboard + gamepad as vanilla), oxygen/battery model, hull/pressure, upgrade tiers + prices
(compact display), interaction with fishing, sonar, silo, Endless Ocean, weather, saving; required new JAMM API (e.g. a
documented Vehicle/Dive API other mods can use); risks; phase plan v0.1 → v1.0.

## Phase 2 – v0.1 (stable beats complete): buy in the boat shop + dive/surface + headlight + oxygen meter
+ fishing from the sub (lure spawns at sub position). Upgrades/hull/pressure can be v0.2 if time is short.

## Art – VERY vanilla-looking
Composed at build time from the player's own vanilla boat/object sprites (recipes/SVG details only, no shipped vanilla
pixels), flat CGF style, vanilla outline width + palette, no seams, no AI look. Art review rounds next to vanilla boats
(≥ 2 rounds), images to `~/jamm-night/submarine/art-review/`.

## Verification
Unit tests, vanilla byte-identical, full suite green, zero warnings. In-game on the VPS (`~/jamm-night/bin/run.sh`,
shared game lock; add a bench scene `submarine`: buy via console money, dive to 3 depths, light on, cast, surface;
screenshots + an MP4/GIF of the dive to `~/jamm-night/submarine/`): solo and personal build. No CODE ERROR.
Build the site package with `site/cgf-mods/make-packages.py` but do not deploy.
Report `docs/reports/submarine.md`. Commit on `exp/submarine`; do not merge into main or push main.
