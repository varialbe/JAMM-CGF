# Endless Ocean 0.3.0 — sonar and marker

## What changed

- Added region-scoped, symbol-guarded sonar hooks. In the ocean any boat clears sonar noise; outside, the vanilla noise formula and Boat7 `boost_radar` rule remain in place. Deep basic sonar follows the lure in a 2000×2000 view without setting `cave_sonar`; shallow ocean sonar follows vertically. The basic blip rendering stays active.
- Added one-time entry/exit messages with a 300 px leave hysteresis, a buoy/beam at `ocean_start`, and an ocean distance/biome label that respects `Girl.hide_ui`.
- Bumped the Endless Ocean recipe to 0.3.0 / generator version 3. Boat7 range extension remains in the existing Boat7-only controller block.

## Patch review

The verified archive was decompiled with `tools/Jamm.ModMaker dump`. The vanilla `SonarPanel_Step_0` and `SonarPanel_Draw_0` bodies remain in their existing order. The Step hook is appended after the vanilla tail; the Draw hook is immediately after the original `Boat.boost_radar` block:

```gml
// gml_Object_SonarPanel_Step_0 (added tail)
jmk_endless_ocean_sonar_view();

// gml_Object_SonarPanel_Draw_0 (added after vanilla boost_radar block)
if (jmk_endless_ocean_sonar_clear()) noise_alpha = 0;
```

The decompiled Draw event still contains the vanilla `noise_alpha` expression, the unchanged Boat7 boost check, the regular fish blip loop, and the original scaled noise/panel draws. Re-applying to decompiled output is idempotent, including the compiler’s expanded multi-line form of the Draw hook.

Vanilla sonar sprites cover the moved windows with the current draw offsets and scales: `spr_sonarpanel` is 201×202 at scale 20 (4020×4040); `spr_sonarnoise` is 192×190 at scale 12 (2304×2280). With the deep branch’s -100 px draw offset, both cover the 2000×2000 window. In shallow mode the noise sprite scales to 3840×3800, enough for the 3700×3700 window.

The real archive resolves the buoy marker sprite by symbol to `spr_buoy_1` from `Buoy`. `Boat7_Create_0` and `Boat_Create_0` decompiled identically in the original and generated archives; vanilla Boat7 still has `MAX_X = 65000` and `boost_radar = 1`, while the base Boat keeps `MAX_X = 2000` and `boost_radar = 0`.

## Verification

- `dotnet build -c Release` — passed, 0 warnings and 0 errors.
- `dotnet test -c Release` — passed: 699 Core tests and 131 GUI tests; 19 Core tests and 1 GUI test skipped because their optional inputs were not configured.
- `node --test tests/worldgen/chunks.test.mjs` — passed, 9 tests.
- Real legacy archive build — passed with the task base SHA-256 `dbb2e5c4b788358d0c6465d3005b2e9d46c4c540e423b064d7a5a3629286d2c1`; archive verification reloaded, checked references, decompiled and compiled the result. Scratch output: `/tmp/jamm-endless-ocean-0.3.0.win`.
- Real full composition test — passed (1 test, 1m24s) using the clean legacy base, all four built-ins, every first-party ModKit package, and Endless Ocean. This included the requested `perf`, `hd-lite`, `console`, `smooth`, `silo`, `weather-pack`, `endless-ocean`, `submarine`, and `harbor-town` combination. The composed archive kept exactly one sonar hook in each event.
- Worldgen scene instrumentation — `jamm-modmaker bench ... --scene worldgen` compiled and wrote a verified scratch archive. Its decompile contains `sonar_visible`, `noise_alpha`, `inside`, and `marker_label_shown` checkpoint columns, with object lookups by symbol.
- No game launch or in-game test was performed.

## Decompiled new runtime code

The following sections are from the built and verified archive. The only omitted decompile is the unchanged vanilla body of the two SonarPanel events.

### Inside-region check — `gml_Script_jmk_endless_ocean_inside`

```gml
var ocean_object = asset_get_index("obj_jmk_endless_ocean");
var girl_object = asset_get_index("Girl");
var lure_object = asset_get_index("Lure");
if (ocean_object < 0 || girl_object < 0 || lure_object < 0)
{
    return 0;
}
if (!instance_exists(ocean_object) || !instance_exists(girl_object) || !instance_exists(lure_object))
{
    return 0;
}
var ocean = instance_find(ocean_object, 0);
var girl = instance_find(girl_object, 0);
var lure = instance_find(lure_object, 0);
if (!variable_instance_exists(ocean, "ocean_start"))
{
    return 0;
}
var x_to_check = girl.x;
if (lure.state == 1 || lure.state == 2)
{
    x_to_check = lure.x;
}
return x_to_check >= ocean.ocean_start;
```

### Sonar view — `gml_Script_jmk_endless_ocean_sonar_view`

```gml
if (!jmk_endless_ocean_inside())
{
    exit;
}
var girl_object = asset_get_index("Girl");
var lure_object = asset_get_index("Lure");
if (girl_object < 0 || lure_object < 0)
{
    exit;
}
if (!instance_exists(girl_object) || !instance_exists(lure_object))
{
    exit;
}
var girl = instance_find(girl_object, 0);
var lure = instance_find(lure_object, 0);
if (girl.hide_ui)
{
    exit;
}
if (!((lure.state == 1 || lure.state == 2 || drawOverride) || (lure.fish > 0 && lure.fish.display_size == 4)))
{
    exit;
}
shouldDraw = 1;
if (lure.deep && !cave_sonar)
{
    fixed_y = min(lure.y, room_height - 1000);
    view_xview[3] = lure.x - 1000;
    view_yview[3] = max(fixed_y - 1000, 4000);
    view_wview[3] = 2000;
    view_hview[3] = 2000;
}
else if (!lure.deep)
{
    view_yview[3] = clamp(lure.y - 1850, 0, max(0, room_height - 3700));
    view_wview[3] = 3700;
    view_hview[3] = 3700;
}
if (active && shouldDraw)
{
    view_visible[3] = true;
}
```

### Sonar clear — `gml_Script_jmk_endless_ocean_sonar_clear`

```gml
return jmk_endless_ocean_inside();
```

### Entry and exit messages — `gml_Object_obj_jmk_endless_ocean_Step_0`

```gml
if (!instance_exists(Girl) || !instance_exists(SaveManager) || !SaveManager.loaded)
{
    exit;
}
if (!ocean_inside_initialized)
{
    ocean_was_inside = Girl.x >= ocean_start;
    ocean_inside_initialized = 1;
    exit;
}
if (!ocean_was_inside && Girl.x >= ocean_start)
{
    ocean_was_inside = 1;
    var ocean_message = instance_create(0, 0, obj_jmk_endless_ocean_message);
    if (instance_exists(ocean_message))
    {
        ocean_message.text = "Endless Ocean";
    }
}
else if (ocean_was_inside && Girl.x < (ocean_start - 300))
{
    ocean_was_inside = 0;
    var ocean_message = instance_create(0, 0, obj_jmk_endless_ocean_message);
    if (instance_exists(ocean_message))
    {
        ocean_message.text = "Leaving the Endless Ocean";
    }
}
```

### Buoy marker — `gml_Object_obj_jmk_endless_ocean_Draw_0`

```gml
if (!instance_exists(obj_jmk_endless_ocean))
{
    exit;
}
var marker_sprite = asset_get_index("spr_buoy_1");
if (marker_sprite < 0)
{
    exit;
}
var marker_x = ocean_start;
var marker_y = 1234 + (x_pos_to_y_off(marker_x) * 0.5);
draw_set_blend_mode(bm_add);
draw_set_color(make_color_rgb(92, 214, 255));
draw_set_alpha(0.35);
draw_line_width(marker_x, marker_y - 420, marker_x, marker_y - 16, 18);
draw_set_alpha(0.9);
draw_line_width(marker_x, marker_y - 420, marker_x, marker_y - 16, 4);
draw_set_blend_mode(bm_normal);
draw_set_color(c_white);
draw_set_alpha(1);
draw_sprite_ext(marker_sprite, 0, marker_x, marker_y, 0.8, 0.8, 0, c_white, 1);
```

### GUI label — `gml_Object_obj_jmk_endless_ocean_Draw_64`

```gml
if (!instance_exists(Girl) || !instance_exists(obj_jmk_endless_ocean))
{
    exit;
}
var girl = instance_find(Girl, 0);
if (!jmk_endless_ocean_inside() || girl.hide_ui)
{
    exit;
}
var ocean_distance = max(0, (girl.x - ocean_start) / 1000);
var ocean_biome = jmk_endless_ocean_biome(girl.y, room_height);
var ocean_biome_name = (ocean_biome == 0) ? "Sandy Shelf" : ((ocean_biome == 1) ? "Rocky Slope" : "Lit Trench");
var ocean_label_text = "ENDLESS OCEAN  ·  " + string_format(ocean_distance, 1, 1) + " km  ·  " + ocean_biome_name;
var ocean_label_x = (display_get_gui_width() - string_width(ocean_label_text)) * 0.5;
draw_set_halign(fa_left);
draw_set_valign(fa_top);
draw_set_color(c_black);
draw_text(ocean_label_x + 2, 22, ocean_label_text);
draw_text(ocean_label_x - 2, 22, ocean_label_text);
draw_text(ocean_label_x + 2, 18, ocean_label_text);
draw_text(ocean_label_x - 2, 18, ocean_label_text);
draw_set_color(c_white);
draw_text(ocean_label_x, 20, ocean_label_text);
draw_set_color(c_white);
```

### Message setup — `gml_Object_obj_jmk_endless_ocean_message_Create_0`

```gml
action_inherited();
text = "Endless Ocean";
font = 1;
```
