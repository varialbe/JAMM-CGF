# JAMM ModKit Reference

ModKit compiles a folder package (`jamm-mod.json` + PNGs + optional `.gml` hooks) into a standalone GameMaker 1.4 `.win` mod targeting the legacy 2023 build of *Cat Goes Fishing*.

---

## CLI Commands

| Command | Description |
| :--- | :--- |
| `cgfmm modkit new <dir> [--template fish\|upgrade\|boat\|hat]` | Scaffold a starter package with a placeholder PNG and hook file. Default template: `fish`. |
| `cgfmm modkit check <dir>` | Parse and validate `jamm-mod.json`, PNG headers/dimensions, and hook paths without building. |
| `cgfmm modkit build <dir> [--base <data.win>] [--out <file.win>]` | Compile onto a read-only archive and verify the output. The default input is the declared library base for addons or `config.base` for other packages. `--out` defaults to `<dir>/<id>.win`. |
| `cgfmm add <dir> [--name <name>]` | Validate, build and import the package folder directly into your JAMM mod library. |

---

## Package Layout & Rules

```text
my-mod/
  jamm-mod.json       # Required manifest
  art/                # PNG sprites (any relative subfolder is fine)
  hooks/              # Optional GM:S 1.4 .gml hook scripts
```

* **JSON syntax**: `jamm-mod.json` is strict standard JSON—**no trailing commas** and **no `//` or `/* */` comments**. Property names are matched case-insensitively.
* **Identifiers (`id` and `key`)**: Must be non-empty strings matching `^[a-z0-9_-]+$` (lowercase letters, digits, underscores, hyphens). Each item `key` must be unique within its array (`fish`, `boats`, `rods`, `hats`, `upgrades`).
* **Paths**: Every `sprite`, `card`, `icon`, and `hooks.*` path must be a **relative path inside the package directory** (no `..` parent traversal and no absolute paths).
* **Generated asset names**:
  * Hyphens (`-`) become underscores (`_`) in GML asset names while the string registration key remains `"<id>:<key>"`.
  * Fish: object `jmk_<id>_<key>`, sprite `jmk_<id>_<key>_spr`, optional hooks `jmk_<id>_<key>_{create,step,caught,draw}`.
  * Boats: object `jmk_<id>_<key>`, world sprite `jmk_<id>_<key>_spr`, shop card `jmk_<id>_<key>_card`.
  * Rods: sprite `jmk_<id>_<key>_rod_sprite`.
  * Hats: object `jmk_<id>_<key>`, sprite `jmk_<id>_<key>_spr`, optional hook `jmk_<id>_<key>_effect`.
  * Upgrades: icon sprite `jmk_<id>_<key>_icon`, optional hooks `jmk_<id>_<key>_{apply,step}`.
  * Room objects use `jmk_<id>_object_<key>`.
  * Init script: `jamm_init_<id>` when the package registers fish or items (with hyphens replaced by underscores).
* **Standalone placeholders**: `ModKitBuilder` adds `return -1;` placeholder scripts only for the `jamm_*_register` functions the package actually calls, so the built `.win` verifies standalone and merges with zero conflicts when JAMM replaces them at merge time.

---

## Top-Level `jamm-mod.json` Fields

| Field | Type | Required | Default | Notes |
| :--- | :--- | :---: | :--- | :--- |
| `id` | string | Yes | — | Package identifier (`^[a-z0-9_-]+$`). |
| `name` | string | Yes | — | Non-empty display name (used by `cgfmm add`). |
| `version` | string | No | `"1.0.0"` | Version string. |
| `author` | string | No | `""` | Author name. |
| `description` | string | No | `""` | Package summary. |
| `role` | string | No | — | Use `"base"` to declare an overhaul that other packages can target. |
| `base` | string | No | — | Addon target. Use the base mod's package ID or exact JAMM library ID. |
| `baseVersion` | string | No | — | Optional exact version of the base mod. |
| `baseSha256` | string | No | — | Optional 64-digit SHA-256 of the base mod's original archive. |
| `fish` | array | No | `[]` | Custom fish definitions. |
| `boats` | array | No | `[]` | Custom boat definitions. |
| `rods` | array | No | `[]` | Custom fishing rod definitions. |
| `hats` | array | No | `[]` | Custom hat (`Gear`) definitions. |
| `upgrades` | array | No | `[]` | Custom rod upgrade definitions. |
| `weathers` | array | No | `[]` | [Weather schedule, visuals, audio and fish](weather.md). |
| `areas` | array | No | `[]` | [Room areas and follow anchors](areas.md). |
| `objects` | array | No | `[]` | Custom room objects with optional event hooks. |
| `sprites` | array | No | `[]` | [Build-time sprite recipes](#sprite-recipes). |

---

## `objects` entries

Use `objects[]` for custom area effects and controllers. Each entry needs a unique `key`. Optional `sprite` names a recipe in `sprites[]`. Optional integer `depth` defaults to `0`. `hooks.create`, `hooks.step` and `hooks.draw` accept package-relative `.gml` files and run as the object's Create, normal Step and Draw events. A Draw hook can call `draw_self()` to include the object's assigned sprite.

Place the object by referring to `jmk_<id>_object_<key>` in an area's `pieces` or `lights`. Room objects have no parent and are recreated with the room. An area can use a colocated controller for an animation or trigger in [the Silo tutorial](../../examples/modkit/silo/README.md).

### Overhauls and addons

An overhaul can declare its identity in a ModKit manifest. Raw archives can
ship a `jamm.json` inside their zip, or a sidecar named
`<archive-stem>.jamm.json` beside a loose `.win` or zip.

```json
{
  "id": "reef-overhaul",
  "name": "Reef Overhaul",
  "version": "2.0.0",
  "role": "base"
}
```

An addon names its base and can pin the release it was made from.

```json
{
  "id": "night-reef",
  "name": "Night Reef",
  "base": "reef-overhaul",
  "baseVersion": "2.0.0"
}
```

Develop and test an addon against the overhaul author's original `data.win`.
Import the base before building a ModKit addon. When building a profile, JAMM
diffs the addon against that library archive and applies its changes on top
of the merged base. Use `baseSha256` to pin exact bytes even when the version
label stays the same. Version and hash mismatches warn by default and fail
`cgfmm check --profile <name> --strict` or
`cgfmm build --profile <name> --strict`.

---

## `fish` Entries

Parent object and Fishalog tab are selected from `size`:

| `size` | Size tab (`0..3`) | Parent object | Default `stamina` |
| :--- | :---: | :--- | :---: |
| `"small"` | `0` | `FishSmall` | `50` |
| `"medium"` | `1` | `FishMedium` | `70` |
| `"large"` | `2` | `FishLarge` | `50` |
| `"huge"` | `3` | `FishHuge` | `100` |

| Field | Type | Required | Default | Constraints / Notes |
| :--- | :--- | :---: | :--- | :--- |
| `key` | string | Yes | — | Unique within `fish` (`^[a-z0-9_-]+$`). |
| `name` | string | Yes | — | Non-empty display name in Fishalog and catch popups. |
| `size` | string | Yes | — | `"small"`, `"medium"`, `"large"`, or `"huge"` (case-insensitive). |
| `sprite` | string | Yes | — | Relative `.png` path or a key from `sprites[]`. Fish should face **left**. |
| `frames` | int | No | `1` | `>= 1`. Horizontal strip frame count (`width % frames == 0`). Omit for a composed sprite to use the recipe frame count. |
| `value` | number | No | `100` | `>= 0`. Base sale value (`BASE_VALUE`). |
| `xp` | number | No | `25` | `>= 0`. Experience awarded on full-life catch (`XP_VALUE`). |
| `stamina` | number | No | `50`/`70`/`50`/`100` | `>= 1`. `MAX_STAMINA` while hooked. |
| `lungeSpeed` | number | No | `8` | `>= 0`. `LUNGE_SPEED`. |
| `lungeRange` | number | No | `60` | `>= 0`. `LUNGE_RANGE_START`. |
| `lungeRangeStop` | number | No | `lungeRange + 5` | `>= lungeRange`. `LUNGE_RANGE_STOP`. |
| `speed` | number | No | `1.5` | `>= 0`. Base idle swim speed (`jamm_swim_speed`). |
| `spawn` | object | Yes | — | `{ "x": [x1, x2], "y": [y1, y2], "count": N }`. Each coordinate array contains two numbers that JAMM orders from minimum to maximum. `count` defaults to `3` and must be at least `1`. The water surface is `y = 1244`. For [area spawns](areas.md#fill-the-area-with-fish), use `{ "area": "<id>", "count": N }` with an optional relative `within` rectangle. |
| `night` | bool | No | `false` | Sets `stayOutAtNight` (`1` if true, `0` if false). |
| `description` | string | No | `"<name>."` | Base catalog description (and fallback for `desc_n` / `desc_e`). |
| `caughtDescription` | string | No | `description` | Shown in Fishalog once caught. |
| `seenDescription` | string | No | `description` | Shown in Fishalog once spotted. |
| `hiddenDescription` | string | No | `"Little is known about this fish."` | Shown before discovery. |
| `exotic` | bool | No | `false` | Marks the fish as exotic in `CatalogData`. |
| `origin` | `[x, y]` | No | center | 2-integer sprite origin in pixels (`[frameWidth/2, height/2]`). |
| `hooks` | object | No | `{}` | `{ "create", "step", "caught", "draw" }` (relative `.gml` path or `null`). |

### Fish idle swimming & hook entry points

ModKit fish override `Other_10` (`ev_user0`) with **path-free idle swimming** so they do not depend on `Fish_Other_10`'s `path_thrust_update()` path assets: medium fish still flee large fish (`fish_fleelarge()`), large fish flee huge fish (`fish_fleehuge()`), `fish_lunge()` runs normally, and fish gently wander near `(start_x, start_y)` while staying below `y = 1294` and right of `x = 600`.

| Hook | GameMaker event | Arguments | Notes & commonly used variables |
| :--- | :--- | :--- | :--- |
| `create` | `Create_0` (at end, after `event_inherited()`) | None | `x`, `y`, `start_x`, `start_y`, `BASE_VALUE`, `XP_VALUE`, `MAX_STAMINA`, `stamina`, `stayOutAtNight`, `jamm_swim_speed`, `image_blend`. Set custom instance variables here. |
| `step` | `Step_2` (End Step, after `event_inherited()` / `Fish_Step_2`) | None | `state` (`0` idle, `1` lunging, `2` returning, `3` hooked, `9` fleeing, `10` night retreat), `lure_distance_squared`, `Lure.x`, `Lure.y`, `Lure.state`, `is_it_night()`, `hspeed`, `vspeed`, `image_blend`, `image_alpha`. |
| `caught` | `Other_19` (`ev_user9`, after `event_inherited()`) | None | Runs when `fish_was_caught()` completes. `life`, `MAX_LIFE`, `value`, `Girl.dollars`, `Rod.rod_id`. |
| `draw` | `Draw_0` (after `event_inherited()` / `Fish_Draw_0`) | None | Standard GM draw state (`x`, `y`, `sprite_index`, `image_index`, `image_xscale`, `image_yscale`, `image_angle`, `image_blend`, `image_alpha`). |

---

## `boats` Entries

Inherits from `Boat` (`depth = -20`). Registered via `jamm_boat_register`. Boats do not support `hooks`.

| Field | Type | Required | Default | Constraints / Notes |
| :--- | :--- | :---: | :--- | :--- |
| `key` | string | Yes | — | Unique within `boats` (`^[a-z0-9_-]+$`). |
| `name` | string | Yes | — | Non-empty shop display name. |
| `cost` | number | No | `15000` | `>= 0`. Purchase price in dollars. |
| `card` | string | Yes | — | Relative `.png` path (1 frame) for the shop card preview. |
| `sprite` | string | Yes | — | Relative `.png` path (1 frame) for the in-world boat (drawn at **`0.5x`** scale by `Boat`). |
| `description` | string | No | `"<name>."` | Shop description (`#` or newlines become `#` line breaks). |
| `unlocked` | bool | No | `false` | `false` (`0`, purchasable in shop) or `true` (`1`, already owned). |
| `stats` | object | No | See right | `maxSpeed` (`4`, min `0.1`), `capacity` (`4`, int min `1`), `maxDistance` (`4500`, min `500`), `moveBonus` (`0`, min `0`). |
| `origin` | `[x, y]` | No | center | 2-integer origin for the world `sprite` (the `card` sprite is always centered). |

---

## `rods` Entries

ModKit calls `jamm_rod_register` from the package's generated init script. The merged game uses the same rod shop, upgrade grid, and save behavior as a rod registered in GML.

| Field | Type | Required | Default | Constraints / Notes |
| :--- | :--- | :---: | :--- | :--- |
| `key` | string | Yes | — | Unique in `rods`. Keys that differ only in hyphens and underscores share a GML asset name. |
| `name` | string | Yes | — | Non-empty shop display name. |
| `cost` | number | Yes | — | Nonnegative purchase price, using the same field as boats. |
| `cast_speed` | number | Yes | — | Casting speed. |
| `rod_power` | number | Yes | — | Reeling power multiplier. |
| `rod_power_flat` | number | Yes | — | Flat reeling power bonus. |
| `reel_speed` | number | Yes | — | Reeling speed. |
| `sinker_distance` | number | Yes | — | Maximum sinker distance. |
| `sprite` | string | Yes | — | Relative path to a single-frame PNG used in the shop and when equipped. |
| `origin` | `[x, y]` | No | center | Two integer sprite coordinates, using the same rules as boats and hats. |
| `upgrade_slots` | integer or `"vanilla"` | No | `8` | Choose `0..16` or one slot more than the highest slot count among existing rods, capped at 16. |
| `unlocked` | bool | No | `false` | `false` makes the rod purchasable. `true` starts it owned, as with boats. |

Use `cost` and `unlocked` for shop availability and ownership. See [the rod cookbook example](cookbook.md#9-a-custom-rod-in-the-shop) and [the registration API](api-reference.md#2-jamm_rod_register).

---

## `hats` Entries

Inherits from `Gear` (`depth = -4`). Registered via `jamm_hat_register`. Note that `name` and `description` are written onto the hat object's `Create_0` (`name`, `desc`) because `Hatalog` reads them directly from the hat object.

| Field | Type | Required | Default | Constraints / Notes |
| :--- | :--- | :---: | :--- | :--- |
| `key` | string | Yes | — | Unique within `hats` (`^[a-z0-9_-]+$`). |
| `name` | string | Yes | — | Non-empty Hatalog display name. |
| `sprite` | string | Yes | — | Relative `.png` path (1 frame, drawn at **`0.25x`** scale). |
| `value` | number | No | `1500` | `>= 0`. Base sell value. Hatalog buy price is `3x` value (or `1x` if `weight == 0`). |
| `weight` | number | No | `5` | `>= 0`. Ocean spawn weight (`0` disables random ocean drops). |
| `rangeMin` | number | No | `0.0` | `0.0..1.0`. Normalized minimum ocean spawn X. |
| `rangeMax` | number | No | `0.75` | `rangeMin..1.0`. Normalized maximum ocean spawn X. |
| `offsetX` | number | No | `14` | Sets `EQUIP_OFFSET_X` on the player cat. |
| `offsetY` | number | No | `-42` | Sets `EQUIP_OFFSET_Y` on the player cat. |
| `hatalogOffsetX` | number | No | `0` | Passed as `offset_x` to `jamm_hat_register` (Hatalog icon X nudge). |
| `hatalogOffsetY` | number | No | `0` | Passed as `offset_y` to `jamm_hat_register` (Hatalog icon Y nudge). |
| `mythic` | bool | No | `false` | Marks the hat as mythic (level 45 Hatalog gate). |
| `description` | string | No | `"<name>."` | Hatalog description. |
| `origin` | `[x, y]` | No | center | 2-integer sprite origin. |
| `hooks` | object | No | `{}` | `{ "effect": "hooks/effect.gml" }` (relative `.gml` path or `null`). |

### Hat hook entry point

| Hook | GameMaker event | Arguments | Common variables inside `Gear_Other_15` |
| :--- | :--- | :--- | :--- |
| `effect` | `Other_15` (`ev_user5`, after `event_inherited()`) | None | `value_scalar[0..3]` (by size: 0=small, 1=medium, 2=large, 3=huge), `lure_scalar[0..n]`, `upgrade_scalar[0..n]`, `no_upgrades` (`1` disables rod upgrades like Tophat), `attract_scalar`, `repel_scalar`, `respawn_scalar`. |

*(Base defaults reset all scalars to `1` in `Gear_Other_15` before your `effect` hook runs, so simple assignments like `value_scalar[0] = 1.25;` work cleanly.)*

---

## `upgrades` Entries

Registered via `jamm_upgrade_register` with `unlocks = 1` (immediately available in the Upgrade menu).

| Field | Type | Required | Default | Constraints / Notes |
| :--- | :--- | :---: | :--- | :--- |
| `key` | string | Yes | — | Unique within `upgrades` (`^[a-z0-9_-]+$`). |
| `name` | string | Yes | — | Non-empty upgrade menu title. |
| `description` | string | No | `"<name>."` | Long hover tooltip in `UpgradeCard`. |
| `description2` | string | No | `description` | Short description inside `UpgradeSlot`. |
| `cost` | number | No | `0.25` | `>= 0`. **Multiplier** relative to `RodData.cost[rod_id]` (e.g. `0.25` = 25% of the active rod's price; base game enforces a `$300` minimum in `calculate_upgrade_cost`). |
| `icon` | string | Yes | — | Relative `.png` path (1 frame, `50x50` recommended, origin is always `0,0`). |
| `maxStacks` | int | No | `1` | `>= 1`. Maximum stacks allowed per rod (enforced in `UpgradeCard_Other_10`). |
| `hooks` | object | No | `{}` | `{ "apply": "hooks/apply.gml", "step": "hooks/step.gml" }`. |

### Upgrade hook entry points

| Hook | Who calls it & when | Arguments | Idempotency & available variables |
| :--- | :--- | :--- | :--- |
| `apply` | `Rod` `Other_10` whenever upgrades are recomputed (equipping, adding/removing upgrades, loading save) | `argument0` = installed stacks (`> 0`) on the current rod | `Rod` `Other_10` resets `Rod.upgrades[0..21] = 0` and `Fish.base_lunge_mod = 1` / `base_lunge_offset = 0` **before** calling `apply` hooks, so modifying those fields with `+=` or `*=` is automatically idempotent. |
| `step` | `obj_jamm_api` every `Begin Step` (`Step_1`) while installed (`stacks > 0`) | `argument0` = installed stacks (`> 0`) on the current rod | Runs once per simulation step. Use marker variables on targets (e.g. `jmk_..._scaled`) so per-catch or per-frame effects do not compound unexpectedly. |

*(Common `Rod.upgrades[]` indices used by base `Lure_Step_0`: `[0]` sink speed bonus, `[1]` reel speed bonus, `[13]` line tension bonus `120 * upgrades[13]`.)*

---

## Validation Errors & How to Fix Them

`cgfmm modkit check` and `ModKitPackage.Load` report errors in the form `jamm-mod.json (<jsonPath>): <message>`.

| Error message | Cause | Fix |
| :--- | :--- | :--- |
| `Missing 'jamm-mod.json' in '...'` | Target folder has no manifest at its root. | Pass the folder containing `jamm-mod.json`, or create one with `cgfmm modkit new <dir>`. |
| `Invalid JSON in 'jamm-mod.json' at ...` | Syntax error, trailing comma `, }`, or `//` comment. | Remove comments/trailing commas and validate JSON syntax. |
| `Invalid identifier '...'. Use only lowercase letters...` | `id` or `key` contains uppercase letters, spaces, or symbols. | Rename to lowercase `[a-z0-9_-]+` (e.g. `my-fish`, `sunfin_1`). |
| `Duplicate key '...'` | Two items in the same array (`fish`, `boats`, `hats`, or `upgrades`) share a `key`. | Give each item in that array a distinct `key`. |
| `Asset path '...' must be a relative path inside the package directory` | Used `/abs/path`, `C:\...`, or `../other`. | Move the PNG/GML inside your package folder and use a relative path like `"art/fish.png"`. |
| `Asset '...' must have extension .png` (or `.gml`) | Wrong file type for sprite/icon/hook. | Convert images to `.png` and hooks to `.gml`. |
| `Referenced file '...' was not found inside the package directory` | File missing or path typo. | Check spelling/case (case-sensitive on Linux) and verify the file exists. |
| `File '...' is not a valid PNG image` | Image was saved as JPEG/WebP/BMP and renamed `.png`. | Re-export as a genuine PNG file. |
| `PNG '...' is WxH, which is not evenly divisible by frames=N` | Horizontal strip width isn't an exact multiple of `frames`. | Resize canvas width to `frameWidth * frames` (or adjust `frames`). |
| `Unknown fish size '...'` | `size` is not one of the four supported sizes. | Use `"small"`, `"medium"`, `"large"`, or `"huge"`. |
| `Missing required 'spawn' object` / `Expected a two-number array [min, max]` | `fish[].spawn` missing or `x`/`y` not `[min, max]`. | Set `"spawn": { "x": [1100, 2200], "y": [1400, 1900], "count": 4 }`. |
| `'...' must be at least ...` / `between ... and ...` | Numeric field outside allowed range (e.g. `lungeRangeStop < lungeRange`, `rangeMax < rangeMin`, `maxSpeed < 0.1`, `stamina < 1`). | Adjust the number to satisfy the constraint shown in the message. |
| `Origin coordinates in '...' must be integers` | `origin` used decimals or didn't have 2 elements. | Use two integers, e.g. `[24, 16]`. |

---

## Art Requirements

* **File format**: Standard 8-bit RGBA PNG with transparent background (alpha `0` for empty pixels). `ModKitSpriteImporter` packs sprites onto new 2048x2048 GameMaker texture pages (`allowCrop: false`, `border: 2`).
* **Frames**:
  * `fish[].sprite` supports horizontal sprite strips (`frames >= 1`, `totalWidth % frames == 0`).
  * `boats[].card`, `boats[].sprite`, `hats[].sprite`, and `upgrades[].icon` are single-frame (`frames = 1`).
* **Origins & masks**:
  * **Fish**: default centered `[frameWidth / 2, height / 2]`; 1-bit collision mask generated from frame 0 alpha > 0. Draw fish facing **left** (head left, tail right).
  * **Boats**: world `sprite` defaults to centered `[width / 2, height / 2]` (override with `origin` to adjust waterline height); `card` is always centered. No collision mask. World boat sprite is drawn at **`0.5x`** scale, so a `200x72` world PNG renders at `100x36` in-game (`100x36` is a good `card` size).
  * **Hats**: default centered `[width / 2, height / 2]`; 1-bit collision mask generated from alpha > 0. Drawn at **`0.25x`** scale on the player cat (so e.g. `96x72` renders as `24x18`).
  * **Upgrade icons**: always origin `(0, 0)`, no collision mask. Recommended **`50x50`** (matches `spr_upgradeicons`; JAMM scales custom icons proportionally to fit `spr_upgradeicons` in `UpgradeCard`, `UpgradeSlot`, and HUD `UpgradeData`).

---

## GML Dialect Notes

ModKit compiles hooks with UndertaleModLib targeting **GameMaker: Studio 1.4**:

* Declare local variables with `var a, b;` before assigning them.
* No GMS2+ structs (`{}`), `function()` literals, chained ternaries, or `\n` string escapes (use `#` for line breaks in UI strings).
* `real()` throws on non-numeric strings.


## Sprite recipes

A recipe creates an ordinary GameMaker sprite using frames sampled from the player's own legacy `data.win` and optional original package artwork. Distribute the recipe and any original parts with the ModKit source. JAMM reads the referenced base sprite by exact asset name at build time.

```json
{
  "sprites": [
    {
      "key": "scute-fish",
      "width": 176,
      "height": 86,
      "origin": [30, 50],
      "compose": [
        { "from": "spr_fish_s17", "crop": { "x": 53, "y": 9, "w": 42, "h": 61 },
          "x": 112, "y": 7, "scaleX": 1.3, "hueShift": 48 },
        { "from": "spr_powerfin", "frame": 0, "crop": { "x": 0, "y": 0, "w": 144, "h": 152 },
          "x": 4, "y": 0, "scaleX": 0.85, "scaleY": 0.5, "tint": "#b4e3b4", "z": 1 },
        { "file": "parts/scutes.svg", "x": 55, "y": 29, "opacity": 0.8, "z": 2 }
      ]
    }
  ]
}
```

Set a fish's `sprite` to `"scute-fish"` or reference the key from an area's `opens.sprite`. Output dimensions are 1–2044 pixels per axis. Origin defaults to the canvas center. For a composed fish, set the origin on the sprite recipe. Compose several output frames by supplying an array of layer arrays, `"compose": [[…], […]]`. All frames share the same canvas and origin. The first frame defines the sprite's collision mask. Frame selection in play is controlled by the object's GML.

| Layer field | Meaning |
| --- | --- |
| `from`, `frame` | Exact base sprite name and zero-based source frame. Frame defaults to 0. |
| `file` | Relative path to an original PNG or self-contained SVG, in place of `from`. SVG parts can use plain shapes and paths with inline drawing attributes. |
| `crop` | Optional integer rectangle `{x,y,w,h}` in the padded source frame. It must fit the source. |
| `alphaMask` | Optional package-relative PNG or self-contained SVG with the same dimensions as the cropped layer (or the full source when there is no crop). JAMM multiplies the source alpha by the mask alpha and preserves the source RGB. The clip follows the layer through flip, scale and rotation. |
| `x`, `y` | Position of the transformed layer's upper-left corner on the canvas. Defaults to 0. Pixels outside the canvas are clipped. |
| `scaleX`, `scaleY` | Positive multipliers for width and height. Defaults to 1. |
| `rotation`, `flip` | Image rotation in clockwise degrees and flip direction (`none`, `x`, `y`, `both`). Flipping comes before scaling and rotation. |
| `paletteMap` | Exact RGB replacement pairs, for example `{"#fece52":"#67a889"}`. Alpha is preserved. |
| `hueShift`, `tint` | Hue rotation in degrees, then a `#RRGGBB` channel multiplier. Palette replacements occur first. |
| `tones` | Flat palette with two or three `colors` and one ascending `cutoffs` value between each pair. Cutoffs use integer luminance from 1 to 255. A pixel at a cutoff uses the next color. Applied after the other color and geometry transforms, with alpha preserved. |
| `edgeSoftness` | Optional alpha smoothing after resizing and rotation, measured in output pixels from 0 to 1.5. Defaults to 0. Fractional values soften jagged silhouette edges while preserving opaque colors. Leave a little transparent space around the source silhouette. |
| `opacity`, `z` | Alpha multiplier from 0 to 1 and integer stacking order. Higher Z is drawn later. Equal Z follows manifest order. |

`modkit check` validates the recipe fields and package paths. `modkit build` also validates referenced base frames and crop sizes against the selected archive.


## Sale values and debug stats in merged builds

A standalone ModKit build keeps `fish[].value` as authored. Final profile assembly
limits fish `BASE_VALUE` and appraised values to the profile's `fishValueCap`
(default 100,000,000). The same cap applies to values supplied through the Fish API.
Computed assignments are guarded at the point of assignment.

With `fairPlay` enabled, JAMM checks modded rod and boat stats, lure and upgrade
effects, shop prices and fish XP against vanilla and comparable items from the
same mod. It uses ten times the highest positive vanilla value as headroom.
An isolated value is beyond both that headroom and five times the highest
comparable peer. For isolated values at or above one billion, JAMM uses the higher
of vanilla headroom and the peer value. Known placeholder literals
`9,999,999` and `999,999,999` above vanilla headroom, and reel or sink speeds
above headroom and at least 10,000, are capped at vanilla headroom. A nonpositive
price for a paid vanilla item restores that item's original price. Possible
debug values without clear evidence
appear as `[CGF-FAIR-002]`. Wallet balances and money grants are outside this guard.
