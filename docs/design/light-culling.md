# Lighting-System Culling Design (`LightCulling`)

Branch: `exp/light-culling`

## 1. Decompilation Findings

### 1.1 `LightingSystem` (`data.win`, personal, TGE/PWB, all-7)

- **`LightingSystem_Create_0`** (`depth = -19.5`): Creates `light_surface` (`view_wview[0] x view_hview[0]`) and `mini_surface` (`100 x 100`), initializes thunder/lightning state (`day_coefficient`, `overcast_coefficient`, `lightning_flash`, `ghost_is_visible`).
- **`LightingSystem_Step_0`**: Runs storm/thunder probability rolls and decays `lightning_flash`.
- **`LightingSystem_Draw_0`**:
  - On `view_current == 0`: sets `light_surface` target, draws depth-gradient darkness (`color_from_depth`), switches to `bm_subtract`, and executes:
    ```gml
    with (AbstractLight)
    {
        if (visible)
        {
            x_original = x;
            y_original = y;
            x = x_original - view_xview[0];
            y = y_original - view_yview[0];
            draw_self();
            x = x_original;
            y = y_original;
        }
    }
    ```
    Then applies `lightning_flash`, resets target, and draws `light_surface` onto the world with `bm_subtract`.
  - On `view_current == 1` (minimap when `view_visible[1]` is active): sets `mini_surface` target (`100x100`), draws depth gradient, switches to `bm_subtract`, and runs a second `with (AbstractLight)` pass scaled by `0.1666`.
  - On `view_current == 0 || view_current == 1`: draws Torby ghost + additive `lightning_flash` overlay when `lightning_flash > 0.3`.
  - Note: TGE/all-7 modifies `LightingSystem_Draw_0` to resize `light_surface` dynamically to `(ceil(view_wview[0]) + 4) x (ceil(view_hview[0]) + 4)` with `+ 2` draw offset.

### 1.2 `AbstractLight` Hierarchy & Scale Conventions

- **Vanilla (`data.win`)**: **26** `AbstractLight` objects (21 using `image_scale` / `image_xscale`, 5 inheriting `Light`'s `special_scale`), **0** placed in `NewWorld` (all spawned dynamically by fish, plants, and `Lightbulb`).
  - `AbstractLight_Draw_0` (`depth = -20`): when `view_current == 0`, draws `draw_self()` in `bm_add` with `add_blend`, then resets `image_blend = sub_blend`.
  - `Light_Draw_0` (`Light` only): scales down to `special_scale` (`0.15` by default) for the additive highlight, then restores `image_xscale = image_scale; image_yscale = image_scale;`.
  - `Light2_Draw_0`, `BulbPlantLight_Draw_0`, `ChainPlantBloom_Draw_0`, `ChainPlantSpecularLight_Draw_0`: skip `draw_self()` in `Draw_0` and only reset `image_xscale`/`image_yscale`/`image_blend` before `LightingSystem_Draw_0` subtracts them.
  - `CrystalfishLight_Step_0`: sets `visible = instance_exists(Lightbulb)` every step.
- **Personal (`own-mods-2026-11-22c.win`)**: **26** `AbstractLight` objects, **4** placed in `NewWorld` (`GeigerLight`), plus fish/plant/lure lights. With Endless Ocean, biome 2 and wreck chunks also spawn `Light` instances (`image_scale = 2.8..3.3, special_scale = 1.4..1.7`). With Harbor Town, `Lightbulb.light` dynamically scales `image_scale` and `special_scale` up to `+40%`. With Submarine, `sub_lamp` (`Light` with `sprite_index = jmk_submarine_recipe_beam`, `image_angle = sub_light_angle`) and `sub_halo` (`Light`) toggle `visible` dynamically.
- **All-7 (`all7.win`)**: **60** `AbstractLight` objects (55 mode-1 `image_scale`, 5 mode-2 `special_scale`), **53** placed in `NewWorld` (`Lightstuff1` 33, `Lightstuff2yellow` 16, `GeigerLight` 4), plus ~1,000 dynamically spawned lights from fish, plants, crystals (`CrystalLight1..5`, `CrystalSaltLight..CrystalRubyBrightLight`), fireflies (`FireflyLight`), minebombs (`Minebombheadlight`), `Boat8Light` (`2000x1200` origin `(0,0)`), `LotusLight`, `GlareLight`, `LanternLightstuff2`, `AngleryLight`, `Parastone_Light`, `GlowcrystalLight`, `RadianiteGlow`, and `crystallight3/4/yel`.

## 2. Measured Baseline Cost (`--profile-scripts`)

Captured with `jamm-modmaker bench --profile --profile-scripts` (renders/blend-mode calls carry `[render-affected]` overhead):

| Archive & Scene | Ticks | `LightingSystem_Draw_0` (`µs/tick`, share) | `AbstractLight_Draw_0` calls/tick (`µs/tick`) | Other light `Draw_0` calls/tick (`µs/tick`) | Light `Step` calls/tick (`µs/tick`) |
|---|---:|---:|---:|---:|---:|
| Vanilla `dock` | 85 | 2,320 µs (1%) | 142.3 (`311 µs`) | `ChainPlantSpecularLight` 15, `BulbPlantLight` 6, `Light2` 4 (`44 µs`) | `CrystalfishLight` 1 (`3 µs`) |
| Vanilla `deepwater` | 38 | 5,650 µs (1%) | 162.5 (`380 µs`) | `Light` 1 (`22,264 µs`, 6%), `ChainPlantSpecularLight` 15, `BulbPlantLight` 6, `Light2` 4 (`54 µs`) | `CrystalfishLight` 1 (`5 µs`) |
| Personal `dock` | 29 | 5,637 µs (1%) | 176.0 (`663 µs`) | `ChainPlantSpecularLight` 15, `BulbPlantLight` 6, `Light2` 4 (`88 µs`) | `CrystalfishLight` 1 (`5 µs`) |
| Personal `deepwater` | 19 | 31,210 µs (4%) | 176.0 (`1,892 µs`) | `Light` 1 (`37,726 µs`, 5%), `ChainPlantSpecularLight` 15, `BulbPlantLight` 6, `Light2` 4 (`97 µs`) | `CrystalfishLight` 1 (`5 µs`) |
| All-7 `dock` | 21 | 4,961 µs (<1%) | 904.0 (`8,169 µs`, 1%) | `Light2` 131 (`1,102 µs`), `BulbPlantLight` 34 (`299 µs`), `ChainPlantSpecularLight` 6 (`52 µs`) | `Firefly` 40, `Lightstuff1` 33, `Lightstuff2` 16, `Minebomb` 11, `Crystalfish` 11, `Glare` 3 (`1,023 µs`) |
| All-7 `deepwater` | 20 | 4,139 µs (<1%) | 903.0 (`11,025 µs`, 1%) | `Light` 1 (`14,896 µs`, 1%), `Light2` 131 (`1,600 µs`), `BulbPlantLight` 34 (`417 µs`), `ChainPlantSpecularLight` 6 (`79 µs`) | — |

Key observations:
1. Every frame, `LightingSystem_Draw_0` runs `with (AbstractLight)` across all ~170–1,075 active lights in the entire `100,000+ px` room (twice when the minimap `view[1]` is open).
2. Even with `DecorSleep` active (which sweeps 512 decor instances/tick with a 2,000 px margin), `LightingSystem_Draw_0` still traverses the entire `AbstractLight` object list, and lights within `128..2000 px` of the view still execute `Draw_0` and surface subtraction.

## 3. Culling Architecture

`LightCulling` (`src/Jamm.Core/Builtins/LightCulling.cs`) is integrated as an opt-in `PerfSettings.LightCulling` toggle (default `true` in profiles, off in plain `builtin-perf` so vanilla + plain Perf remains byte-identical):

1. **Controller (`obj_jamm_light_cull`, `persistent = true`, `depth = -999998`)**:
   - Reads `[Perf] LightCulling` from `jamm_perf.ini` (independent of F8).
   - Runs in `DrawBegin` (`view_current == 0`, when `view_enabled && view_visible[0]`) after all `Step` events and after all higher-depth `DrawBegin` light-sync events (`light_anchor`, `GlowPlant_Draw_72`, `GlowCrystal_Draw_72`, `ChainPlant_Draw_72`, etc.).
   - Computes per-sprite conservative half-extents `_mx`, `_my` from `sprite_get_width/height/xoffset/yoffset` (cached in `global.jamm_lc_mx/my`), scaled by `max(abs(image_xscale), abs(image_yscale), abs(image_scale), abs(special_scale))` using build-time scale modes (`1` = `image_scale`, `2` = `image_scale` + `special_scale`, `3` = runtime `variable_instance_exists`). Uses L1 radius `_r = (_mx + _my) * _sx` so arbitrary rotations (`image_angle`, e.g. Submarine beam) and off-center origins (`Boat8Light` at `(0, 0)`) are never clipped.
   - Tests intersection against `view[0]` + `128 px` margin (`global.jamm_lc_v0`) and `view[1]` + `128 px` margin when `view_visible[1]` is true (`global.jamm_lc_v1`).
   - Lights that fail both view checks are temporarily set `visible = false` for the draw pass (skipping `AbstractLight_Draw_0` / `Light_Draw_0` / `Light2_Draw_0` etc.) and recorded in `global.jamm_lc_hid`. Lights already `visible == false` (from `DecorSleep`, `CrystalfishLight_Step_0`, `light_anchor`, or Submarine lamp toggle) are left untouched.
   - `PostDraw`, `DrawGUIBegin`, `RoomStart`, `RoomEnd`, and `Destroy` restore `visible = true` for `global.jamm_lc_hid` and reset `global.jamm_lc_ready = 0` so no instance's persistent `visible` state or `DecorSleep` visibility snapshot is altered across frames.
2. **`LightingSystem_Draw_0` loop rewrite**:
   - Replaces both `with (AbstractLight)` blocks (`view_current == 0` and `view_current == 1`) with a guarded loop over `global.jamm_lc_v0` / `global.jamm_lc_v1` when `global.jamm_lc_enabled && global.jamm_lc_ready`, retaining the original `with (AbstractLight)` as an `else` fallback.
