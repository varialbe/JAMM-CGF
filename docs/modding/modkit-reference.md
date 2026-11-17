# JAMM ModKit Reference

ModKit compiles a folder package (`jamm-mod.json` + PNGs + optional `.gml` hooks) into a standalone GameMaker 1.4 `.win` mod targeting the legacy 2023 build of *Cat Goes Fishing*.

---

## CLI Commands

| Command | Description |
| :--- | :--- |
| `cgfmm modkit new <dir> [--template fish\|upgrade\|boat\|hat]` | Scaffold a starter package with a placeholder PNG and hook file. Default template: `fish`. |
| `cgfmm modkit check <dir>` | Parse and validate `jamm-mod.json`, PNG headers/dimensions, and hook paths without building. |
| `cgfmm modkit build <dir> [--base <data.win>] [--out <file.win>]` | Compile onto the read-only base archive and verify the output `.win`. `--base` defaults to `config.base`; `--out` defaults to `<dir>/<id>.win`. |
| `cgfmm add <dir> [--name <name>]` | Validate, build against the configured base, and import the package folder directly into your JAMM mod library. |

---

## Package Layout & Rules

```text
my-mod/
  jamm-mod.json       # Required manifest
  art/                # PNG sprites (any relative subfolder is fine)
  hooks/              # Optional GM:S 1.4 .gml hook scripts
```

* **JSON syntax**: `jamm-mod.json` is strict standard JSON—**no trailing commas** and **no `//` or `/* */` comments**. Property names are matched case-insensitively.
* **Identifiers (`id` and `key`)**: Must be non-empty strings matching `^[a-z0-9_-]+$` (lowercase letters, digits, underscores, hyphens). Each item `key` must be unique within its array (`fish`, `boats`, `hats`, `upgrades`).
* **Paths**: Every `sprite`, `card`, `icon`, and `hooks.*` path must be a **relative path inside the package directory** (no `..` parent traversal and no absolute paths).
* **Generated asset names**:
  * Hyphens (`-`) become underscores (`_`) in GML asset names while the string registration key remains `"<id>:<key>"`.
  * Fish: object `jmk_<id>_<key>`, sprite `jmk_<id>_<key>_spr`, optional hooks `jmk_<id>_<key>_{create,step,caught,draw}`.
  * Boats: object `jmk_<id>_<key>`, world sprite `jmk_<id>_<key>_spr`, shop card `jmk_<id>_<key>_card`.
  * Hats: object `jmk_<id>_<key>`, sprite `jmk_<id>_<key>_spr`, optional hook `jmk_<id>_<key>_effect`.
  * Upgrades: icon sprite `jmk_<id>_<key>_icon`, optional hooks `jmk_<id>_<key>_{apply,step}`.
  * Init script: `jamm_init_<id>` (with hyphens replaced by underscores).
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
| `fish` | array | No | `[]` | Custom fish definitions. |
| `boats` | array | No | `[]` | Custom boat definitions. |
| `hats` | array | No | `[]` | Custom hat (`Gear`) definitions. |
| `upgrades` | array | No | `[]` | Custom rod upgrade definitions. |

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
| `sprite` | string | Yes | — | Relative `.png` path (fish should face **left**). |
| `frames` | int | No | `1` | `>= 1`. Horizontal strip frame count (`width % frames == 0`). |
| `value` | number | No | `100` | `>= 0`. Base sale value (`BASE_VALUE`). |
| `xp` | number | No | `25` | `>= 0`. Experience awarded on full-life catch (`XP_VALUE`). |
| `stamina` | number | No | `50`/`70`/`50`/`100` | `>= 1`. `MAX_STAMINA` while hooked. |
| `lungeSpeed` | number | No | `8` | `>= 0`. `LUNGE_SPEED`. |
| `lungeRange` | number | No | `60` | `>= 0`. `LUNGE_RANGE_START`. |
| `lungeRangeStop` | number | No | `lungeRange + 5` | `>= lungeRange`. `LUNGE_RANGE_STOP`. |
| `speed` | number | No | `1.5` | `>= 0`. Base idle swim speed (`jamm_swim_speed`). |
| `spawn` | object | Yes | — | `{ "x": [x1, x2], "y": [y1, y2], "count": N }`. `x` and `y` are 2-number arrays (auto-ordered min..max); `count` defaults to `3` (`>= 1`). Water surface is `y = 1244`. |
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
