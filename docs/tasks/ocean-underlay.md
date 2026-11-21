# Task: Endless Ocean underlay without visible tiling and seams (branch `exp/ocean-underlay`)

Endless Ocean 0.2.0 (`examples/modkit/endless-ocean`, report `docs/reports/worldgen-v1.md`) fills below the
generated floor with a solid underlay. In-game captures (`~/jamm-night/worldgen-v1/showcase/deep-trench.png`,
`wreck.png`, `sandy-shelf.png`) show two visual problems:
1. The underlay is one small stone texture repeated in a strict grid, so the same shape repeats every ~150 px.
2. Vertical seams at every chunk/strip boundary (texture restarts, slight brightness jump).

Vanilla CGF rock (CaveRock*, the big cave walls) looks like irregular cobbles with darker outlines and no visible
repetition.

## Do
1. Find where the underlay sprite/texture is composed (build-time sprite recipe) and where it is drawn (GML).
2. Make the pattern world-aligned (sample by absolute x/y, not per strip), so neighbouring strips continue
   seamlessly; remove brightness differences between strips.
3. Break the repetition: larger source area and/or several variants chosen per cell from the seed with
   flips; compose from vanilla rock artwork at build time (no shipped game pixels), keep the vanilla palette.
4. Keep performance: no per-pixel work at runtime; draw cost must not grow notably (document draw calls per
   chunk before/after).
5. Unit tests for determinism and world alignment (e.g. sampled offset of strip k+1 equals end of strip k).
   Package version 0.2.1, rebuild the site package via `site/cgf-mods/make-packages.py` (do not deploy).
6. `dotnet build -c Release` zero warnings, `dotnet test` green. Short report `docs/reports/ocean-underlay.md`.
   You cannot run the game; the orchestrator tests in-game.

## Rules
AGENTS.md applies. Every command in the foreground (no background tasks). Simple single commands (no `&&`, pipes,
redirections, no `rm`). Do not commit; the orchestrator commits.
