# Endless Ocean Underlay — Seamless World-Aligned Cobbles (0.2.1)

## Root causes found

### Round 1 issues (vs. 0.2.0)
In Endless Ocean 0.2.0 (`src/Jamm.Core/Builtins/EndlessOceanSprites.cs`, `Worldgen/*.gml`), two mechanisms caused the visible tiling and vertical seams:
1. **Small mirrored tile + bilinear border bleed:** `EndlessOceanSprites` cropped one 256×256 rectangle from `CaveRockBig`, mirrored it horizontally, and packed it with `BorderFlags.None`.
2. **Per-strip coordinates and tinting:** `jmk_endless_ocean_load.gml` passed a per-strip `image_blend`, and `draw_ground.gml` reset local `(u, v)` per strip and per slope cap.

### Round 2 findings (post in-game review of Round 1)
Inspecting the Round 1 captures (`jamm_worldgen_wreck.png`, `jamm_worldgen_pan_3_0.png`) and built archives (`solo.win`, `showcase.win`) revealed two additional root causes:
1. **Double UV mapping in GameMaker Studio 1.4 + 2-page spill:** In GMS 1.4 (`Build 9999`), `draw_primitive_begin_texture(pr_trianglelist, sprite_get_texture(spr, v))` with `draw_vertex_texture_colour` already maps `(0, 0)..(1, 1)` into frame `v`'s `TPAG` rectangle. Feeding `sprite_get_uvs` (`[SrcX/PageW, SrcY/PageH, ...]`) into `draw_vertex_texture_colour` applied the page-UV transform twice, zooming each `480×480` cell onto a `~112×112` interior sub-crop (`~4.3×` magnification) and creating hard rectangular seams along every cell border. In addition, packing 16 `484×484` tiles alongside `spr_jmk_ocean_wreck` and the ground sprites caused frames 14 and 15 to spill onto a second `1024×2048` texture page.
2. **Oversized cobble scale & DP patch cuts:** `CaveRockBig` (`1024×1024`) has `~200–350 px` cobbles, so `132×132` DP-quilted patches frequently landed inside flat cobble interiors or severed cobbles with straight cuts, while luminance differences (`±3` RGB) between `CaveRock*` sprites produced faint rectangular blocks.

## Changes made

- **Whole-cobble Wang synthesis (`src/Jamm.Core/Builtins/EndlessOceanSprites.cs`):**
  - Samples the exact dark outline and light cobble colors directly from `CaveRockBig` at build time (zero hardcoded game colors or shipped game pixels).
  - Extracts whole connected-component cobbles (with anti-aliased fringes) from `CaveRockBig` and luminance-compatible `CaveRock*` sprites, scaled to natural seafloor cobble proportions (`<= 150 px`).
  - Synthesizes 16 seamless `480×480` Wang tiles (`H0/H1`, `V0/V1`, shared corner cluster) by stamping whole cobbles with a `9 px` minimum dark mortar gap—no severed cobbles or flat patch seams.
  - Packs all 16 variants (`4×4` grid of `512×512` slots, each `480×480` cell centered with `16 px` exact neighbor Wang padding) onto a single dedicated `2048×2048` `spr_jmk_ocean_underlay_tex` page (`SourceX = 0, SourceY = 0, SourceWidth = 2048, SourceHeight = 2048`).
- **Identity-safe atlas UVs & single-batch rendering (`EndlessOceanMod.cs`, `Worldgen/draw_ground.gml`):**
  - `RoomStart` maps each variant `0..15` inside the single `2048×2048` atlas (`atlas_uvs = [0, 0, 1, 1]`), eliminating double-UV remapping whether the runner applies `TPAG` UVs or passes raw page coordinates.
  - All 16 variants share one texture handle (`ocean_tex`), so each underlay strip and slope cap draws in a single `pr_trianglelist` batch without mid-strip texture switches.

## Draw calls per chunk (before vs. after)

- **Before (0.2.0):** `ceil(visible_height / 256)` `draw_sprite_part_ext` calls + `1` `draw_self()` per visible strip (`3–5` draw calls per strip, `~12–20` per 4-strip chunk).
- **After (0.2.1):** Strictly `1` draw call for `underlay` + `1` draw call for `cap` per visible strip (`1–2` per strip, `~4–8` per chunk), with zero per-pixel CPU work at runtime and zero texture switches across cells.

## Verification

- Headless preview rendered from `EndlessOceanMod.Apply` on the legacy archive into `.audit/preview.png` (`3×3` cells) and `.audit/preview_slope.png` (`3×512 px` sloped strips across cell boundaries), visually verified for 1:1 vanilla cobble look and zero seams/grid.
- `dotnet build -c Release`: **0 warnings, 0 errors**.
- `dotnet test -c Release --no-build`:
  - `Jamm.Core.Tests`: **605 passed, 0 failed, 16 skipped** (opt-in real-file tests when `JAMM_ORIGINAL` / `JAMM_MODS_DIR` are unset).
  - `Jamm.Gui.Tests`: **130 passed, 0 failed, 1 skipped**.
- `node --test tests/worldgen/chunks.test.mjs`: **8 passed, 0 failed**.
- Site packages and pages rebuilt via `site/cgf-mods/make-packages.py` and `site/cgf-mods/make-pages.py` (not deployed).
