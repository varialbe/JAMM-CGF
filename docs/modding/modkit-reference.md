# JAMM ModKit Reference

ModKit compiles a folder package (`jamm-mod.json` + PNGs + optional `.gml` hooks) into a standalone GameMaker 1.4 `.win` mod that targets the legacy 2023 build of *Cat Goes Fishing*.

## CLI Commands

| Command | Description |
|---|---|
| `cgfmm modkit new <dir> [--template fish\|upgrade\|boat\|hat]` | Scaffold a starter package with a placeholder PNG and hook file. Default template: `fish`. |
| `cgfmm modkit check <dir>` | Parse and validate `jamm-mod.json`, image headers, frame counts, and hook files without building. |
| `cgfmm modkit build <dir> [--base <data.win>] [--out <file.win>]` | Compile the package onto the read-only base archive and verify the output `.win`. `--base` defaults to `config.base`; `--out` defaults to `<dir>/<id>.win`. |
| `cgfmm add <dir>` | Validate, build against the configured base, and import the package folder into JAMM's mod library. |

---

## Package Layout & Naming Rules

* Manifest file: `jamm-mod.json` at the package root.
* Asset paths (`sprite`, `card`, `icon`, and `hooks.*`) must be **relative paths inside the package directory** (no `..` traversal or absolute paths).
* `id` and every item `key` must match `^[a-z0-9_-]+$` and be unique per item type (`fish`, `boats`, `hats`, `upgrades`).
* Generated asset names are automatically namespaced as `jmk_<id>_<key>…` (`-` is converted to `_` in GML identifiers, while the string registration key stays `"<id>:<key>"`).
* Generated init script: `jamm_init_<id>`.
* Placeholder scripts (`return -1;`) are added only for the `jamm_*_register` functions the package calls, so the built `.win` compiles standalone and merges cleanly without conflicts.

---

## Top-Level `jamm-mod.json` Fields

| Field | Type | Required | Default | Notes |
|---|---|---|---|---|
| `id` | string | Yes | — | Package identifier (`^[a-z0-9_-]+$`). |
| `name` | string | Yes | — | Display name (used by `cgfmm add`). |
| `version` | string | No | `"1.0.0"` | Semantic version string. |
| `author` | string | No | `""` | Author name. |
| `description` | string | No | `""` | Package summary. |
| `fish` | array | No | `[]` | Custom fish definitions. |
| `boats` | array | No | `[]` | Custom boat definitions. |
| `hats` | array | No | `[]` | Custom hat (`Gear`) definitions. |
| `upgrades` | array | No | `[]` | Custom rod upgrade definitions. |

---

## `fish` Entries

Parent object is chosen from `size`: `FishSmall` (tab `0`), `FishMedium` (tab `1`), `FishLarge` (tab `2`), or `FishHuge` (tab `3`).

| Field | Type | Required | Default | Notes |
|---|---|---|---|---|
| `key` | string | Yes | — | Unique fish key (`^[a-z0-9_-]+$`). |
| `name` | string | Yes | — | Display name in Fishalog and catch popups. |
| `size` | string | Yes | — | `"small"`, `"medium"`, `"large"`, or `"huge"`. |
| `sprite` | string | Yes | — | Relative `.png` path (fish should face **left**). |
| `frames` | int | No | `1` | Horizontal strip frame count (`width % frames == 0`). |
| `value` | number | No | `100` | Base sale value (`BASE_VALUE`). |
| `xp` | number | No | `25` | Experience awarded on full-life catch (`XP_VALUE`). |
| `stamina` | number | No | `50`/`70`/`50`/`100` | `MAX_STAMINA` while hooked. |
| `lungeSpeed` | number | No | `8` | `LUNGE_SPEED`. |
| `lungeRange` | number | No | `60` | `LUNGE_RANGE_START`. |
| `lungeRangeStop` | number | No | `lungeRange + 5` | `LUNGE_RANGE_STOP`. |
| `speed` | number | No | `1.5` | Base idle swim speed. |
| `spawn` | object | Yes | — | `{ "x": [x1, x2], "y": [y1, y2], "count": N }`. Water surface is `y = 1244`. |
| `night` | bool | No | `false` | Sets `stayOutAtNight` (`1` if true). |
| `description` | string | No | `"<name>."` | Base catalog description. |
| `caughtDescription` | string | No | `description` | Shown in Fishalog once caught. |
| `seenDescription` | string | No | `description` | Shown in Fishalog once spotted. |
| `hiddenDescription` | string | No | `"Little is known about this fish."` | Shown before discovery. |
| `exotic` | bool | No | `false` | Whether the fish is treated as exotic. |
| `origin` | `[x, y]` | No | center | Sprite origin in pixels. |
| `hooks` | object | No | `{}` | `{ "create", "step", "caught", "draw" }` (`.gml` paths or `null`). |

### Fish Hook Entry Points

* `create`: runs at the end of `Create_0` after `event_inherited()`.
* `step`: runs at the end of `Step_2` after `Fish_Step_2` (so `state`, `lure_distance_squared`, `Lure`, and `is_it_night()` are up to date).
* `caught`: runs in `Other_19` (`ev_user9`) when `fish_was_caught()` triggers.
* `draw`: runs in `Draw_0` after `Fish_Draw_0`.

---

## `boats` Entries

Inherits from `Boat`. Registered via `jamm_boat_register`.

| Field | Type | Required | Default | Notes |
|---|---|---|---|---|
| `key` | string | Yes | — | Unique boat key. |
| `name` | string | Yes | — | Shop display name. |
| `cost` | number | No | `15000` | Purchase price in dollars. |
| `card` | string | Yes | — | Relative `.png` path for the shop card preview. |
| `sprite` | string | Yes | — | Relative `.png` path for the in-world boat sprite (drawn at `0.5x` scale by `Boat`). |
| `description` | string | No | `"<name>."` | Shop description (`#` for line breaks). |
| `unlocked` | bool | No | `false` | `false` (`0`, purchasable in shop) or `true` (`1`, already owned). |
| `stats` | object | No | `{}` | `maxSpeed` (`4`), `capacity` (`4`), `maxDistance` (`4500`), `moveBonus` (`0`). |
| `origin` | `[x, y]` | No | center | Boat world sprite origin. |

---

## `hats` Entries

Inherits from `Gear`. Registered via `jamm_hat_register`.

| Field | Type | Required | Default | Notes |
|---|---|---|---|---|
| `key` | string | Yes | — | Unique hat key. |
| `name` | string | Yes | — | Hatalog display name. |
| `sprite` | string | Yes | — | Relative `.png` path (drawn at `0.25x` scale). |
| `value` | number | No | `1500` | Base sell value (Hatalog buy price is `3x`, or `1x` if `weight == 0`). |
| `weight` | number | No | `5` | Ocean spawn weight (`0` disables ocean drops). |
| `rangeMin` | number | No | `0.0` | Normalized minimum spawn X (`0..1`). |
| `rangeMax` | number | No | `0.75` | Normalized maximum spawn X (`rangeMin..1`). |
| `offsetX` | number | No | `14` | `EQUIP_OFFSET_X` on the player cat. |
| `offsetY` | number | No | `-42` | `EQUIP_OFFSET_Y` on the player cat. |
| `hatalogOffsetX` | number | No | `0` | Hatalog icon X nudge. |
| `hatalogOffsetY` | number | No | `0` | Hatalog icon Y nudge. |
| `mythic` | bool | No | `false` | Marks the hat as mythic (level 45 Hatalog gate). |
| `description` | string | No | `"<name>."` | Hatalog description. |
| `origin` | `[x, y]` | No | center | Sprite origin. |
| `hooks` | object | No | `{}` | `{ "effect": "hooks/effect.gml" }` (runs in `Other_15` after `Gear_Other_15`). |

---

## `upgrades` Entries

Registered via `jamm_upgrade_register`.

| Field | Type | Required | Default | Notes |
|---|---|---|---|---|
| `key` | string | Yes | — | Unique upgrade key. |
| `name` | string | Yes | — | Upgrade menu title. |
| `description` | string | No | `"<name>."` | Long hover tooltip in `UpgradeCard`. |
| `description2` | string | No | `description` | Short description inside `UpgradeSlot`. |
| `cost` | number | No | `0.25` | `UpgradeData.cost` multiplier relative to `RodData.cost[rod_id]` (e.g. `0.05`..`1.2`, minimum `$300`). |
| `icon` | string | Yes | — | Relative `.png` path (`50x50` recommended, origin `0,0`). |
| `maxStacks` | int | No | `1` | Maximum stacks allowed per rod (`>= 1`). |
| `hooks` | object | No | `{}` | `{ "apply": "hooks/apply.gml", "step": "hooks/step.gml" }`. |

### Upgrade Hook Entry Points

* `apply`: called by `Rod` `Other_10` whenever upgrades are recomputed (`argument0` = installed stacks on the current rod). Because `Rod` `Other_10` resets `Rod.upgrades[]` and `Fish.base_lunge_mod` / `base_lunge_offset` first, `apply` hooks that modify those fields are idempotent.
* `step`: called every `Begin Step` by `obj_jamm_api` while installed (`argument0` = installed stacks on the current rod).

---

## GML Dialect Notes

ModKit compiles GML using UndertaleModLib for **GameMaker: Studio 1.4**:
* Declare local variables with `var x, y;` before assigning them.
* No GMS2 structs, `function() {}` literals, ternary chains, or `\n` string escapes (use `#` for line breaks in UI strings).
* `real()` throws on non-numeric strings.
