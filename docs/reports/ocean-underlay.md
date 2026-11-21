# Endless Ocean Underlay — Seamless World-Aligned Cobbles (0.2.1)

## Root causes found

In Endless Ocean 0.2.0 (`src/Jamm.Core/Builtins/EndlessOceanSprites.cs`, `Worldgen/*.gml`), two separate mechanisms caused the visible tiling and vertical seams seen in `deep-trench.png`, `wreck.png`, and `sandy-shelf.png`:

1. **Small mirrored tile + bilinear border bleed:** `EndlessOceanSprites` cropped a single 256×256 interior rectangle from `CaveRockBig`, mirrored it horizontally (`Math.Min(x, 511 - x)`), and packed it with `BorderFlags.None` (transparent black outer borders). That created a strict 256 px symmetric motif and dark 1 px horizontal/vertical lines when filtered under `HD Lite` scaling.
2. **Per-strip coordinates and per-strip tinting:** `jmk_endless_ocean_load.gml` passed a per-strip `image_blend` computed from `(layout[i] + layout[i + 1]) / 2`, producing brightness and hue steps at every 512 px boundary. `draw_ground.gml` drew each underlay strip starting at `(left=0, top=0)` anchored to `base_y = max(west, east)` while slope caps drew `draw_self()` with vertical `image_yscale = abs(east - west) / 255`, restarting and squashing the pattern at every strip and along `y = base_y`.

## Changes made

- **Quilted vanilla Wang tiles (`src/Jamm.Core/Builtins/EndlessOceanSprites.cs`):**
  - Extracts opaque interior 132×132 rock patches (with 8 orientations: rotations and flips) from `CaveRockBig` and luminance-compatible `CaveRock*` sprites at build time, keeping the vanilla slate rock palette.
  - Synthesizes 16 seamless `480 × 480` Wang tile variants (`spr_jmk_ocean_underlay_tex`) using minimum-error DP seam cuts across 24 px overlaps around shared corner and edge collars (`H0/H1`, `V0/V1`, `K`).
  - Pads each `480 × 480` frame by 2 px of its exact matching neighbor edge/corner pixels and adjusts TPAG `SourceX/Y/Width/Height` to `480 × 480` so GPU bilinear filtering across cell edges never samples transparent black or mismatched neighbors. All 16 frames pack onto a single `2048 × 2048` texture page.
  - Preserves `spr_jmk_ocean_underlay` (`512 × 256`), `spr_jmk_ocean_slope_down`, `spr_jmk_ocean_slope_up` (`512 × 272`), and their collision masks unchanged.
- **World-aligned rendering & uniform lighting (`Worldgen/*.gml`, `EndlessOceanMod.cs`):**
  - Added `jmk_endless_ocean_underlay_cell.gml` to select each `480 × 480` world cell `(cx, cy)` deterministically from `ocean_seed` via `jamm_chunk_unit` edge hashes.
  - Removed per-strip `depth_fraction` / `tint` from `jmk_endless_ocean_load.gml` and `jmk_endless_ocean_ground.gml`, and recorded `slope_west` / `slope_east` on slope caps.
  - Updated `EndlessOceanMod.cs` `RoomStart` to cache `sprite_get_uvs` and `sprite_get_texture` for all 16 variants once per room start.
  - Updated `draw_ground.gml` to sample both rectangular underlay strips and clipped slope-cap triangles in absolute `(x, y)` world coordinates using `draw_primitive_begin_texture(pr_trianglelist, ...)` and `draw_vertex_texture_colour(..., c_white, 1)`.

## Draw calls per chunk (before vs. after)

- **Before (0.2.0):** Each visible 512 px strip issued `ceil(visible_height / 256)` `draw_sprite_part_ext` calls (`2–4` at 720p) plus `1` `draw_self()` for the slope cap = `3–5` draw calls per strip (`~12–20` draw calls per 4-strip chunk if fully in view).
- **After (0.2.1):** All 16 variants share one texture page (`1` texture handle), and both the underlay quads and clipped slope-cap triangles are batched per strip into a single `pr_trianglelist`: `1` draw call for `underlay` + `1` draw call for `cap` (`1–2` draw calls per visible strip, `~4–8` per chunk), with zero per-pixel CPU work at runtime.

## Verification

- `node --test tests/worldgen/chunks.test.mjs`: **8 passed, 0 failed** (determinism, seed variation, Wang edge continuity, uniform `image_blend`, world-aligned UVs across strip/chunk boundaries, and slope-cap/underlay vertex+UV matching at `y = base_y`).
- `dotnet build -c Release`: **0 warnings, 0 errors**.
- `dotnet test -c Release --no-build`:
  - `Jamm.Core.Tests`: **605 passed, 0 failed, 16 skipped** (opt-in real-file tests without `JAMM_ORIGINAL` / `JAMM_MODS_DIR`). Includes new tests for Wang tile determinism, palette bounds, byte-for-byte 2 px padding continuity across all matching Wang edges, single texture-page packing, and full GML compilation of all Endless Ocean scripts and events.
  - `Jamm.Gui.Tests`: **130 passed, 0 failed, 1 skipped**.
- Package `examples/modkit/endless-ocean` bumped to version `0.2.1` and rebuilt via `site/cgf-mods/make-packages.py` (`endless-ocean.zip`: 1,599 bytes, SHA-256 `36344f758fc77d928c3cdc9fdd75cecd13570299c6d6c86f9c477070258d0c5f`) and `site/cgf-mods/make-pages.py` (not deployed).
