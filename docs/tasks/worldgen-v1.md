# Task: Endless Ocean v1 – variety, smooth transition, POIs (package `endless-ocean` 0.2.0)

Branch `exp/worldgen-v1`, worktree `~/jamm-wt/worldgen-v1`. Read AGENTS.md, `docs/tasks/worldgen-v0.md`,
`docs/reports/worldgen-v0.md` (incl. its v1 plan), `docs/modding/chunks.md` and the generator code first.

## Review of v0 (orchestrator + user)
Good v0, but: only one rock type (`CaveRockBig`) and few fish; abrupt cliff where the old world ends; gaps under
the floor so rocks look floating at the bottom edge; little reason to travel far.

## Required (stable in one night beats feature count – do the list top to bottom, the rest goes into the plan)
1. **Smooth transition** at the east edge of the vanilla world: blend the generated floor height into the last
   vanilla floor/rock profile over ≥ 2 chunks (no cliff, no gap, no overlap with vanilla rocks). Check solo and
   with Cave/Verdant/Colour (they place rocks near the east edge) in all-7.
2. **No floating rocks / no gaps:** fill below the surface line down to the world bottom (solid underlay using
   vanilla rock pieces/masks), and make sure collision matches what is drawn at every checkpoint.
3. **Variety from vanilla:** several vanilla rock types (all `CaveRock*` variants, flips/rotations/scales), vanilla
   plants/kelp/decor objects that exist in NewWorld, at least 3 biomes chosen by depth and distance (e.g. sandy
   shallows, rocky slopes, deep trench with lights from vanilla light objects), caves/overhangs where the noise allows
   (but the lure must never get trapped – guarantee a passable corridor).
4. **More and better-fitting fish** per depth band and biome (all suitable vanilla species, rarer/deeper species
   further away), respecting the existing caps (≤ 15 streamed fish per window) and Perf; streamed fish stay excluded
   from saves.
5. **Points of interest:** occasional wrecks/ruins assembled from vanilla parts (rocks, chests, Geiger lights, boat
   sprites as wreck silhouettes – composed at build time, no shipped vanilla pixels), seeded and rare (e.g. one per
   10–20 chunks), with a small reward hook (e.g. a rare fish spawn nearby). Optional if time is short.
6. Keep performance limits, seed-only saving and the floating-origin/sonar/quest/nav/boat-travel items of the v0
   plan as an updated **v2 plan** in the report unless one of them is trivially stable.
7. Package `examples/modkit/endless-ocean` version **0.2.0** (recipe), rebuild site package via
   `site/cgf-mods/make-packages.py` (do not deploy).
8. Tests: unit tests (determinism per seed, transition blend, underlay coverage, caps), vanilla byte-identical,
   full suite green, zero warnings.
9. In-game (VPS `~/jamm-night/bin/run.sh <win> <label> worldgen`, shared game lock; `--showcase` panoramas):
   solo, personal build (perf, hd-lite, console, smooth, silo, weather-pack, endless-ocean) and all-7: no CODE ERROR,
   collision checkpoints OK, instance counts drop when heading back west. Showcase panoramas (transition, each biome,
   a POI, seed comparison) to `~/jamm-night/worldgen-v1/showcase/` (solo captures only for anything public).
10. Report `docs/reports/worldgen-v1.md`. Commit on `exp/worldgen-v1`; do not merge into main or push main.
