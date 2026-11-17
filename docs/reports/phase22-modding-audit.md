# Phase 22 — Modding API Audit (Items, Menus, Upgrades, Saves, and Fish)

Read-only audit of the decompiled GML of Cat Goes Fishing (legacy 2023 build) for designing collision-free item/fish registration APIs, name-keyed persistence (`[JAMM_Items]`), and scrollable shop/inventory menus.

---

## 1. Item Data Objects, Parallel Arrays, and Count Computations

Every item category stores its catalog in a singleton `*Data` object instantiated at startup by `ControlObjectSpawner` (`gml_Object_ControlObjectSpawner_Create_0.gml`), except `RodData` (which is placed in the room/persistent alongside `Rod`).

### 1.1 Boats — `BoatData` (`gml_Object_BoatData_Create_0.gml`)

* **Item count**: `nItems = array_length_1d(unlocked)` -> `8` (indices `0..7`: `"No Boat"` plus `Boat1`..`Boat7`).
* **Parallel arrays (length 8)**:
  * `unlocked[i]`: `1` (owned/available), `0` (visible in shop, purchasable), or `-1` (hidden from shop until prerequisite is met). Initial values: `unlocked[0] = 1`, `unlocked[1] = 0`, `unlocked[2] = 0`, `unlocked[3..7] = -1`.
  * `cost[i]`: purchase price (`0, 35000, 120000, 240000, 360000, 720000, 1500000, 5000000`).
  * `name[i]`: display string (`"No Boat"`, `"Basic Boat"`, `"Good Boat"`, `"Speed Boat"`, `"Dark Boat"`, `"Luxury Boat"`, `"Collier"`, `"Tech Boat"`).
  * `object[i]`: object asset index (`-1` for slot `0`, `608..614` for `Boat1`..`Boat7`).
  * `disp[i]`: preview sprite asset index (`434..441`).
  * `desc[i]`: multi-line `#`-delimited description string.
* **Dynamic unlock logic** (`gml_Object_BoatData_Step_0.gml`):
  * Loops `i = 1..6`: if `unlocked[i - 1] > 0`, clamps `unlocked[i] = max(0, unlocked[i])`.
  * Slot `7` (Tech Boat) unlocks (`unlocked[7] = 0`, spawns `Message_TechBoat`) when `CatalogData.count[3, 9] > 0 && CatalogData.count[0, 41] > 0`.

### 1.2 Rods — `RodData` (`gml_Object_RodData_Create_0.gml`)

* **Item count**: Hardcoded `nItems = 6` (indices `0..5`). Note that `unlocked[6] = -2` and `unlocked[7] = -2` are also assigned in `Create_0`, making `array_length_1d(unlocked) == 8` while `nItems == 6`.
* **Parallel arrays (indices `0..5`)**:
  * `unlocked[i]`: `1` at `[0]`, `0` at `[1..2]`, `-1` at `[3..5]`, `-2` at `[6..7]`.
  * `cost[i]`: `100, 750, 10000, 35000, 180000, 320000`.
  * `sinker_distance[i]`: `1200, 2400, 3600, 4300, 6000, 7500`.
  * `name[i]`: `"Basic Rod"`, `"Good Rod"`, `"Power Rod"`, `"Quick Rod"`, `"Great Rod"`, `"Master Rod"`.
  * `cast_speed[i]`: `8, 10, 12, 18, 13.75, 14`.
  * `rod_power[i]`: `1.15, 1.52, 1.75, 1.4, 1.95, 2.15`.
  * `rod_power_flat[i]`: `4, 3.35, 0, 5, 5, 0`.
  * `reel_speed[i]`: `8, 9, 9.5, 12, 10, 11`.
* **No `disp[]` or `object[]` arrays**: `RodCard` draws `spr_rod` with `image_index = item_id`, and `Rod` sets `image_index = rod_id` (using `spr_rod`).
* **Stat-bar normalization scalars**: `min_reel = 5`, `max_reel = 15`, `min_speed = 5`, `max_speed = 18`, `min_power = 0.8`, `max_power = 2.9`.

### 1.3 Lures — `LureData` (`gml_Object_LureData_Create_0.gml`)

* **Item count**: `nItems = array_length_1d(unlocked)` -> `6` (indices `0..5`: Rocket, Bomb, Lightbulb, Huge Hook, Mega Hook, Trawler).
* **Parallel arrays (length 6)**:
  * `unlocked[i]`: `1` at `[0..1]`, `-1` at `[2..5]`. (`1` means available to purchase repeatedly; lures are consumables/per-cast attachments, not permanent one-time unlocks).
  * `cost[i]`: `400, 200, 1000, 5000, 50000, 100000` (may be overridden by `Analytics_Other_11` AB group `[2]`).
  * `name[i]`: `"Rocket"`, `"Bomb"`, `"Lightbulb"`, `"Huge Hook"`, `"Mega Hook"`, `"Trawler"`.
  * `item[i]`: `PowerUp` child object asset (`288` = Rocket, `289` = Bomb, `295` = Lightbulb, `293` = BigHook, `294` = BigHook2, `291` = Net).
  * `disp[i]`: preview sprite asset (`352, 353, 356, 357, 358, 355`).
  * `desc[i]`: `#`-delimited description string.
* **Dynamic unlock logic** (`gml_Object_LureData_Step_0.gml`): sets `unlocked[5] = 1` when `instance_exists(Boat) && Girl.level >= 27`, else `-1`. Other slots (`2`, `3`, `4`) are unlocked by catching/seeing Bulbfish, Shark, or reaching level 40.

### 1.4 Tools — `ToolData` (`gml_Object_ToolData_Create_0.gml`)

* **Item count**: Hardcoded `nItems = 3` (indices `0..2`: Minimap, Sonar, Deep Sonar). Like `RodData`, `unlocked` is padded to 5 entries (`unlocked[3] = -1`, `unlocked[4] = -1`).
* **Parallel arrays (indices `0..2`)**:
  * `unlocked[i]`: `0` at `[0..1]` (available once quest count & cash met), `-1` at `[2..4]`.
  * `cost[i]`: `5000, 125000, 1200000` (`cost[1]` overridden to `20000` by `Analytics_Other_11` AB group `[3]`).
  * `quests[i]`: quest-count requirement `10, 20, 30` (compared against `Girl.counter[5]`).
  * `name[i]`: `"Minimap"`, `"Sonar"`, `"Deep Sonar"`.
  * `item[i]`: tool controller object asset (`428` = `Minimap`, `430` = `SonarPanel`, `429` = `SonarPanelAdvanced`).
  * `message[i]`: message popup object asset (`404` = `Message_Minimap`, `405` = `Message_Radar`, `406` = `Message_DeepSonarEnabled`).
  * `desc[i]`: `#`-delimited description string.
* **No `disp[]` array**: `ToolCard` draws `spr_tools` with `image_index = item_id`.

### 1.5 Upgrades — `UpgradeData` (`gml_Object_UpgradeData_Create_0.gml`)

* **Item count**: No `nItems` field exists. There are **18** hardcoded upgrades (`0..17`).
* **Per-upgrade parallel arrays (indices `0..17`)**:
  * `unlocks[i]`: initialized `0` for `0..17`, then `unlocks[1] = 1` (`"Diving Lure"`).
  * `name[i]`: upgrade display name (`"Scouting"` .. `"Flick"`).
  * `desc[i]`: long tooltip description shown on hover in `UpgradeCard`.
  * `desc2[i]`: short description shown inside equipped `UpgradeSlot`.
  * `cost[i]`: cost multiplier relative to rod cost (`0.05` .. `1.2`).
* **Other fields**:
  * `cost_base[0..5]`: copied from `RodData.cost[0..5]` (unused by `calculate_upgrade_cost`, which reads `RodData.cost[argument1]` directly).
  * **No `disp[]` array**: icons come from `spr_upgradeicons` at subimage `1 + upgrade_id`.
* **Per-rod 6x8 slot matrix**:
  * `upg[ix, iy]` (`ix = 0..5`, `iy = 0..7`): `-1` for available empty slots (`3 + ix` slots on rod `ix`, i.e., rod `0` has `3` slots and rod `5` has `8` slots); `-2` for locked/unavailable slots.
  * `object[ix, iy]` (`ix = 0..5`, `iy = 0..7`): all initialized to `-1`. Used only by Attract (`4`) and Repel (`5`) to store the target `Fish` `object_id`.

### 1.6 Gear / Hats — `GearData` (`gml_Object_GearData_Create_0.gml`)

* **Item count**: Hardcoded `n_gear = 30` (indices `0..29`).
* **Parallel arrays (indices `0..29`)**:
  * `unlocked[i]`: initialized `0` for all `i`, then `unlocked[0] = 1` (`Tophat`? No, slot `0` is `gear[0] = 460`).
  * `gear[i]`: `Gear` child object asset ID (`459..488`).
  * `value[i]`: base sell value (`1200..100000`). Hatalog purchase price is `value[i] * value_scalar` where `value_scalar` is `3` (or `1` if `weight[i] == 0`).
  * `weight[i]`: spawn weight (`0, 1, 2, 5, 10, 12`). Indices `25, 26, 28, 29` have `weight = 0`.
  * `range_min[i]`, `range_max[i]`: normalized horizontal spawn range (`0..1`, except index `25` at `100..100`).
  * `offset_x[i]`, `offset_y[i]`: initialized `0`; `offset_y` tweaked per hat for Hatalog icon centering.
  * `mythic[i]`: initialized `0`; `1` for mythic hats (indices `1, 11, 15, 16, 20, 21, 22, 23, 27`).
* **Display sorting permutation**: `map[0..29]` built in `GearData_Create_0` by sorting descending on `weight[j] - (mythic[j] ? 10 : 0)`.
* **Name, description, sprite**: NOT stored in `GearData`; stored on each hat object (`name`, `desc`, `sprite_index`). `Hatalog_Create_0` instantiates a temporary offscreen hat at `(-500, -500)` to read these three properties, then destroys it.

---

## 2. Menu Layout, Card Lifecycle, Click Handling, and Viewport Capacity

### 2.1 Parent `Handler` & `Buttons` Infrastructure

* **Opening**: `icon_Boats`, `icon_Lures`, `icon_Tools`, `icon_Hats`, `icon_HatsNew` check `if (!instance_exists(Handler))` in `Other_10` and spawn their menu at `(ORIGIN_X, ORIGIN_Y)`. `icon_Rods_Other_10` spawns `RodMenu` unconditionally.
* **`Handler`** (`gml_Object_Handler_Create_0.gml`, `gml_Object_Handler_Destroy_0.gml`):
  * On Create: sets `Rod.swing_locked = 1` and `Boat.moveLocked = 1`.
  * On Destroy: destroys all `Card` instances (`with (Card) instance_destroy()`), restores `Rod.swing_locked = 0` and `Boat.moveLocked = 0`.
  * `icon_Cancel_Other_10` destroys all `Handler` instances.
* **`Buttons`** (`gml_Object_Buttons_Create_0.gml`, `Mouse_4`, `Mouse_7`, `Draw_0`):
  * Left press (`Mouse_4`) sets `clicked = 1` if `visible`.
  * Left release (`Mouse_7`) fires `event_perform(ev_other, ev_user0)` (`Other_10`) and clears `clicked = 0`.
  * `Card` inherits from `Buttons`, so clicking any card triggers its `Other_10`.

### 2.2 `CardHandler` & `Card` (Boats, Rods, Lures, Tools)

* **`CardHandler_Create_0.gml`**:
  * Inherits `Handler`. Anchors at `x = icon_Rods.ORIGIN_X; y = icon_Rods.ORIGIN_Y;` (`ORIGIN_X = x; ORIGIN_Y = y;`).
  * `DELAY = 2` frames; `X_GAP = 160`; `Y_GAP = 230`; `card_width = 3`; `iCard = 0`; `done_spawning = 0`; `alarm[0] = DELAY`.
  * `BoatMenu_Create_0` overrides `card_width = 4` (with `data_object = 257`, `card_type = 332`).
  * `RodMenu_Create_0`: `data_object = 255`, `card_type = 331`, `card_width = 3`.
  * `LureMenu_Create_0`: `data_object = 256`, `card_type = 336`, `card_width = 3`.
  * `ToolMenu_Create_0`: `data_object = 258`, `card_type = 337`, `card_width = 3`.
* **`CardHandler_Alarm_0.gml`** (staggered card spawning):
  * Creates `r = instance_create(x, y, card_type)` with `r.item_id = iCard`.
  * Wraps row via `if (((iCard + 1) % card_width) == 0) { x = ORIGIN_X; y += Y_GAP; } else { x += X_GAP; }`.
  * Increments `iCard++`, then `while (iCard < data_object.nItems && data_object.unlocked[iCard] < 0) iCard++;`.
  * Schedules `alarm[0] = DELAY` while `iCard < data_object.nItems`, else `done_spawning = 1`.
  * **Existing wrapping bug**: Row wrapping tests `(iCard + 1) % card_width` instead of counting actually spawned cards. If any earlier index has `unlocked[i] < 0` (skipped), `x` only advances per spawned card while the modulo uses the raw `iCard`, misaligning subsequent rows.
* **`CardHandler_Destroy_0.gml`**: Calls `action_inherited()`, destroys all `Card` instances, and sets `Rod.swing_locked = 0`.
* **`Card` (`Create_0`, `Step_0`, `Draw_0`)**:
  * `Create_0`: Stores `ORIGIN_X = x - Girl.x; ORIGIN_Y = y - Girl.y;` and offsets `x += 2000` for slide-in animation.
  * `Step_0` / `Draw_0`: Computes `target_x = ORIGIN_X + Girl.x; target_y = ORIGIN_Y + Girl.y;`, eases `x` left by `0.5 * (target_x - x)`, snaps `y = target_y`. `Draw_0` (on `view_current == 0`) runs `ev_user1` (`Other_11`) to draw card contents.
* **Screen capacity at ~1280x720 (GUI / view coordinates)**:
  * `Girl_Create_0.gml` sets `display_set_gui_size(800, 480)`. `Hatalog` and `Fishalog` span `width = 600, height = 340` starting at `icon_Rods.ORIGIN_X + 40, icon_Rods.ORIGIN_Y + 40..50`.
  * With `Y_GAP = 230`, **2 rows** occupy `460` view units vertically, filling the `~480`-unit view height:
    * `card_width = 3` (`RodMenu`, `LureMenu`, `ToolMenu`): **6 cards max** (`3 x 2`). Note `RodMenu_Draw_0` draws help text at `(ORIGIN_X + 640, ORIGIN_Y + 220)` to the right of the 3-column grid (`3 * 160 = 480` wide).
    * `card_width = 4` (`BoatMenu`): **8 cards max** (`4 x 2`, `4 * 160 = 640` wide). Base already has 8 boats (`0..7`), completely filling 2 rows.
  * Any 3rd row starts at `ORIGIN_Y + 460`, clipping off the bottom of the screen and becoming unreachable/off-view (and colliding visually with `icon_Cancel`).
* **Click actions (`Other_10`)**:
  * `BoatCard_Other_10`: Requires `!instance_exists(Net) && (!instance_exists(Boat) || Boat.fish_count == 0)` (else `Message_BoatHasFish`). If locked (`!unlocked[item_id]`), checks `Girl.dollars >= cost`, sets `unlocked[item_id] = 1`, reveals up to 2 subsequent locked boats (`item_id + 1..2` if `< nItems - 1` and `== -1` -> `0`), deducts dollars, calls `boat_was_unlocked(item_id)`. If unlocked, destroys existing `Boat` and spawns `BoatData.object[item_id]` if `> 0`. Always destroys `BoatMenu`.
  * `RodCard_Other_10`: If `item_id == Rod.rod_id`, destroys `RodMenu` and opens `UpgradeSlotMenu`. Otherwise if locked, buys rod (revealing `item_id + 1..2` if `< nItems` and `== -1` -> `0`), calls `rod_was_unlocked(item_id)`, sets `closeMenu = 1`. If unlocked, equips `Rod.rod_id = item_id` and runs `Rod` `ev_user0` (`Other_10`). Destroys `RodMenu` if `closeMenu`.
  * `LureCard_Other_10`: Calls `buy_lure(item_id)`, which validates `Lure.state == 0 && icon_Lures.unlocked && LureData.unlocked[item_id]`, applies `Girl.gear_lure_scalar[item_id]`, handles Bomb/BigHook/BigHook2 stacking and rock/fish checks, spawns `LureData.item[item_id]`, calls `lure_was_purchased(item_id)`, and destroys `LureMenu`.
  * `ToolCard_Other_10`: If locked, checks `Girl.counter[5] >= ToolData.quests[item_id]` (else `Message_MoreQuests`) and `Girl.dollars >= ToolData.cost[item_id]` (else `Message_MoreMoney`), sets `unlocked[item_id] = 1` and calls `tool_was_purchased(item_id)`. If unlocked, toggles `ToolData.item[item_id].active = !active` and spawns `ToolData.message[item_id]`. Destroys `ToolMenu`.

### 2.3 `RowHandler` (`UpgradeSlotMenu` & `UpgradeMenu`)

* **`RowHandler_Create_0.gml`**: Sets `x = icon_Rods.ORIGIN_X; y = icon_Rods.ORIGIN_Y; DELAY = 2; X_GAP = 325; Y_GAP = 85; card_width` not used (`% 2`).
* **`UpgradeSlotMenu_Alarm_0.gml`**: Spawns up to 8 `UpgradeSlot` cards (`iCard = 0..7`) in 2 columns (`X_GAP = 325, Y_GAP = 85` -> 4 rows = `340`px), skipping slots where `UpgradeData.upg[Rod.rod_id, iCard] == -2`.
  * `UpgradeSlot_Other_10`: If slot occupied (`!= -1`), first click sets `confirmDestroy = 1`; second click clears `upg` and `object` to `-1` and calls `upgrade_was_destroyed()`. If empty (`-1`), stores `Rod.slot_id = slot_id`, destroys `UpgradeSlotMenu`, and opens `UpgradeMenu`.
* **`UpgradeMenu_Create_0.gml` & `Alarm_0.gml`**: Overrides `Y_GAP = 40`, defines `spawn_order[0..17]`, and iterates `iCard = 0..17` (hardcoded `<= 17`), spawning `UpgradeCard` (`image_yscale = 0.45`) in 2 columns (`spawns % 2 == 0`) for each `UpgradeData.unlocks[spawn_order[iCard]] != 0`.
  * **Capacity**: 2 columns x `Y_GAP = 40`: `18` upgrades = **9 rows** (`360`px). More than ~10-11 rows will overflow the bottom of the 480-unit view.
  * `UpgradeCard_Other_10`: Validates per-type limits, `Girl.dollars >= cost`, Glitter/Stealth mutual exclusion, and hooked fish for Attract/Repel; writes `UpgradeData.upg[Rod.rod_id, Rod.slot_id] = card_id` (and `object` if 4/5), calls `upgrade_was_purchased(card_id)`, destroys `UpgradeMenu`, and reopens `UpgradeSlotMenu`.

### 2.4 `GearMenu` (Hat Swap Popup) and `Hatalog` (Hat Shop / Scroll Surface)

* **`GearMenu`** (`gml_Object_GearMenu_Create_0.gml`, `Alarm_0.gml`, `Draw_0.gml`): Not a list menu—it is the 2-card swap dialog shown when reeling in `Lure.gear` while wearing `Girl.gear`. Spawns `GearCard` for `Girl.gear` (`equipped = 1`) and `Lure.gear` (`equipped = 0`). Clicking `GearCard` (`Other_10`) equips `Lure.gear` (`gear_equip()`) or destroys it (`instance_destroy()`, selling it) and closes `Handler`. `GearCardBlank` calls `gear_destroy()` to unequip.
* **`Hatalog`** (`gml_Object_Hatalog_Create_0.gml`, `Draw_0.gml`, `Step_0.gml`, `Catalog_Destroy_0.gml`):
  * Inherits `Catalog` (which inherits `Handler`).
  * Positioned at `(icon_Rods.ORIGIN_X + 40, icon_Rods.ORIGIN_Y + 50)` with `width = 600, height = 340`.
  * Creates 1 `CatalogTab` (`"Hats"`), scrollbar controls (`CatalogUp`, `CatalogDown`, `CatalogHandle`), `CatalogSurface` (`surfw = 566, surfh = 295`, `surfx = 4, surfy = 42`), and surface `csurf` (note `Hatalog_Draw_0` fallback recreate uses `570 x 274`).
  * Iterates `0..GearData.n_gear - 1` via `GearData.map[j]` and creates a `HatalogItem` for the currently equipped hat (first) and every unlocked hat (`GearData.unlocked[i]`).
  * Completion percentage divides by `total_hats - disabled_hats` where `disabled_hats = 3` is hardcoded.
  * **Scrolling**: `Hatalog_Draw_0` sets `py = scroll_y`, performs `ev_user1` (`Other_11`) on each `HatalogItem` (drawing at `Catalog.py` onto `csurf` and incrementing `Catalog.py += sprite_height`), then updates `scroll_height = py - scroll_y - HatalogItem.sprite_height`. `Hatalog_Step_0` clamps `scroll_y` with spring velocity (`scroll_vel`) between `0` and `-scroll_height`. `CatalogUp` / `CatalogDown` handle mouse wheel (`Mouse_60`/`Mouse_61`) and clicks (`Other_10`, `+/- 50`). `CatalogHandle` supports scrollbar dragging (`Mouse_4/50/56`, `Step_2`).
  * `HatalogItem_Other_10` unequips if `equipped`, or buys & equips for `value * value_scalar` (`3x` if `weight > 0`, `1x` if `weight == 0`, gated by `Girl.level >= 45` if `GearData.mythic[index]`), provided `Girl.gear < 0` (otherwise shows `Message_AlreadyHat`).

---

## 3. Hardcoded Numeric Index & Item References Outside `*Data`

### 3.1 Boats
* `gml_Object_Boat1_Create_0.gml` .. `gml_Object_Boat7_Create_0.gml`: each hardcodes `boat_index = 1` .. `7`.
* `gml_Object_Girl_Create_0.gml`: initializes `bestBoat = 0`.
* `gml_Script_boat_was_unlocked.gml`: updates `Girl.bestBoat` (`argument0 > Girl.bestBoat`) and calls `steam_set_achievement("boat_" + string(argument0))`.
* `gml_Script_start_quest_fish.gml` & `gml_Script_start_quest_fish_old.gml`: compute `quest_index` using `min(1, Girl.bestBoat)`.
* `gml_Object_Lightbulb_Step_2.gml`: checks `instance_exists(Boat4)` (Dark Boat halves lightbulb battery drain).
* `gml_Object_Great_Cusper_Other_10.gml`: checks `instance_exists(Boat7)` (Tech Boat attracts Great Cusper).
* `gml_Object_SaveManager_Other_13.gml` / `14` / `15`: `[Unlocks] boat<i>`, `[Main] boat`, `Girl.bestBoat`, `steam_set_achievement("boat_" + string(i))`.

### 3.2 Rods
* `gml_Object_Rod_Create_0.gml`: initializes `rod_id = 0`.
* `gml_Object_Rod_Other_10.gml`: reads `RodData.cast_speed[rod_id]`, `rod_power[rod_id]`, `rod_power_flat[rod_id]`, `reel_speed[rod_id]`, `sinker_distance[rod_id]`, sets `image_index = rod_id` (`spr_rod`), and indexes `UpgradeData.upg[rod_id, i]` & `UpgradeData.object[rod_id, i]`.
* `gml_Object_Rod_KeyPress_88.gml` (X) & `gml_Object_Rod_KeyPress_90.gml` (Z): cycle unlocked rods in `0..RodData.nItems - 1`. (Note: `Rod_KeyPress_88` has a base bug where `rid--` is used instead of `rid++` inside `while (rid < RodData.nItems)`).
* `gml_Object_RodCard_Other_11.gml`: draws `spr_rod` subimage `item_id`.
* `gml_Object_Rocket_Create_0.gml` & `gml_Object_Rocket_Step_2.gml`: defines `target_distance[0..5]` and indexes `target_distance[Rod.rod_id]`. **Will crash if `Rod.rod_id >= 6` without fallback.**
* `gml_Object_Girl_Create_0.gml`: initializes `bestRod = 0`.
* `gml_Script_rod_was_unlocked.gml`: updates `Girl.bestRod`, `Analytics.rod_index = argument0`, `Girl.counter[4]`, `steam_set_achievement("rod_" + string(argument0))`.
* `gml_Script_start_quest_dist.gml`: defines 6-element arrays `dist[0..5]` and `dpth[0..5]` indexed by `Girl.bestRod`, and `q.cash = (Girl.bestRod + 1) * 300`. **Will crash if `Girl.bestRod >= 6` without clamping/fallback.**
* `gml_Script_start_quest_rods.gml` & `gml_Script_new_quest.gml`: index `RodData.cost[Girl.bestRod + 1]` and check `Girl.bestRod == 0` / `< 2`.
* `gml_Script_start_quest_fish.gml` & `gml_Script_start_quest_fish_old.gml`: check `min(4, Girl.bestRod)` and `Girl.bestRod == 5`.
* `gml_Object_RodQuest_Step_0.gml`: `progress = Girl.bestRod`.
* `gml_Object_icon_NewQuest_Other_11.gml`: checks `Girl.bestRod == 0`.
* `gml_Object_Shark_Other_19.gml`: checks `if (Rod.rod_id == 0)` for achievement `"basic_shark"`.
* `gml_Object_UpgradeData_Create_0.gml`: reads `RodData.cost[0..5]` into `cost_base[0..5]`.
* `gml_Script_calculate_upgrade_cost.gml`: indexes `RodData.cost[argument1]` (`Rod.rod_id`).
* `gml_Object_SaveManager_Other_13.gml` / `14` / `15`: `[Unlocks] rod<i>`, `[Main] activerod`, `Girl.bestRod`, `steam_set_achievement("rod_" + string(i))`.

### 3.3 Lures
* `gml_Object_Girl_KeyPress_82.gml` (R), `66` (B), `76` (L), `72` (H): hotkeys calling `buy_lure(0)` (Rocket), `buy_lure(1)` (Bomb), `buy_lure(2)` (Lightbulb), `buy_lure(3)` (Huge Hook).
* `gml_Script_buy_lure.gml`: checks `argument0 == 1` (Bomb rock pal), `LureData.item[argument0] == 295` (Lightbulb), `289` (Bomb), `294` (BigHook2/Mega Hook), and `Girl.gear_lure_scalar[argument0]`.
* `gml_Object_LureCard_Other_11.gml`: indexes `Girl.gear_lure_scalar[item_id]`.
* `gml_Object_Girl_Other_10.gml` & `gml_Object_Gear_Other_15.gml`: `Gear_Other_15` initializes `lure_scalar[0..4]`; `Girl_Other_10` initializes `gear_lure_scalar[0..5]` when `gear < 0`, but only copies `gear.lure_scalar[0..4]` when `gear >= 0`. **Any lure at index `>= 5` (including Trawler if `gear >= 0`, or new mod lures `>= 6`) will crash `buy_lure` / `LureCard_Other_11` unless `gear_lure_scalar` is bounds-checked or dynamically sized.**
* `gml_Object_CowboyHat_Other_15.gml`, `gml_Object_Demolition_Other_15.gml`, `gml_Object_Mechanic_Other_15.gml`: write `lure_scalar[0]`, `lure_scalar[1]`, `lure_scalar[0..4]`.
* `gml_Object_Girl_Step_0.gml`: level 40 gate sets `LureData.unlocked[4] = 1` (Mega Hook).
* `gml_Object_Shark_Other_18.gml`: sets `LureData.unlocked[3] = 1` (Huge Hook).
* `gml_Object_Bulbfish_Other_19.gml`: sets `LureData.unlocked[2] = 1` (Lightbulb).
* `gml_Object_GearData_Other_10.gml`: checks `if (LureData.unlocked[2] <= 0)` (gates `gear[19]` spawning until Lightbulb is unlocked).
* `gml_Object_Analytics_Other_11.gml`: overrides `LureData.cost[0]` and `LureData.cost[1]` in AB group `[2]`.
* `gml_Script_lure_was_purchased.gml`: increments Steam stat `"lp_" + string(argument0 + 1)`, sets achievement `"lure_" + string(argument0)`, sets `Analytics.lure_index = argument0`, updates `LureQuest`.
* `gml_Script_start_quest_lure.gml` & `gml_Object_LureQuest_Draw_64.gml`: `LureQuest.type` indexes `LureData.name[type]` (`0` = Rocket, `1` = Bomb).
* `gml_Object_SaveManager_Other_13.gml` / `14` / `15`: `[Unlocks] lure<i>`.

### 3.4 Tools
* `gml_Object_ToolCard_Other_11.gml`: draws `spr_tools` subimage `item_id`.
* `gml_Object_Girl_Step_0.gml`: level 35 gate sets `ToolData.unlocked[2] = 0` (Deep Sonar) and creates `Message_DeepSonar`.
* `gml_Object_RadarButton_Step_0.gml`: checks `ToolData.unlocked[1] == 1` and `SonarPanel.active`.
* `gml_Object_icon_NewQuest_Other_11.gml`: loops `0..ToolData.nItems - 1` setting `ToolData.quests[i] = 0` when `CatalogData.all_quests_complete`.
* `gml_Object_Analytics_Other_11.gml`: overrides `ToolData.cost[1] = 20000` in AB group `[3]`.
* `gml_Script_tool_was_purchased.gml`: sets `Analytics.tool_index = argument0` and `steam_set_achievement("tool_" + string(argument0 + 1))`.
* `gml_Object_Minimap_Step_0.gml`, `gml_Object_SonarPanel_Create_0.gml` / `Step_0.gml` / `Step_1.gml` / `Draw_0.gml`, `gml_Object_SonarPanelAdvanced_Create_0.gml` / `Step_0.gml`, `gml_Object_Message_Minimap_Create_0.gml`, `gml_Object_Message_Radar_Create_0.gml`, `gml_Object_Message_DeepSonarEnabled_Create_0.gml`, `gml_Object_Dragon_Other_10.gml` / `Step_2.gml`, `gml_Object_Maskfish_Step_0.gml`, `gml_Object_Bulbfish_Other_16.gml`: reference `Minimap.active`, `SonarPanel.active` / `shouldDraw`, `SonarPanelAdvanced.active`.
* `gml_Object_SaveManager_Other_13.gml` / `14` / `15`: `[Unlocks] tool<i>`, sets `ToolData.item[i].active = 1`, `steam_set_achievement("tool_" + string(i + 1))`.

### 3.5 Upgrades
* `gml_Object_Girl_Step_0.gml`: 17 level/mode gates calling `UnlockUpgrade(id)` for IDs `0..16` (ID `17` Flick is unlocked in `gml_Script_line_snap.gml` when `Lure.touching_rock`).
* `gml_Object_UpgradeMenu_Create_0.gml` & `Alarm_0.gml`: hardcoded `spawn_order[0..17]` and loop bound `iCard <= 17`.
* `gml_Object_UpgradeSlotMenu_Alarm_0.gml`: hardcoded `iCard < 8`.
* `gml_Object_UpgradeData_Draw_0.gml`, `gml_Object_UpgradeCard_Other_11.gml`, `gml_Object_UpgradeSlot_Other_11.gml`: draw `spr_upgradeicons` subimage `1 + upgrade_id`.
* `gml_Object_Gear_Other_15.gml` & `gml_Object_Girl_Other_10.gml`: hardcoded `for (i = 0; i <= 17; i++)` for `upgrade_scalar` / `gear_upgrade_scalar`.
* `gml_Script_upgrade_was_purchased.gml`: sets `steam_set_achievement("upg_" + string(argument0))`.
* `gml_Object_Rod_Other_10.gml`: reads `UpgradeData.upg[rod_id, 0..7]`, `UpgradeData.object[rod_id, 0..7]`, populates `upgrades[0..21]`.
* `gml_Object_UpgradeCard_Other_10.gml`: checks `Rod.upgrades[...]` stack limits, writes `UpgradeData.upg[Rod.rod_id, Rod.slot_id]` and `UpgradeData.object[...]`.
* `gml_Object_Lure_Step_0.gml`, `Draw_0.gml`, `Keyboard_17.gml`, `gml_Script_fish_set_lunge_mod.gml`, `gml_Script_check_lunge.gml`, `gml_Script_buy_lure.gml`, `gml_Object_Bomb_Step_0.gml`, `Draw_0.gml`, `Other_10.gml`, `Mouse_54.gml`, `gml_Object_BombNoPal_Draw_0.gml`, `Other_10.gml`: read `Rod.upgrades[...]`.
* `gml_Object_SaveManager_Other_13.gml` / `14` / `15`: hardcoded `i <= 17`, `ix < 6`, `iy < 8`.

### 3.6 Gear / Hats
* `gml_Object_Girl_Create_0.gml`: `gear = -1; gear_id = -1;`.
* `gml_Object_Dig_Create_0.gml` (`gear_id = 26`), `gml_Object_Fennec_Create_0.gml` (`gear_id = 29`), `gml_Object_Explorer_Create_0.gml` (`gear_id = 25`), `gml_Object_Trucker_Create_0.gml` (`gear_id = 25`), `gml_Object_Witchy_Create_0.gml` (`gear_id = 25`): hardcode `gear_id` in their own `Create_0`.
* `gml_Object_GearData_Other_10.gml`: checks `gear[i] == 476` (night/level 16 spawn rule) and `true_weight[19] = 0` (`LureData.unlocked[2] <= 0`).
* `gml_Object_Gear_Create_0.gml`: special-cases `object_index != Explorer`.
* `gml_Object_Gear_Other_11.gml` / `Other_12.gml` / `Destroy_0.gml`: read/write `GearData.unlocked[gear_id]`, `GearData.weight[gear_id]`, `GearData.value[gear_id]`.
* `gml_Object_GearCard_Other_11.gml`, `gml_Script_gear_equip.gml`, `gml_Script_gear_destroy.gml`, `gml_Script_gear_spawn.gml`: use `gear_id`.
* `gml_Object_Hatalog_Create_0.gml`, `gml_Object_HatalogItem_Other_10.gml` / `Other_11.gml`: use `Girl.gear_id`, `GearData.n_gear`, `GearData.map[]`, `disabled_hats = 3`.
* `gml_Object_SaveManager_Other_13.gml` / `14` / `15`: `[Main] gear`, `[Hatalog] hat<i>` (`i < n_gear`), `meta.ini` `Hat` string (`Girl.gear.name`).

---

## 4. Persistence in `SaveManager` (`Other_13`, `Other_14`, `Other_15`)

* **Save files**:
  * `Other_13` (Load): Opens `"sdata" + string(loadflag) + "n_beta.ini"`. Falls back to `"sdata" + string(loadflag) + "n.ini"` (and creates `Message_NewSave`) if `[Main]` is missing or `saveflag == -56`.
  * `Other_14` (Save): Writes `"sdata" + string(saveflag) + "n_beta.ini"`, plus summary fields to `"meta.ini"` (`[save<N>]`: `nss`, `Used`, `Level`, `Dollars`, `Mode`, `Hat`).
  * `Other_15` (Backup save): Writes `"sdata" + string(saveflag) + "n-backup.ini"` and `"meta.ini"`.

### Exact INI Sections, Keys, and Loop Bounds

| Type | Section | Keys | Loop Bounds & Quirks |
| :--- | :--- | :--- | :--- |
| **Icons** | `Unlocks` | `object_get_name(object_index)` | `with (Icons)` — already name-keyed. |
| **Rods** | `Unlocks` | `rod<i>` (`i = 0..nItems-1`) | `for (i = 0; i < nItems; i++)`. On load, sets `Rod.rod_id = i` & `Girl.bestRod = i` for each truthy unlocked rod, triggers achievement `"rod_" + string(i)`, runs `Rod` `ev_user0`, then overrides `Rod.rod_id` from `[Main] activerod`. |
| **Rods (equipped)** | `Main` | `activerod` | Numeric `Rod.rod_id` (default `-1`). |
| **Boats** | `Unlocks` | `boat<i>` (`i = 0..nItems-1`) | `for (i = 0; i < nItems; i++)`. On load, updates `Girl.bestBoat = i` & achievement `"boat_" + string(i)`. |
| **Boats (equipped)** | `Main` | `boat` | `Other_14` writes `Boat.boat_index` (or `-1`); `Other_15` omits it. `Other_13` reads `bi` and spawns `BoatData.object[bi]` if `bi > 0 && BoatData.object[bi] > 0`. |
| **Lures** | `Unlocks` | `lure<i>` (`i = 0..nItems-1`) | `for (i = 0; i < nItems; i++)`. |
| **Tools** | `Unlocks` | `tool<i>` (`i = 0..nItems-1`) | `for (i = 0; i < nItems; i++)`. **Quirk**: On load, any truthy `unlocked[i]` forces `ToolData.item[i].active = 1` (does not persist toggle-off state) and sets achievement `"tool_" + string(i + 1)`. |
| **Upgrades (unlocks)** | `Upgrades` | `unlocks<i>` | **Hardcoded `for (i = 0; i <= 17; i++)`** in `Other_13`, `Other_14`, and `Other_15`! |
| **Upgrades (slots)** | `Upgrades` | `upg<ix><iy>`, `obj<ix><iy>` | **Hardcoded `for (ix = 0; ix < 6; ix++)` and `for (iy = 0; iy < 8; iy++)`** in `Other_13`, `Other_14`, and `Other_15`! Note `obj<ix><iy>` stores numeric `Fish` `object_index` (not name). |
| **Gear / Hats** | `Hatalog` | `hat<i>` (`i = 0..n_gear-1`) | `for (i = 0; i < n_gear; i++)`. |
| **Gear (equipped)** | `Main` | `gear` | Numeric `Girl.gear_id` (`-1` if none). On load, spawns `GearData.gear[gear_id]` and calls `gear_equip()`. |
| **Catalog** | `Catalog` | `f<i><j>`, `li<i><j>`, `pop<i><j>` | Already supplemented by `[JAMM_Catalog]` in Phase 16. |
| **OnBoat / LF / Fishes** | `OnBoat`, `LF`, `Fishes`, `Lamp` | `f<i>ind`, `f<i>id`, `f`, `fid`, `string(id)` | Store numeric GM `object_index` and `id` (pre-existing profile-switch limitation noted in `docs/catalog-api.md`). |

---

## 5. End-to-End Upgrade Architecture

1. **Data & Grid Semantics (`UpgradeData`)**:
   * `upg[rod_id, slot_id]` (`6 x 8`):
     * `-2`: slot locked/unavailable on that rod (`0`: 3 slots; `1`: 4 slots; `2`: 5 slots; `3`: 6 slots; `4`: 7 slots; `5`: 8 slots).
     * `-1`: available empty slot.
     * `0..17`: installed upgrade ID.
   * `object[rod_id, slot_id]` (`6 x 8`): `-1` unless `upg` is Attract (`4`) or Repel (`5`), in which case it stores the hooked fish's `object_id` (`Lure.fish.object_id`).
   * `unlocks[0..17]`: `0` (locked) or `1` (unlocked via `UnlockUpgrade(id)` in `Girl_Step_0` or `line_snap`).
2. **Upgrade Objects**:
   * Base upgrades `0..17` do **not** have per-upgrade item objects (`UpgradeData` only has `name`, `desc`, `desc2`, `cost`, `unlocks`, `upg`, `object`).
   * The only runtime object spawned for an upgrade is **`CastShield`** (parent `PowerUp`, `gml_Object_PowerUp_Create_0.gml` / `Step_2.gml`), created in `Lure_Step_0` at water entry when `Rod.upgrades[15]` is active. `PowerUp` tracks `Lure.x/y` using `ATTACH_DISTANCE` / `ATTACH_DISTANCE_VERT`, and `CastShield_Step_0` expires after `duration = 90` frames or immediately if `Lure.wasJustReeling`.
3. **Compilation into `Rod` (`gml_Object_Rod_Other_10.gml`)**:
   * Resets `upgrades[0..21]` to `0`, `attract[0..2]` & `repel[0..2]` to `0`, and counters (`scouts`, `weights`, `feathers`, `cranks`, `attracts`, `repels`, `weeds`, `sparks`, `n_upgrades`) to `0`.
   * Checks `with (Gear) if (state == 2 && no_upgrades) other.allowUpgrades = 0` (e.g., `Tophat_Other_15` sets `no_upgrades = 1`).
   * If `allowUpgrades`, loops `i = 0..7` over `UpgradeData.upg[rod_id, i]` and populates `Rod.upgrades[]` and counters, then copies `attract[0..2]` into `upgrades[5..7]` and `repel[0..2]` into `upgrades[8..10]`.
   * Triggers achievements (`"patience"`, `"impatience"`, `"finetune"`, `"bruteforce"`, `"attraction"`, `"repulsion"`).
   * Loops `with (Fish)` to recompute `base_lunge_mod` and `base_lunge_offset` from Attract (`upgrades[5..7]`), Repel (`upgrades[8..10]`), Glitterbait (`upgrades[11]`), Stealthbait (`upgrades[12]`), and active Gear scalars.
4. **Upgrade Table (`UpgradeCard_Other_10`, `Rod_Other_10`, & Consumers)**:

| `UpgradeData` ID | Name | `Rod.upgrades[]` Target(s) | Per-Rod Limit (`UpgradeCard_Other_10`) | Effect Application |
| :--- | :--- | :--- | :--- | :--- |
| `0` | Scouting | `upgrades[2] = 1`, `scouts++` | `Rod.scouts >= 1` | `Lure_Step_0`: disables `forced_reel` |
| `1` | Diving Lure | `upgrades[0] += 1`, `upgrades[1] -= 0.2`, `weights++` | `Rod.weights >= 8` | `Lure_Step_0`: sink rate `+1`, reel `-0.2`; `Lure_Draw_0`: `spr_weight` |
| `2` | Rising Lure | `upgrades[17] += 3`, `feathers++` | `Rod.feathers >= 2` | `Lure_Step_0`: rise speed when reeling; `Lure_Draw_0`: `spr_feather` |
| `3` | Improved Crank | `upgrades[1] += 1.4`, `cranks++` | None (8 slots max) | `Lure_Step_0`: `modified_reel_speed += upgrades[1]` |
| `4` | Attract | `upgrades[5..7]` via `attract[]`, `attracts++` | `Rod.attracts >= 3` (+ not in repel, requires `Lure.fish > 0`) | `Rod_Other_10`: `Fish.base_lunge_mod *= power(1.58, gear_attract_scalar)`, `+100` offset |
| `5` | Repel | `upgrades[8..10]` via `repel[]`, `repels++` | `Rod.repels >= 3` (+ not in attract, requires `Lure.fish > 0`) | `Rod_Other_10`: `Fish.base_lunge_mod *= power(0.5, gear_repel_scalar)`, `-60` offset |
| `6` | Crankbait | `upgrades[3] = 1` | `Rod.upgrades[3] == 1` | `fish_set_lunge_mod`: reduces lunge unless `wasJustReeling`; `Lure_Draw_0`: `spr_crankbait_emblem` |
| `7` | Bait Guard | `upgrades[4] = 1` | `Rod.upgrades[4] == 1` | `check_lunge`: blocks `size <= 1` on `Lure.size >= 3`; `Lure_Draw_0`: `spr_baitguard_dome` |
| `8` | Gauge | `upgrades[13] += 3` | `Rod.upgrades[13] >= 8` (max 3 stacks = 9) | `Lure_Step_0`: `MAX_TENSION += 120 * upgrades[13]`; `Lure_Draw_0`: thicker line |
| `9` | Glitterbait | `upgrades[11]++`, `sparks++` | `Rod.upgrades[11] >= 3` (+ `upgrades[12] == 0`) | `Rod_Other_10`: `base_lunge_mod *= power(1.2, upgrades[11])`; `Lure_Step_0`: glitter particles |
| `10` | Stealthbait | `upgrades[12]++`, `weeds++` | `Rod.upgrades[12] >= 3` (+ `upgrades[11] == 0`) | `Rod_Other_10`: `base_lunge_mod *= power(0.8, upgrades[12])`; `Lure_Draw_0`: `spr_stealthbait` |
| `11` | Retract | `upgrades[14] = 1` | `Rod.upgrades[14]` | `Lure_Step_0`: faster reel when `size <= 0` |
| `12` | Cast Shield | `upgrades[15] = 1` | `Rod.upgrades[15]` | `Lure_Step_0`: spawns `CastShield`; `fish_set_lunge_mod`: `lunge_mod = 0` while active |
| `13` | Detonator | `upgrades[16] = 1` | `Rod.upgrades[16]` | `Bomb_Mouse_54`: right-click detonates `Bomb` (`ev_user0`) |
| `14` | Bomb Stack | `upgrades[18] = 1` | `Rod.upgrades[18]` | `buy_lure`, `Bomb_*`, `BombNoPal_*`: stack up to 5 bombs, prevent auto-detonate on cast |
| `15` | Cast Finesse | `upgrades[19] = 1` | `Rod.upgrades[19]` | `Lure_Step_0`: hold button mid-cast to brake/lock line |
| `16` | Sinker | `upgrades[20] = 1` | `Rod.upgrades[20]` | `Lure_Step_0`: sink vertically below cast arc up to `Rod.sinker_distance + gear_sinker_mod`; `Lure_Draw_0`: `spr_sinkerhang` |
| `17` | Flick | `upgrades[21] = 1` | `Rod.upgrades[21]` | `Lure_Keyboard_17`: Ctrl flicks lure toward mouse (`max_flick`) |

   * When any per-type cap fails, `UpgradeCard_Other_10` spawns `Message_TooManyUpgrades` (or `Message_NoDullGlitter` / `Message_AttractRepel` / `Message_NoFishOnRod`).

---

## 6. Fish Architecture, Spawning, Appraisal, and Examples

### 6.1 `Fish` Parent Object & Hierarchy

* **Size parents** (inherit `Fish`, run before child `Create_0` or via `action_inherited()`):
  * `FishSmall` (`size = 1`, `RESPAWN_DELAY = 1500000`, `MAX_STAMINA = 50`, `thrall_force = 6`)
  * `FishMedium` (`size = 2`, `RESPAWN_DELAY = 600000`, `MAX_STAMINA = 70`, `thrall_force = 4`, `questFail = 0`, `fearless = 0`; `Other_10` runs `fish_fleelarge()`)
  * `FishLarge` (`size = 3`, `RESPAWN_DELAY = 1800000`, `MAX_STAMINA = 50`, `thrall_force = 3`, `delirious = 0`; `Other_10` runs `fish_fleehuge()`)
  * `FishHuge` (`size = 3`, `display_size = 4`, `RESPAWN_DELAY = 600000`, `NOTHUNGRY_TIMER = 150`, `depth = -14`, `questFail = 1`; `Other_17` runs `fish_huge_touch_lure()`)
* **`Fish_Create_0.gml`**:
  * Sets `object_id = object_index`, `depth = -15 + size`, `old_depth = depth`, `display_size = size`, `catalog_index = -1`, `start_x = x`, `start_y = y`, `TRUE_START_X = x`, `TRUE_START_Y = y`, `MAX_LIFE = 3`, `life = 3`, `MAX_STAMINA = 100`, `stamina = 100`, `autoflip = 1`, `stayOutAtNight = 0`, `deepfish = 0`, `requireMega = 0`, `maxSpeed = 30`, `speed_decay = 0.9`, `lungeCooldown = 0`, `base_lunge_offset = 0`, `clumsiness_coefficient = 0.3`, `image_xscale = 0.5`, `image_yscale = -1`, `image_angle = 180`, `bone_index = image_number - 1`, `path_init()`.
* **`Fish_Step_2.gml` State Machine & User Events**:
  * Every frame: runs `ev_user12` (`Other_22` -> `fish_standard_step12()`, off-screen pause via `isVisible`), dispatches `state`:
    * `0`: `ev_user0` (`Other_10`, idle swim / `path_thrust_update()` + `fish_lunge()`)
    * `1`: `ev_user1` (`Other_11`, lunging at lure -> `fish_default_lunge()` -> `fish_lunge_a/b/c()`, triggers `ev_user7` `Other_17` on bite)
    * `2`: `ev_user2` (`Other_12`, returning to start -> `fish_standard_step2()`)
    * `3`: `ev_user3` (`Other_13`, hooked on lure -> thrash/stamina/drag physics + `fish_check_caught()`)
    * `4`: `ev_user4` (`Other_14`, stuck on boat -> `fish_stick_catswim()`)
    * `5`: `fish_not_hungry()` (`Alarm_0` resets `state = 2`)
    * `6`: `ev_user6` (`Other_16`, dead / waiting for `current_time > respawn_time` -> `fish_respawn()`)
    * `7`: `ev_user8` (`Other_18`, custom grab/pull, e.g. Shark)
    * `8`: `fish_was_just_bombed()`
    * `9`: `ev_user13` (`Other_23`, fleeing -> `fish_standard_step13()`)
    * `10`: `ev_user15` (`Other_25`, night-time retreat)
    * `11`: `speed = 0` (immobile / `netherFish`)
    * `12`: `fish_disabled()` (zoning)
    * `13`: `ev_user5` (`Other_15`, lunging at `CatSwim` -> `fish_default_follow()`)
    * `14`: `fish_dead_float()`
    * `15`: `fish_in_net()` (trawler)
  * Also checks night transition (`!stayOutAtNight && is_it_night()` -> `state = 10`), cave rock collision (`!deepfish && y > 5000`), catalog proximity reveal (`lure_distance_squared < 22500` -> `unhide_entry_for_values(display_size - 1, catalog_index)`), and `fish_check_trawler()`.
* **`Fish_Draw_0.gml`**: In `view_current != 3`, calls `fish_predraw_switchcase()` (orientation/autoflip) and `fish_basicdraw()` (bone sprite overlay when `life < 3`, `translucent`, or `lightning_flash > 0`).
* **Catch & Bite (`Other_17`, `fish_huge_touch_lure`, `fish_was_caught`)**:
  * `Fish_Other_17`: checks `Lure.size >= size` (or `BigHook2` for non-`FishHuge`), hooks fish (`state = 3`) or steals bait (`state = 5`). If another fish is already hooked (`Lure.fish.size < size`), bites it (`Lure.fish.life--`, calls `fish_depop()` if it was full life, replaces on `BigHook` / `Skrimp` / `Worm`).
  * `fish_was_caught`: sets `Catalog` stats (`set_best_life_for_name`, `increment_count_for_name`), adds `XP_VALUE` if `life >= 3`, runs `fish_appraise()`, fires `ev_user9` (`Other_19`), and notifies `CatchQuestAbstract`.

### 6.2 Spawning, Population, and Appraisal

* **Placement & Population Initialization**: Most fish are placed directly as room instances in the GameMaker room. `BulbfishSpawner` (`gml_Object_BulbfishSpawner_Create_0.gml`) is an exception, procedurally spawning a grid of `Bulbfish` at `(950, 3500)` (`13000 x 1100`, `gap = 400`, with `night_y_offset`).
* **`CatalogData` Population (`Step_0`, `Alarm_5`, `Step_2`)**:
  * `Step_0` initially counts `max_pop[i, j] = max(max_pop[i, j], instance_number(object[i, j]))`.
  * `Alarm_5` doubles `max_pop` (`max(max_pop * 2, 5)` or `3` for huge), sets `max_pop_loaded = 1`, initializes `pop` if not loaded, and clamps `pop` to `[0, max_pop]`.
  * `Step_2` sets `catalog_index` on every `Fish` instance by iterating `CatalogData.object[i, j]` (`with (object[i, j]) catalog_index = other.j;`), with explicit fixes for `Kingfish` (`2`) and `Bombat` (`24`).
* **Respawn & Culling**:
  * `fish_die.gml`: pays out `value` (or `BASE_VALUE` in net), calls `fish_depop()` if `life >= MAX_LIFE` (decrements `CatalogData.pop[size - 1, catalog_index]`), sets `respawn_time = current_time + RESPAWN_DELAY * Girl.gear_respawn_scalar[display_size - 1] * target_mod`, moves to `(-500, -500)`, sets `state = 6`.
  * `fish_respawn.gml`: checks `CatalogData.pop_loaded && max_pop_loaded` and `instance_number(object_id) < CatalogData.pop[ii, jj]` (note base quirk: `fish_respawn` uses `ii = size`, unlike `fish_depop`/`FishThinner` which use `size - 1`), copies a `hollow = 1` shell, spawns below `room_height + 1000` near `start_x`, resets `life = 3, state = 2`.
  * `FishThinner` (`Step_0`, `Other_10`): deactivates (`deactivated = 1`) distant fish (`> 2000` from lure, `instance_number > 5`, excluding `Bulbfish`/`Boom_Puff`) when FPS drops (`cull_trigger > 120`), and kills excess instances if `CatalogData.live > CatalogData.pop` when `kill_extras` is set.
* **Appraisal (`gml_Script_fish_appraise.gml`)**:
  * If `life > 0`: `value = BASE_VALUE * power(0.5, MAX_LIFE - life) * Girl.gear_value_scalar[display_size - 1]`.
  * If `life <= 0`: `value = Girl.gear_bone_enchant * BASE_VALUE`.
  * If `Girl.gear_target_fish == object_id`: `value *= Girl.gear_target_value_scalar`.
  * Also sets `catalog_index = get_index_for_name(...)`, `exotic = get_exotic_for_name(...)`, and runs `fish_check_exotic(607)` (`Boat` uniqueness check).

### 6.3 Example Fish Variable Profiles

* **Simple Fish 1 — `Cuddlefish`** (`FishSmall` child, `gml_Object_Cuddlefish_Create_0.gml` / `Other_10/12/13`):
  * Sets `randomize_color(80, 135, 10, 30, 10, 250)`, `hspeed`/`vspeed` random `[-1.5, 1.5]`, `LUNGE_RANGE_START = 60`, `LUNGE_RANGE_STOP = 65`, `LUNGE_SPEED = 7.8`, `hasPath = 0`, `BASE_VALUE = 50`, `XP_VALUE = 50`, `dataobject = 264` (`CuddlefishData`), `limp_frames = 30`, `thrall_frames = -1`, `thrall_force = 5`, `MAX_STAMINA = 30`. Inherits `size = 1`, `display_size = 1`, `RESPAWN_DELAY = 1500000`. Idle `Other_10` steers away from `FishMedium`/`FishLargeEarly`/`FishHuge` and toward `(start_x, start_y)` using `CuddlefishData`.
* **Simple Fish 2 — `Swooper`** (`FishSmall` child, `gml_Object_Swooper_Create_0.gml`):
  * Sets `LUNGE_RANGE_START = 60`, `LUNGE_RANGE_STOP = 65`, `LUNGE_SPEED = 10.2`, `hasPath = 1`, `PATH = 2`, `PATH_MIN_SPEED = 0.5`, `PATH_THRUST_SPEED = 10`, `PATH_THRUST_PHASE = 0.9`, `PATH_THRUSTS = 3`, `BASE_VALUE = 200`, `XP_VALUE = 50`, `RESPAWN_DELAY = 900000`, `limp_frames = 60`, `thrall_frames = 60`, `thrall_force = 6`, `MAX_STAMINA = 50`. Inherits `size = 1`, `display_size = 1`. Uses default path-following `Fish_Other_10`.
* **Simple Fish 3 — `Roundfin`** (`FishSmall` child, `gml_Object_Roundfin_Create_0.gml`):
  * Sets `LUNGE_RANGE_START = 50`, `LUNGE_RANGE_STOP = 55`, `LUNGE_SPEED = 8`, `hasPath = 1`, `PATH = 4`, `PATH_MIN_SPEED = 2`, `PATH_THRUST_SPEED = 3`, `PATH_THRUST_PHASE = 0`, `PATH_THRUSTS = 2`, `BASE_VALUE = 400`, `XP_VALUE = 80`, `path_position` randomized `(random(0.5)+random(0.5))/2`, `limp_frames = 60`, `thrall_frames = -1`, `thrall_force = 10`, `thrall_ang_force = 0.017`, `thrall_friction = 0.8895`, `MAX_STAMINA = 60`.
* **Special Fish 1 — `Shark`** (`FishHuge` child, `gml_Object_Shark_Create_0.gml` / `Other_10/11/12/13/17/18/19/22`):
  * Sets `LUNGE_RANGE_START = 300`, `LUNGE_RANGE_STOP = 330`, `LUNGE_SPEED = 12`, `hasPath = 0`, `BASE_VALUE = 64000`, `XP_VALUE = 5000`, spawns `SharkPath`, `depth = -18.4`, `display_size = 4` (with `size = 3` from `FishHuge`), `RESPAWN_DELAY = 3600000`, `limp_frames = -1`, `thrall_frames = -1`, `thrall_force = 0`, `thrall_ang_force = 0.02`, `thrall_friction = 0.5`, `MAX_STAMINA = 100`, `stayOutAtNight = 1`, `NOTHUNGRY_TIMER = 1000`, `NOT_HUNGRY_TIMER_DEFAULT = 60`, `stamina_recharge = 0`, `stamina_recharge_uses = 6`, `isDragging = 1`. Follows `SharkPath` (shifted at night or when `CaptainsHat` is equipped), pulls the player (`state = 7` -> `Other_18`) unless `BigHook` + `size == 2` bait is used, unlocks `LureData.unlocked[3] = 1` (Huge Hook) on drop, and checks `Rod.rod_id == 0` in `Other_19` for `"basic_shark"`.
* **Special Fish 2 — `Bulbfish`** (`FishSmall` child, `gml_Object_Bulbfish_Create_0.gml` / `Other_10/16/18/19`):
  * Spawned by `BulbfishSpawner` (not placed individually in room) with `night_y_offset`. Sets `LUNGE_RANGE_START = 60`, `LUNGE_RANGE_STOP = 65`, `LUNGE_SPEED = 8`, `BASE_VALUE = 4200`, `XP_VALUE = 500`, `dataobject = 272`, `depth = -18.1`, `RESPAWN_DELAY = 180000`, `limp_frames = -1`, `thrall_frames = 1`, `thrall_force = 0`, `clumsiness_coefficient = 0.95`, `stayOutAtNight = 1`, `light = instance_create(x, y, BulbfishLight)`, `light2 = -1`, `lamp_x/y`. Rises toward surface at night via `night_y_offset`, supports `state = 7` (`Other_18`) persistent world-lamp placement (`[Lamp]`), and unlocks `LureData.unlocked[2] = 1` (Lightbulb) in `Other_19` when caught at `life == 3`.

---

## 7. Concrete Injection & Implementation Recommendations

### 7.1 Runtime Registration Scripts (`jamm_<type>_register`)

Following `jamm_catalog_register` (`docs/catalog-api.md`), JAMM should inject runtime registration scripts (and replace mod-shipped placeholder scripts) called unconditionally at the **end of each `*Data` object's `Create_0`**. Every registration script should accept an explicit stable object/asset identity (or unique string key where no object exists), deduplicate against already-registered entries, and return the assigned slot index.

1. **`jamm_boat_register(object, name, cost, disp, desc, unlocked?)`**:
   * Appends to `BoatData` arrays (`unlocked` default `0`, `cost`, `name`, `object`, `disp`, `desc`), increments `nItems`, deduplicates by `object`.
   * **Injection points**:
     * `BoatData_Create_0`: append mod calls at end; keep `nItems = array_length_1d(unlocked)`.
     * `BoatCard_Other_10`: after `instance_create(0, 0, BoatData.object[item_id])`, set `Boat.boat_index = item_id` on the created instance (so mod boats don't need to hardcode their dynamic `boat_index`), and guard `item_id + i < BoatData.nItems - 1` or allow unlocked `>= 0` items to remain visible.
     * `SaveManager_Other_13`: same `boat_index = bi` assignment on load.
   * **Risks**: `BoatData_Step_0` only auto-unlocks `i = 1..6` and slot `7`. Mod boats must pass `unlocked = 0` (purchasable immediately) or implement their own unlock condition without assuming slot numbers. `Boat8` collisions across Abyss & Cave still require asset namespacing if both mods are loaded together.

2. **`jamm_rod_register(name, cost, cast_speed, rod_power, rod_power_flat, reel_speed, sinker_distance, disp?, unlocked?)`**:
   * Appends to `RodData` arrays, stores `disp[slot]` (optional sprite asset for custom rods, `-1` fallback to `spr_rod`), initializes `UpgradeData.upg[slot, 0..7]` (e.g. `8` available slots `-1` or configurable `slots` arg) and `UpgradeData.object[slot, 0..7] = -1`, increments `RodData.nItems`. (Important: remove/ignore base `unlocked[6..7] = -2` padding or reset `nItems = 6` before registrations).
   * **Injection points**:
     * `RodData_Create_0`: reset/maintain `nItems = 6`, append mod calls at end.
     * `RodCard_Other_11` & `Rod_Other_10` (or `Rod_Draw_0`): if `RodData.disp[id] >= 0`, draw/use `RodData.disp[id]` instead of `spr_rod` subimage `id` (prevents out-of-range subimage crashes when `id >= sprite_get_number(spr_rod)`).
     * `Rod_KeyPress_88`: fix `rid--` -> `rid++` bug.
     * `Rocket_Create_0` / `Rocket_Step_2`: clamp `min(Rod.rod_id, 5)` or compute `target_distance` dynamically when `Rod.rod_id >= 6`.
     * `start_quest_dist`: clamp `min(Girl.bestRod, 5)` when indexing `dist[]` and `dpth[]`.
     * `UpgradeData_Create_0` (or dynamically inside `jamm_rod_register`): ensure `upg[slot, 0..7]` and `object[slot, 0..7]` are initialized before `SaveManager_Other_13` runs.
   * **Risks**: Since rods don't have distinct GM objects, deduplicate by `(name)` or a dedicated `key` parameter.

3. **`jamm_lure_register(item_object, name, cost, disp, desc, unlocked?)`**:
   * Appends to `LureData` arrays (`unlocked` default `1`), increments `nItems`, deduplicates by `item_object`.
   * **Injection points**:
     * `LureData_Create_0`: append mod calls at end.
     * `Girl_Other_10` & `Gear_Other_15`: initialize `gear_lure_scalar[0..LureData.nItems - 1]` and `lure_scalar[0..LureData.nItems - 1]` to `1` (fixes both the base `lure_scalar[5]` omission and `>= 6` out-of-bounds crash in `LureCard_Other_11` and `buy_lure`).
   * **Risks**: `LureCard_Other_11` and `buy_lure` unconditionally multiply by `Girl.gear_lure_scalar[item_id]`, so dynamic initialization (or a `jamm_lure_scalar(id)` helper) is mandatory.

4. **`jamm_tool_register(item_object, name, cost, quests, message_object, disp, desc, unlocked?)`**:
   * Appends to `ToolData` arrays (`unlocked` default `0`, plus new `disp[slot]` sprite field), increments `nItems`, deduplicates by `item_object`.
   * **Injection points**:
     * `ToolData_Create_0`: initialize `disp[0..2] = -1` (clean up unused `unlocked[3..4] = -1`), append mod calls at end.
     * `ToolCard_Other_11`: if `ToolData.disp[item_id] >= 0`, draw `ToolData.disp[item_id]`, else `spr_tools` subimage `item_id`.
   * **Risks**: `ToolData.item[i]` must be an already-instantiated persistent/room object (like `Minimap` / `SonarPanel`), or `jamm_tool_register` / `ToolCard_Other_10` must ensure the tool object is instantiated (`if (!instance_exists(ToolData.item[i])) instance_create(0, 0, ToolData.item[i])`) so `.active` can be toggled safely.

5. **`jamm_upgrade_register(name, desc, desc2, cost, disp, unlocked?, max_stacks?)`**:
   * Appends to `UpgradeData` (`name`, `desc`, `desc2`, `cost`, `unlocks` default `1`, new `disp[slot]` sprite, `max_stacks`), increments `UpgradeData.nItems` (starting from `18`). Deduplicates by `name` (or explicit key).
   * **Injection points**:
     * `UpgradeData_Create_0`: set `nItems = 18`, `disp[0..17] = -1`, append mod calls at end.
     * `UpgradeMenu_Create_0` & `Alarm_0`: populate `spawn_order` dynamically for `18..UpgradeData.nItems - 1`, change `iCard <= 17` -> `iCard < UpgradeData.nItems`.
     * `UpgradeData_Draw_0`, `UpgradeCard_Other_11`, `UpgradeSlot_Other_11`: draw `UpgradeData.disp[upg_id]` when `>= 0` instead of `spr_upgradeicons` subimage `1 + upg_id`.
     * `Girl_Other_10` & `Gear_Other_15`: loop `0..UpgradeData.nItems - 1` instead of `i <= 17` for `gear_upgrade_scalar`.
     * `Rod_Other_10`: initialize/count custom upgrade IDs (`Rod.upgrades[22..]` or `Rod.jamm_upgrades[]`, or pass `upgrades_index`), and `UpgradeCard_Other_10`: enforce `max_stacks` for `card_id >= 18` via `UpgradeData.max_stacks[card_id]`.
   * **Risks**: Mod upgrades still need code hooks (`Lure_Step_0`, `Rod_Other_10`, etc.) to enact gameplay effects; `Rod_Other_10` resetting and recomputing custom upgrade counts cleanly makes reading `Rod.upgrades[upg_id]` (or a helper `jamm_rod_has_upgrade(id)`) straightforward.

6. **`jamm_gear_register(gear_object, value, weight, range_min, range_max, offset_x?, offset_y?, mythic?, unlocked?)`**:
   * Appends to `GearData` arrays (`gear`, `value`, `weight`, `range_min`, `range_max`, `offset_x`, `offset_y`, `mythic`, `unlocked`), increments `n_gear`, rebuilds `map[]` permutation, deduplicates by `gear_object`.
   * **Injection points**:
     * `GearData_Create_0`: move `map[]` sorting into a reusable helper (called after registrations, or re-run in `jamm_gear_register`), append mod calls before final `map[]` build.
     * `Gear_Other_11` / `Other_12` / `Hatalog_Create_0`: note `Dig`, `Fennec`, `Explorer`, `Trucker`, `Witchy` hardcode `gear_id` in their `Create_0`, whereas `gear_spawn` and `Hatalog` assign `gear_id` dynamically after `instance_create`. Avoid calling `gear_spawn` with invalid indices; for `Hatalog_Create_0`, `disabled_hats = 3` only affects completion percentage display (can optionally compute dynamically from unobtainable hats if desired).
   * **Risks**: `Hatalog_Create_0` creates and destroys a temporary instance of every unlocked hat at `(-500, -500)`. Mod hats must not leak child instances in `Create_0` without cleaning them up in `Destroy_0`.

7. **Fish registration (extending Phase 16 `jamm_catalog_register`)**:
   * Fish definitions already use `jamm_catalog_register(...)` (`docs/catalog-api.md`) for catalog arrays and `CatalogData_Step_2` sets `catalog_index` dynamically by iterating `CatalogData.object[i, j]`.
   * Remaining gap for **new mod fish** is **world spawning**: base fish are placed in the room file rather than spawned from a data table. A `jamm_fish_spawn_register(object, x, y, count)` (or room/spawner hook at game start before `CatalogData_Alarm_5`) would let mods introduce new fish instances without editing the shared room asset. Also fix `fish_respawn.gml` `var ii = size;` -> `size - 1` when checking `CatalogData.pop` bounds.

### 7.2 Name-Keyed Save Persistence (`[JAMM_Items]`)

Mirror the Phase 16 `[JAMM_Catalog]` strategy in `SaveManager` (`Other_13`, `Other_14`, `Other_15`) so legacy base slots (`0..N_base - 1`) keep their original INI keys intact while all mod-added items (and equipped-by-name state) persist collision-free under `[JAMM_Items]`:

* **Stable identity keys**:
  * **Boats**: `object_get_name(BoatData.object[i])` (slot `0` `"No Boat"` has `object = -1`, use `"__none__"` or keep legacy `boat0`).
  * **Rods**: `RodData.name[i]` (or a sanitized key `rod:<name>`, since rods lack separate GM objects).
  * **Lures**: `object_get_name(LureData.item[i])`.
  * **Tools**: `object_get_name(ToolData.item[i])`.
  * **Upgrades**: `UpgradeData.name[i]` (or `upg:<name>` for unlock state; for installed slots on a rod, key by rod identity + slot `iy`: `uslot:<rod_key>:<iy>` storing the upgrade's `name`, and `uobj:<rod_key>:<iy>` storing `object_get_name(object[ix, iy])` when `> 0`!).
  * **Gear / Hats**: `object_get_name(GearData.gear[i])`.
* **Save (`Other_14` and `Other_15`)**:
  * Keep writing legacy keys (`[Unlocks] boat<i>`, `rod<i>`, `lure<i>`, `tool<i>`; `[Main] boat`, `activerod`, `gear`; `[Upgrades] unlocks0..17`, `upg<0..5><0..7>`, `obj<0..5><0..7>`; `[Hatalog] hat<i>`) **strictly clamped to the base counts** (`boat`: `0..7`, `rod`: `0..5`, `lure`: `0..5`, `tool`: `0..2`, `upg`: `0..17` & `ix < 6`, `hat`: `0..29`) so vanilla saves stay backward-compatible and legacy indices never collide with mod slots.
  * Write `[JAMM_Items]` for **all** items (`0..nItems - 1` / `0..n_gear - 1`):
    * `boat_u:<key>`, `rod_u:<key>`, `lure_u:<key>`, `tool_u:<key>`, `upg_u:<key>`, `hat_u:<key>`
    * `boat_eq` = `object_get_name(Boat.object_index)` (or `""` if none)
    * `rod_eq` = `RodData.name[Rod.rod_id]`
    * `gear_eq` = `object_get_name(Girl.gear.object_index)` (or `""` if none)
    * `uslot:<rod_key>:<iy>` = upgrade name (or `""` if `-1`, `"__locked__"` if `-2`) and `uobj:<rod_key>:<iy>` = fish `object_get_name` (or `""`).
    * Optional: `tool_a:<key>` to actually persist whether an unlocked tool is toggled `active` on/off.
* **Load (`Other_13`)**:
  * Check `ini_section_exists("JAMM_Items")`.
  * **If `[JAMM_Items]` exists**: load base + mod unlock/equip/upgrade-slot state by key from `[JAMM_Items]`. Any newly added mod item not yet in `[JAMM_Items]` retains its `Create_0` default (never reads stale numeric mod indices from legacy sections). If an equipped boat/rod/hat from `[JAMM_Items]` is from a removed mod, fall back safely (`No Boat`, `Basic Rod` `0`, no hat `-1`) without crashing.
  * **If `[JAMM_Items]` does NOT exist (first load of a legacy save)**: read only the unambiguous base slot ranges from legacy `[Unlocks]`, `[Main]`, `[Upgrades]`, `[Hatalog]`, leaving any appended mod slots (`>= N_base`) at their defaults.
  * Update achievement loops (`rod_<i>`, `boat_<i>`, `tool_<i+1>`) so they only fire Steam achievements for valid base indices or guard safely.

### 7.3 Making Card Menus Scroll (`CardHandler` & `RowHandler`)

* **Targets**:
  * `CardHandler` (`gml_Object_CardHandler_Create_0.gml`, `Alarm_0.gml`, `Destroy_0.gml`, plus a new `Step_0` or `Mouse_60`/`Mouse_61` or dedicated scroll-arrow/button child object): covers `BoatMenu`, `RodMenu`, `LureMenu`, `ToolMenu`.
  * `RowHandler` (`gml_Object_RowHandler_Create_0.gml`, `UpgradeMenu_Alarm_0.gml`): covers `UpgradeMenu` (and `UpgradeSlotMenu` if rods ever get `> 8` slots).
* **Recommended Minimal Design (Paged / Row-Windowed `CardHandler` + `RowHandler`)**:
  1. **Collect visible item IDs first** in `CardHandler_Create_0` / `Alarm_0` (or `User Event`): build an array `visible_items[]` of indices where `data_object.unlocked[i] >= 0`, with length `n_visible`. This also fixes the base `iCard % card_width` row-wrap misalignment bug when items are skipped.
  2. **Window to `visible_rows` rows** (`visible_rows = 2` for `CardHandler`, yielding `page_size = card_width * 2`, i.e., `6` cards for Rod/Lure/Tool and `8` cards for Boat; `visible_rows = 9` for `UpgradeMenu`, `page_size = 18`):
     * Track `scroll_row = 0`, `max_scroll_row = max(0, ceil(n_visible / card_width) - visible_rows)`.
     * Only spawn `Card` instances for visible indices `[scroll_row * card_width .. min(n_visible, (scroll_row + visible_rows) * card_width) - 1]`.
  3. **Scrolling / Paging Controls**:
     * In `CardHandler_Step_0` (and `RowHandler_Step_0`): check `mouse_wheel_up()` / `mouse_wheel_down()` (or `keyboard_check_pressed(vk_up/vk_down)`), and optionally spawn lightweight Up/Down arrow `Buttons` children (similar to `CatalogUp`/`CatalogDown`, destroyed in `Handler_Destroy_0`) when `max_scroll_row > 0`.
     * When `scroll_row` changes: destroy current `Card` instances (`with (Card) instance_destroy();`), reset `x = ORIGIN_X; y = ORIGIN_Y;`, and respawn only the current window's cards (either immediately in a loop for crisp paging or via `alarm[0]` starting at the new window offset).
     * Draw a subtle page/row indicator (e.g. `"Row 1-2 / 3"` or `"Page 1/2"`) in `CardHandler_Draw_0` when `max_scroll_row > 0`.
  4. **Alternative (Continuous Scroll via `y` clipping)**: Not recommended for `Card` objects because `Card` inherits `Buttons` (`Mouse_4`/`Mouse_7` use room-space bounding boxes) and `Card_Step_0`/`Draw_0` overwrite `y = ORIGIN_Y + Girl.y` every frame; off-screen cards would still receive clicks unless `visible = false` and `Card_Step_0` were rewritten, whereas row-windowed respawning requires zero changes to `Card`, `Buttons`, or individual `*Card` objects.
* **Risks**:
  * `Wallet_Other_11` checks `if (instance_exists(CardHandler) && CardHandler.done_spawning)`; ensure `done_spawning` is reset to `0` during page rebuild and set to `1` immediately when spawning completes.
  * `RodMenu_Draw_0` checks `if (done_spawning)` and draws help text at `(ORIGIN_X + 640, ORIGIN_Y + 220)`—this sits cleanly to the right of `card_width = 3` (`3 * 160 = 480`), so it will not overlap paged `3 x 2` grids.
  * Ensure any spawned scroll arrow buttons are NOT children of `Card` (or destroy them explicitly in `CardHandler_Destroy_0` / `Handler_Destroy_0`), so switching pages (`with (Card) instance_destroy()`) does not destroy the scroll buttons themselves.
