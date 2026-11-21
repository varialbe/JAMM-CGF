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

## Round 2 – orchestrator in-game review of round 1 (REJECTED)
Captures of your 0.2.1 build: `~/jamm-night/n21/ocean/pans/jamm_worldgen_wreck.png` and
`jamm_worldgen_pan_3_0.png` (compare with 0.2.0: `~/jamm-night/worldgen-v1/showcase/wreck.png`).
- The underlay now shows large flat light/dark blotches; the cobble outlines of vanilla rock are gone. It looks
  magnified roughly 3–4x: a visible square grid of ~140 px cells, not 480 px.
- Cell borders are clearly visible as hard straight lines (both horizontal and vertical).
Likely causes to check first: texture coordinates (map exactly one 480x480 frame per 480x480 world cell using the
frame's UV rectangle from `sprite_get_uvs`, which is normalized and may include padding/cropping offsets
[left, top, right, bottom, x-offset, y-offset, ...]); texture page scaling (`draw_vertex_texture` expects 0..1 within
the page); whether the synthesized tiles really contain cobble outlines (dump one tile PNG to `.audit/` and look at it
with an image tool); seams in the tile synthesis.
Goal: looks like vanilla cave rock (irregular cobbles with dark outlines) at 1:1 scale, no visible grid. Write a
small headless check that renders a 3x3 cell block from the tile sprites into `.audit/preview.png` (C# test helper or
python3 with the dumped tiles) so you can verify the look yourself before finishing.
