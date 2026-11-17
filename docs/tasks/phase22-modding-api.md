# Phase 22 — JAMM Modding API (design + sub-tasks)

Read first: `docs/reports/phase22-modding-audit.md` (game audit), `docs/catalog-api.md` (Phase 16 pattern),
`src/Jamm.Core/Building/CatalogRuntime.cs` + `HudRuntime.cs` (how runtime GML is injected into a build:
`XxxRuntime.Inject(data, recompile)` in `ArchiveBuilder`, `recompile[name] = gml` for new/changed code entries).

## Goal

Mods add boats, rods, lures, tools, hats, upgrades and fish **without editing shared game objects**, so several
mods never conflict. Items are identified by stable string keys, saved by name, and every shop list scrolls when
it overflows.

## Core idea: zero-conflict mods through init scripts

* A mod ships only NEW assets: sprites, objects, and one or more scripts named **`jamm_init_<modid>`**
  (any suffix). Inside, the author calls the registration API, e.g.
  ```gml
  jamm_boat_register("mymod:raft", "Raft", 15000, spr_raft_card, obj_raft, "A wobbly raft.#Cheap!");
  jamm_hat_register("mymod:crown", obj_crown_hat, 2500, 0, 0, 3000);
  ```
* At build time JAMM generates **`jamm_api_init`**, which calls every `jamm_init_*` script present in the merged
  archive in a deterministic order (profile/load order, then name), and injects a call to it at the start of
  `SaveManager` Step 0 *before* the save is loaded (guarded so it runs exactly once per Data-object lifetime:
  each `*Data` instance gets `jamm_api_done = 1`; if a Data object is re-created, registrations run again).
  The base game loads saves on SaveManager's first Step (`if (!loaded)`), after all Creates, so all Data objects exist.
* Registration functions are idempotent by key: registering the same key twice returns the existing slot.
* The reserved namespace is `jamm_` (scripts) and `JAMM_` (INI sections). Mods may ship placeholder scripts with the
  API names to compile standalone; JAMM replaces their code (same as catalog API).
* Legacy mods (UTMT edits of Data Create) keep working unchanged through the normal merge pipeline.

## When the runtime is injected

* **Menu scrolling** (22a) is injected into every archive built by `ArchiveBuilder` from the legacy base when the
  target objects exist (`CardHandler`, `UpgradeMenu`). Behaviour with <= one page is identical to the base game.
* **Item API + JAMM_Items saves** (22b) are injected when the merged archive contains at least one `jamm_init_*`
  script **or** any mod calls a `jamm_<type>_register` function. Builds without API mods stay byte-identical to today
  except for 22a.

## API surface (GML, GM:S 1.4 dialect, all return the slot index or -1 on invalid input)

| function | data object / arrays | identity |
|---|---|---|
| `jamm_boat_register(key, name, cost, disp_sprite, boat_object, desc [, unlocked=0])` | BoatData unlocked/cost/name/object/disp/desc, nItems | key |
| `jamm_rod_register(key, name, cost, cast_speed, rod_power, rod_power_flat, reel_speed, sinker_distance, disp_sprite [, upgrade_slots=8, unlocked=0])` | RodData arrays + UpgradeData.upg/object rows | key |
| `jamm_lure_register(key, name, cost, item_object, disp_sprite, desc [, unlocked=1])` | LureData arrays, Girl.gear_lure_scalar/lure_scalar = 1 | key |
| `jamm_tool_register(key, name, cost, quests, item_object, message_object, disp_sprite, desc [, unlocked=0])` | ToolData arrays (instantiates item_object if needed) | key |
| `jamm_hat_register(key, gear_object, value, weight, range_min, range_max [, offset_x, offset_y, mythic=0])` | GearData arrays + map, n_gear | key |
| `jamm_upgrade_register(key, name, desc, desc2, cost, icon_sprite, max_stacks, apply_script, step_script)` | UpgradeData arrays (ids >= 18) | key (22c) |
| `jamm_fish_register(key, object, size_tab, name, desc, spawn_x1, spawn_x2, spawn_y1, spawn_y2, count)` | spawns instances + `jamm_catalog_register` | object name (22d) |
| `jamm_item_find(type, key)` | returns slot or -1; type is "boat","rod","lure","tool","hat","upgrade" | |

Each Data object additionally gets a parallel `jamm_key[i]` array; base items get fixed keys
`base:boat0` … `base:boat7`, `base:rod0` …, `base:hat0` … (by base slot).

Sprites: where the base draws a fixed sprite strip by subimage (`spr_rod`, `spr_tools`, `spr_upgradeicons`, lure/boat
cards), patch the draw code so that `disp[i] >= 0` draws that sprite instead; base items keep `disp = -1`/their
current behaviour. Never index a base sprite strip beyond `sprite_get_number`.

## Saves (`[JAMM_Items]`, written in addition to all legacy keys)

* Save (append to SaveManager Other_14 and Other_15): for every item of every type `u:<type>:<key>` = unlocked value;
  `eq:boat` = key of the equipped boat (Boat.boat_index -> key, `""` none), `eq:rod` = key of Rod.rod_id,
  `eq:hat` = key of Girl.gear_id (`""` none); upgrades: `slot:<rodkey>:<iy>` = upgrade key, `""` empty, `"#locked"` for -2;
  `slotobj:<rodkey>:<iy>` = `object_get_name` of the attract/repel fish or `""`.
* Load (append to SaveManager Other_13, i.e. after the legacy load): if `[JAMM_Items]` exists, apply every key found
  (unknown keys ignored — they belong to removed mods and stay in the file); items without a key keep their Create
  default. Never read legacy numeric keys for slots >= the base count. Re-equip by key (destroy/spawn Boat, set
  Rod.rod_id + Rod event_user0, gear_spawn) and fall back safely (no boat / rod 0 / no hat) if the key is gone.
  If `[JAMM_Items]` is absent (legacy save): legacy load already handled base slots; reset mod slots (>= base count)
  to their Create defaults (the legacy loops may have read stale `boat8`-style keys).
* Achievements loops that build names like `"boat_" + string(i)` must not run for slots >= base count.

## Menus (22a)

Row-windowed paging for `CardHandler` (Boat/Rod/Lure/Tool menus; 2 visible rows) and `UpgradeMenu` (RowHandler,
9 visible rows): build the visible item list (`unlocked >= 0`), spawn only the current window, mouse wheel and
Up/Down keys (and small clickable ▲/▼ buttons drawn next to the grid) change the row, destroy+respawn cards on change,
draw "Page x/y" when more than one page. Fixes the base row-wrap bug when items are skipped. `done_spawning` must be
reset while respawning. See audit §7.3. Upgrade menu must also lift the hard-coded `iCard <= 17` / `spawn_order` limit
to `UpgradeData.nItems` (base ids keep their order, extra ids appended).

## Sub-tasks / branches

* **22a** `exp/api-menus` — menu paging (CardHandler + UpgradeMenu), injected always. Tests + real build.
* **22b** `exp/api-items` — init-script discovery + `jamm_api_init`, registration for boat/rod/lure/tool/hat,
  `jamm_item_find`, draw patches for custom disp sprites, `[JAMM_Items]` save/load, equip by key.
* **22c** `exp/api-upgrades` — custom upgrades: registration, menu/slot/icon draw, Rod Other_10 stack counting into
  `Rod.jamm_upg[id]`, `apply_script(stacks)` on recompute and `step_script(stacks)` every step via a persistent
  `obj_jamm_api` controller, per-upgrade max_stacks enforced in UpgradeCard, saved by key.
* **22d** `exp/modkit` — **JAMM ModKit**: a folder package (`jamm-mod.json` + PNGs + optional `.gml` hook files)
  compiled by `cgfmm modkit build <dir> [--out mod.win]` into a normal mod data.win that only adds assets and one
  `jamm_init_<id>` script. Supports fish (parent FishSmall/Medium/Large/Huge, sprite, speeds, value, XP, spawn box,
  optional behaviour hooks per state), boats, hats, upgrades. Example packages in `examples/modkit/`:
  `showcase-upgrades` (5 upgrades) and `example-fish`, with original art generated by a script (no game art).
  `cgfmm add <folder>` builds and adds it.
* **22e** docs: `docs/modding/` guide (getting started, each API with examples, ModKit reference), README updates.

## Common rules for all sub-tasks

AGENTS.md hard rules. GM:S 1.4 GML compiled by UndertaleModLib (`var` locals, no structs/methods/`?:` chains,
`real()` throws on non-numbers). Every patch to existing game code: decompile the code entry from the merged data,
apply a precise text edit (fail with a clear `InvalidDataException` naming the entry if the anchor text is not found),
put the result in `recompile`. No game code copied into the repo (tests use synthetic UndertaleData like
`SmoothGeneratorTests`/`CatalogApiTests`). Zero build warnings, all tests green. Real-file tests opt-in via
`JAMM_ORIGINAL` / `JAMM_MODS_DIR`.
