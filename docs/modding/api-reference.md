# GML Runtime API Reference

Room placement and build-time art use ModKit's declarative APIs. See [Area API](areas.md) for room bounds, anchors, instance settings and free-water fish spawning, and [sprite recipes](modkit-reference.md#sprite-recipes) for composing sprites from base art.

This reference is for **UndertaleModTool / GML authors** (and curious ModKit authors) who want to register boats, rods, lures, tools, hats, rod upgrades, or fish directly in GameMaker: Studio 1.4 GML without editing shared base objects.

If you prefer JSON + PNGs + short hook scripts without UndertaleModTool, use **[JAMM ModKit](modkit-reference.md)** instead—ModKit generates `jamm_init_*` scripts calling this exact runtime API under the hood.

---

## How `jamm_init_*` and `jamm_api_init` Work

1. **Write one or more `jamm_init_<modid>` scripts** in your `.win` mod (for example `jamm_init_coolmod`). Add only your own new sprites, objects, and scripts—leave `BoatData`, `RodData`, `LureData`, `ToolData`, `GearData`, `UpgradeData`, `CatalogData`, `SaveManager`, and rooms untouched.
2. **Order of execution**: At merge time, JAMM discovers every `jamm_init_*` script across the merged archive, sorts them deterministically by **profile load order** (then alphabetically by script name), and generates **`jamm_api_init`**.
3. **When `jamm_api_init` runs**:
   * **When Item / Upgrade API is active** (a code entry calls `jamm_{boat,rod,lure,tool,hat,upgrade}_register` or an initializer needs the item runtime):
     * JAMM injects `jamm_api_init()` into `SaveManager` `Step_0` right **before** `if (!loaded)` (and after all `Create` events have created the `*Data` singletons).
     * `jamm_api_init()` ensures `obj_jamm_api` exists, checks whether any existing `*Data` object (`BoatData`, `RodData`, `LureData`, `ToolData`, `GearData`, `UpgradeData`) lacks `jamm_api_done`, runs `jamm_items_ensure_base()`, calls each `jamm_init_*()` script once, marks `jamm_api_done = 1` on each `*Data` instance, and triggers `Rod` `ev_user0` (`Other_10`) so custom upgrade `apply` scripts run immediately.
     * If a save-slot change calls `room_restart()` and recreates the `*Data` objects, `jamm_api_init()` automatically re-registers everything before the newly loaded save is applied.
   * When a package calls only `jamm_fish_register` directly and supplies no `jamm_init_*` script, `FishRuntime` injects `jamm_fish_register` and adds `jamm_catalog_*` when `CatalogData` exists. For fish-only ModKit initializers, `FishRuntime` installs a standalone `jamm_api_init()` at the top of `SaveManager` `Step_0` guarded by `CatalogData.jamm_api_done` or `SaveManager.jamm_api_done`.

> [!IMPORTANT]
> **Keep `jamm_init_*` calls deterministic and idempotent**: Register items with literal values and asset names (not random rolls), and use stable string keys like `"mymod:raft"`. Every `jamm_*_register` function returns the existing slot if called twice with the same key (or object, for fish/catalog).

---

## Standalone Compiling & Placeholders

Because your `.win` mod doesn't contain JAMM's runtime implementations until JAMM merges it, UndertaleModTool needs script definitions for any `jamm_*` functions your mod calls so it can compile standalone:

* Ship minimal placeholder scripts with the exact function names:
  ```gml
  // Placeholder replaced by JAMM during profile build:
  return -1;
  ```
* At build/merge time, JAMM replaces all `jamm_*` API script bodies with the real runtime implementations.
* When two mods ship identical `return -1;` placeholders, JAMM treats the overlap as harmless (`INFO`) rather than a conflict.

## Reserved Namespaces

Avoid naming your own assets or INI sections with these prefixes:

* **`jamm_`** — reserved for JAMM scripts (`jamm_init_*`, `jamm_api_init`, `jamm_*_register`, `jamm_item_find`, `jamm_upgrade_stacks`, `jamm_items_*`, `jamm_catalog_*`, `jamm_hud_*`) and JAMM instance variables (`jamm_key`, `jamm_base_count`, `jamm_api_done`, `jamm_upg`, etc.).
* **`obj_jamm_`** — reserved for JAMM runtime controller objects (`obj_jamm_api`, `obj_jamm_console`, etc.).
* **`jmk_`** — reserved for ModKit-generated sprites, objects, and hook scripts (`jmk_<id>_<key>…`).
* **`JAMM_`** — reserved for JAMM save INI sections such as `[JAMM_Items]`, `[JAMM_Catalog]` and `[JAMM_Areas]`.

## Automatic Asset Relinking Across Mods

When multiple mods are merged, sprite, object, and script numeric indices shift. JAMM's `CgfAssetTypes` registers typed argument slots so asset arguments decompile by name and relink cleanly across mods:

* `jamm_boat_register`: `disp_sprite` (`sprites`), `boat_object` (`objects`)
* `jamm_rod_register`: `disp_sprite` (`sprites`)
* `jamm_lure_register`: `item_object` (`objects`), `disp_sprite` (`sprites`)
* `jamm_tool_register`: `item_object` (`objects`), `message_object` (`objects`), `disp_sprite` (`sprites`)
* `jamm_hat_register`: `gear_object` (`objects`)
* `jamm_upgrade_register`: `icon_sprite` (`sprites`), `apply_script` (`scripts`), `step_script` (`scripts`)

Always pass real asset identifiers (or `-1` where allowed), never raw hardcoded integers.

---

## Registration & Lookup Functions

### 1. `jamm_boat_register`

```gml
jamm_boat_register(key, name, cost, disp_sprite, boat_object, desc [, unlocked])
```

Appends a boat to `BoatData` (`name`, `cost`, `disp`, `object`, `desc`, `unlocked`, `jamm_key`, `jamm_default_unlocked`, `nItems`).

| Arg | Parameter | Type | Default | Description |
| :---: | :--- | :--- | :---: | :--- |
| 0 | `key` | string | Required | Non-empty unique key (e.g. `"mymod:raft"`). |
| 1 | `name` | string | Required | Shop display name. |
| 2 | `cost` | real | Required | Price in dollars (`>= 0`). |
| 3 | `disp_sprite` | sprite / `-1` | Required | Shop card sprite, or `-1` for none. |
| 4 | `boat_object` | object | Required | Boat object (should inherit `Boat` and initialize `move_speed`, `fish_limit`, `MAX_X`, etc.). |
| 5 | `desc` | string | Required | Shop description (`#` for line breaks). |
| 6 | `unlocked` | real | `0` | `0` = purchasable in shop, `1` = owned at start, `-1` = hidden. |

* **Returns**: Slot index (`>= 0`), existing slot if `key` is already registered (idempotent), or `-1` on invalid input / missing `BoatData`.
* **Example**:
  ```gml
  // In jamm_init_mymod:
  jamm_boat_register("mymod:raft", "Wooden Raft", 15000, spr_raft_card, obj_raft, "A sturdy raft.#Holds 4 fish.", 0);
  ```

---

### 2. `jamm_rod_register`

```gml
jamm_rod_register(key, name, cost, cast_speed, rod_power, rod_power_flat, reel_speed, sinker_distance, disp_sprite [, upgrade_slots, unlocked])
```

Appends a rod to `RodData`, stores its open slot count in `RodData.jamm_slots[slot]`, and initializes its `UpgradeData.upg` / `UpgradeData.object` row (at least `8` columns, expanded up to `JAMM_MAX_ROD_SLOTS = 16`, with `UpgradeData.jamm_max_slots` tracking the highest column count in use; `-1` for unlocked slots, `-2` for locked slots). `RodCard` and `Rod` draw `RodData.disp[slot]` when `> -1`.

| Arg | Parameter | Type | Default | Description |
| :---: | :--- | :--- | :---: | :--- |
| 0 | `key` | string | Required | Non-empty unique key (e.g. `"mymod:bamboo"`). |
| 1 | `name` | string | Required | Rod display name. |
| 2 | `cost` | real | Required | Price in dollars (`>= 0`). |
| 3 | `cast_speed` | real | Required | Cast velocity (vanilla rods: `8..18`). |
| 4 | `rod_power` | real | Required | Tension multiplier (vanilla: `1.15..2.15`). |
| 5 | `rod_power_flat` | real | Required | Flat power bonus (vanilla: `0..5`). |
| 6 | `reel_speed` | real | Required | Reel speed (vanilla: `8..12`). |
| 7 | `sinker_distance` | real | Required | Sinker depth limit (vanilla: `1200..7500`). |
| 8 | `disp_sprite` | sprite / `-1` | Required | Custom rod sprite, or `-1` to fall back to `spr_rod`. |
| 9 | `upgrade_slots` | int | `8` | `0..16` (`JAMM_MAX_ROD_SLOTS = 16`) explicit slot count (default `8`), or `-1` for vanilla progression (best existing rod's slots `+ 1`, clamped to `16`). |
| 10 | `unlocked` | real | `0` | `0` = purchasable in shop, `1` = owned at start, `-1` = hidden. |

* **Choosing `upgrade_slots` (simple vs. vanilla rule)**:
  * **Simple: `8` slots (default) or explicit `0..16`**: Omit `upgrade_slots` (or pass `8`) to match the vanilla Master Rod cap, or pass an explicit count in `0..16`.
  * **Vanilla rule (`-1`)**: In the unmodded game, each rod has one more upgrade slot than the previous one (`3, 4, 5, 6, 7, 8`). Pass `-1` to follow that progression: your rod receives `(max slot count of all rods registered or existing before it) + 1`, clamped to `16` (so the first `-1` rod after vanilla gets `9`, the next gets `10`, and so on).
* **Returns**: Slot index (`>= 0`), existing slot if `key` is already registered, or `-1` on invalid input / missing `RodData`.
* **Example**:
  ```gml
  // In jamm_init_mymod:
  jamm_rod_register("mymod:bamboo", "Bamboo Rod", 25000, 13, 1.85, 4, 10.5, 4200, spr_bamboo_rod, 6, 0);
  ```

---

### 3. `jamm_lure_register`

```gml
jamm_lure_register(key, name, cost, item_object, disp_sprite, desc [, unlocked])
```

Appends a consumable/attachable lure to `LureData` and initializes `Girl.gear_lure_scalar[slot] = 1` and `Gear.lure_scalar[slot] = 1`.

| Arg | Parameter | Type | Default | Description |
| :---: | :--- | :--- | :---: | :--- |
| 0 | `key` | string | Required | Non-empty unique key (e.g. `"mymod:glow_bobber"`). |
| 1 | `name` | string | Required | Shop display name. |
| 2 | `cost` | real | Required | Price per purchase (`>= 0`). |
| 3 | `item_object` | object | Required | `PowerUp` child object spawned by `buy_lure()`. |
| 4 | `disp_sprite` | sprite / `-1` | Required | Shop card sprite, or `-1` for none. |
| 5 | `desc` | string | Required | Shop description (`#` for line breaks). |
| 6 | `unlocked` | real | `1` | `1` = available in shop (default for lures), `-1` = hidden. |

* **Returns**: Slot index (`>= 0`), existing slot if `key` is already registered, or `-1` on invalid input / missing `LureData`.
* **Example**:
  ```gml
  // In jamm_init_mymod:
  jamm_lure_register("mymod:glow_bobber", "Glow Bobber", 1500, obj_glow_bobber, spr_glow_bobber_card, "Lights up dark depths.", 1);
  ```

---

### 4. `jamm_tool_register`

```gml
jamm_tool_register(key, name, cost, quests, item_object, message_object, disp_sprite, desc [, unlocked])
```

Appends a toggleable tool to `ToolData`. If no instance of `item_object` exists in the world yet, `jamm_tool_register` creates one at `(0, 0)` and initializes `active = 0` if unset. `ToolCard` draws `ToolData.disp[slot]` when `> -1`.

| Arg | Parameter | Type | Default | Description |
| :---: | :--- | :--- | :---: | :--- |
| 0 | `key` | string | Required | Non-empty unique key (e.g. `"mymod:depth_gauge"`). |
| 1 | `name` | string | Required | Shop display name. |
| 2 | `cost` | real | Required | Purchase price (`>= 0`). |
| 3 | `quests` | real | Required | Minimum completed quests (`Girl.counter[5]`) required (`>= 0`). |
| 4 | `item_object` | object | Required | Tool controller object (uses `.active` `0`/`1`). |
| 5 | `message_object` | object / `-1` | Required | Popup message object shown on toggle, or `-1` for none. |
| 6 | `disp_sprite` | sprite / `-1` | Required | Shop card icon sprite, or `-1` to fall back to `spr_tools`. |
| 7 | `desc` | string | Required | Shop description (`#` for line breaks). |
| 8 | `unlocked` | real | `0` | `0` = visible in shop, `1` = owned, `-1` = hidden. |

* **Returns**: Slot index (`>= 0`), existing slot if `key` is already registered, or `-1` on invalid input / missing `ToolData`.
* **Example**:
  ```gml
  // In jamm_init_mymod:
  jamm_tool_register("mymod:depth_gauge", "Depth Gauge", 12000, 5, obj_depth_gauge, -1, spr_depth_gauge_icon, "Displays lure depth on screen.", 0);
  ```

---

### 5. `jamm_hat_register`

```gml
jamm_hat_register(key, gear_object, value, weight, range_min, range_max [, offset_x, offset_y, mythic])
```

Appends a hat to `GearData`, sets `unlocked[slot] = 0`, and rebuilds the `GearData.map[]` display permutation sorted by `weight - (mythic ? 10 : 0)`.

> [!NOTE]
> `offset_x` and `offset_y` here set `GearData.offset_x` / `GearData.offset_y` (used to nudge the icon inside **Hatalog**). The hat's position on the player's head (`EQUIP_OFFSET_X`, `EQUIP_OFFSET_Y`), its display `name`, and its `desc` live on `gear_object` itself (`Create_0`).

| Arg | Parameter | Type | Default | Description |
| :---: | :--- | :--- | :---: | :--- |
| 0 | `key` | string | Required | Non-empty unique key (e.g. `"mymod:crown"`). |
| 1 | `gear_object` | object | Required | Hat object inheriting from `Gear` (sets `name`, `desc`, `EQUIP_OFFSET_X/Y`, optional `Other_15` effect). |
| 2 | `value` | real | Required | Base sell value (`>= 0`). Hatalog price is `3 * value` (or `1 * value` if `weight == 0`). |
| 3 | `weight` | real | Required | Ocean drop weight (`>= 0`; `0` = Hatalog-only). |
| 4 | `range_min` | real | Required | Normalized minimum ocean spawn X (`0..1`). |
| 5 | `range_max` | real | Required | Normalized maximum ocean spawn X (`0..1`). |
| 6 | `offset_x` | real | `0` | Hatalog icon X offset (`GearData.offset_x`). |
| 7 | `offset_y` | real | `0` | Hatalog icon Y offset (`GearData.offset_y`). |
| 8 | `mythic` | real | `0` | `1` for mythic (level 45 Hatalog gate), `0` normal. |

* **Returns**: Slot index (`>= 0`), existing slot if `key` is already registered, or `-1` on invalid input / missing `GearData`.
* **Example**:
  ```gml
  // In jamm_init_mymod:
  jamm_hat_register("mymod:crown", obj_crown_hat, 2500, 5, 0.1, 0.8, 0, -4, 0);
  ```

---

### 6. `jamm_upgrade_register`

```gml
jamm_upgrade_register(key, name, desc, desc2, cost, icon_sprite, max_stacks, apply_script, step_script)
```

Appends a custom rod upgrade to `UpgradeData` (dynamically at `array_length_1d(UpgradeData.unlocks)`, never assuming index `18`), sets `unlocks[slot] = 1`, marks `jamm_is_api[slot] = 1`, updates `UpgradeData.nItems`, and initializes `Girl.gear_upgrade_scalar` / `Gear.upgrade_scalar`.

**All 9 arguments are required.** Pass `-1` for `icon_sprite`, `max_stacks`, `apply_script`, or `step_script` when not used.

| Arg | Parameter | Type | Description |
| :---: | :--- | :--- | :--- |
| 0 | `key` | string | Non-empty unique key (e.g. `"mymod:turbo"`). |
| 1 | `name` | string | Upgrade menu title. |
| 2 | `desc` | string | Long hover tooltip in `UpgradeCard`. |
| 3 | `desc2` | string | Short description shown inside equipped `UpgradeSlot`. |
| 4 | `cost` | real | Cost multiplier (`>= 0`) relative to `RodData.cost[rod_id]` (e.g. `0.35`). |
| 5 | `icon_sprite` | sprite / `-1` | Icon sprite (origin `0,0`, scaled to `spr_upgradeicons`), or `-1` for default icon. |
| 6 | `max_stacks` | int / `-1` | Positive integer `>= 1` max stacks per rod, or `-1` for no extra limit (up to the rod's slot count, max `16`). |
| 7 | `apply_script` | script / `-1` | Script called by `Rod` `Other_10` with `argument0 = stacks` (`> 0`), or `-1`. |
| 8 | `step_script` | script / `-1` | Script called every `Begin Step` by `obj_jamm_api` with `argument0 = stacks` (`> 0`), or `-1`. |

* **Returns**: Slot index (`>= 0`), existing slot if `key` is already registered, or `-1` on invalid input / missing `UpgradeData`.
* **Idempotency note**: `Rod` `Other_10` resets `Rod.upgrades[]` and `Fish.base_lunge_mod`/`base_lunge_offset` before running `apply_script(stacks)`, so `apply_script` hooks using `+=` or `*=` on those fields never double-stack.
* **Example**:
  ```gml
  // In jamm_init_mymod:
  jamm_upgrade_register("mymod:turbo", "Turbo Reel", "Reel speed +2.0 per stack.", "Faster reeling.", 0.35, spr_turbo_icon, 3, scr_turbo_apply, -1);

  // In scr_turbo_apply:
  Rod.upgrades[1] += 2.0 * argument0;
  ```

---

### 7. `jamm_fish_register`

```gml
jamm_fish_register(key, object, size_tab, name, desc, x1, x2, y1, y2, count [, desc_caught, desc_seen, desc_hidden, desc_n, desc_e, exotic])
```

Spawns missing instances of `object` inside `[min(x1,x2)..max(x1,x2)] x [min(y1,y2)..max(y1,y2)]` (initializing `start_x`, `start_y`, `TRUE_START_X`, `TRUE_START_Y`, `nothungry_x`, `nothungry_y`), registers the display name in `CatalogData.jamm_fish_names` (so `fish_object_to_name` returns `name` in catch popups and saves), and registers the fish in the Fishalog via `jamm_catalog_register`.

| Arg | Parameter | Type | Default | Description |
| :---: | :--- | :--- | :---: | :--- |
| 0 | `key` | string | Required | Non-empty string identifier (validated; identity in catalog is `object`). |
| 1 | `object` | object | Required | Fish object (child of `FishSmall`, `FishMedium`, `FishLarge`, or `FishHuge`). |
| 2 | `size_tab` | int (`0..3`) | Required | `0` small, `1` medium, `2` large, `3` huge. |
| 3 | `name` | string | Required | Display name in Fishalog and catch popups. |
| 4 | `desc` | string | Required | Base catalog description. |
| 5–6 | `x1, x2` | real | Required | Horizontal spawn range (auto-ordered `min..max`). |
| 7–8 | `y1, y2` | real | Required | Vertical spawn range (auto-ordered `min..max`; water surface is `y = 1244`). |
| 9 | `count` | real (`>= 0`) | Required | Target number of spawned instances (`floor(count) - instance_number(object)` spawned). |
| 10 | `desc_caught` | string | `desc` | Description after catching. |
| 11 | `desc_seen` | string | `desc` | Description after spotting. |
| 12 | `desc_hidden` | string | `"Little is known about this fish."` | Description before discovery. |
| 13 | `desc_n` | string | `desc` | Night description slot (`CatalogData.desc_n`). |
| 14 | `desc_e` | string | `desc` | Extra/exotic description slot (`CatalogData.desc_e`). |
| 15 | `exotic` | real | `0` | `1` for exotic, `0` normal. |

* **Returns**: Catalog slot index (`>= 0`) in `size_tab`, `0` if the archive has no `CatalogData`, or `-1` on invalid input / missing `CatalogData` instance. Safe across `room_restart()`: only missing instances (`count - instance_number(object)`) are spawned and `jamm_catalog_register` deduplicates by `object`.
* **Example**:
  ```gml
  // In jamm_init_mymod:
  jamm_fish_register("mymod:sunfin", obj_sunfin, 0, "Sunfin", "A sunny reef fish.", 1100, 2400, 1400, 1950, 4, "Caught Sunfin!");
  ```

---

### 8. `jamm_catalog_register` (plus `jamm_catalog_find`, `jamm_catalog_area`)

```gml
jamm_catalog_register(area, object, name, desc [, desc_caught, desc_seen, desc_hidden, desc_n, desc_e, exotic])
jamm_catalog_find(object)
jamm_catalog_area(object)
```

Registers a fish object in `CatalogData` without spawning instances (used directly in `CatalogData` `Create_0`, or automatically by `jamm_fish_register`). See **[Catalog API](../catalog-api.md)** for complete details.

| Arg | Parameter | Type | Default | Description |
| :---: | :--- | :--- | :---: | :--- |
| 0 | `area` | int (`0..3`) | Required | Size tab: `0` small, `1` medium, `2` large, `3` huge. |
| 1 | `object` | object | Required | Fish object asset. |
| 2 | `name` | string | Required | Display name. |
| 3 | `desc` | string | Required | Base description. |
| 4–9 | `desc_caught .. exotic` | string / real | `desc` / `"Little is known..."` / `0` | Optional Fishalog descriptions and exotic flag (`0`/`1`). |

* **Returns**:
  * `jamm_catalog_register`: Appended or existing slot index (`>= 0`) if `object` is already in `area`; `-1` on invalid input or if `object` is already registered in a different `area`.
  * `jamm_catalog_find(object)`: Slot index (`>= 0`) or `-1`.
  * `jamm_catalog_area(object)`: Size tab (`0..3`) or `-1`.
* **Example**:
  ```gml
  var slot;
  slot = jamm_catalog_register(1, obj_cave_eel, "Cave Eel", "Hides in deep crevices.");
  ```

---

### 9. `jamm_item_find`

```gml
jamm_item_find(type, key)
```

Looks up the runtime slot index of any registered base or mod item by its string key. Calls `jamm_items_ensure_base()` automatically so base keys (`"base:boat0"`, `"base:rod0"`, `"base:hat0"`, `"base:upgrade0"`, etc.) also resolve.

| Arg | Parameter | Type | Description |
| :---: | :--- | :--- | :--- |
| 0 | `type` | string | One of `"boat"`, `"rod"`, `"lure"`, `"tool"`, `"hat"`, `"upgrade"`. |
| 1 | `key` | string | Non-empty item key (e.g. `"mymod:raft"` or `"base:rod5"`). |

* **Returns**: Slot index (`>= 0`) in the corresponding `*Data` object, or `-1` if unknown, invalid, or the `*Data` instance does not exist.
* **Example**:
  ```gml
  var raft_id;
  raft_id = jamm_item_find("boat", "mymod:raft");
  if (raft_id > -1 && BoatData.unlocked[raft_id] > 0)
  {
      // Player owns the raft!
  }
  ```

---

### 10. `jamm_upgrade_stacks`

```gml
jamm_upgrade_stacks(key)
```

Returns how many copies of an upgrade are installed on the **currently equipped rod** (`Rod`), respecting `Rod.allowUpgrades` (for example Tophat sets `no_upgrades = 1` -> `allowUpgrades = 0`, so `jamm_upgrade_stacks` returns `0`).

| Arg | Parameter | Type | Description |
| :---: | :--- | :--- | :--- |
| 0 | `key` | string | Non-empty upgrade key (e.g. `"showcase-upgrades:turbo"` or `"base:upgrade3"`). |

* **Returns**: Integer stack count (`>= 0`); returns `0` if the key is unknown, invalid, `UpgradeData` / `Rod` doesn't exist, or `Rod.allowUpgrades == 0`.
* **Example**:
  ```gml
  // Inside a fish hook or lure script:
  var turbo_stacks;
  turbo_stacks = jamm_upgrade_stacks("showcase-upgrades:turbo");
  if (turbo_stacks > 0)
  {
      // React to the player's installed Turbo Reel stacks!
  }
  ```

## Weather API

Use [Weather API](weather.md) for `weathers[]`, `jamm_weather_current()`, `jamm_weather_name()`,
`jamm_weather_intensity()`, `jamm_weather_command()`, and weather lifecycle hooks.
