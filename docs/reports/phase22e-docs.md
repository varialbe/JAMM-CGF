# Phase 22e — Modding & Player Documentation

Created and unified the human-facing modding documentation under `docs/modding/`, updated player and CLI documentation (`README.md`, `docs/user-guide.md`, `docs/cli.md`), and audited every signature, default, constraint, hook event, and save key directly against the implementation.

## Files Created or Updated

### `docs/modding/`
* [`docs/modding/README.md`](../modding/README.md) — Landing page and index with the **"Which path is for me?"** comparison (ModKit folder mods vs. GML Modding API for UndertaleModTool vs. legacy whole-`data.win` mods), merge architecture overview, compatibility promise, and links to all guides and `examples/modkit/*`.
* [`docs/modding/getting-started.md`](../modding/getting-started.md) — 10-minute walkthrough for creating, drawing, checking (`cgfmm modkit check`), building (`cgfmm modkit build`), importing (`cgfmm add`), profiling, and testing a first fish mod.
* [`docs/modding/modkit-reference.md`](../modding/modkit-reference.md) — Complete `jamm-mod.json` schema for top-level, `fish`, `boats`, `hats`, and `upgrades` with exact types, defaults, constraints, validation error messages & fixes, hook events/arguments/variables, PNG/strip/origin/scale art requirements, and GM:S 1.4 dialect notes.
* [`docs/modding/api-reference.md`](../modding/api-reference.md) — Runtime GML API reference: `jamm_init_*` discovery and `jamm_api_init` injection timing, standalone placeholders, reserved namespaces (`jamm_`, `obj_jamm_`, `jmk_`, `JAMM_`), typed asset relinking (`CgfAssetTypes`), and full parameter tables, return values, idempotency rules, and examples for `jamm_boat_register`, `jamm_rod_register`, `jamm_lure_register`, `jamm_tool_register`, `jamm_hat_register`, `jamm_upgrade_register`, `jamm_fish_register`, `jamm_catalog_register` (`jamm_catalog_find`, `jamm_catalog_area`), `jamm_item_find`, and `jamm_upgrade_stacks`.
* [`docs/modding/saves.md`](../modding/saves.md) — Player and modder guide to `[JAMM_Items]` and `[JAMM_Catalog]` keys, legacy save migration, removing/reordering/re-adding mods, safe equip fallbacks, remaining numeric sections (`OnBoat`, `LF`, `Fishes`, `Lamp`), and what to back up.
* [`docs/modding/menus.md`](../modding/menus.md) — Player controls (mouse wheel, Up/Down, ▲/▼, `Page x/y`) and modder internals (row windows, visible-item wrapping fix, `UpgradeData.nItems`, `done_spawning`, `RodMenu_Draw_0`, graceful skip on heavily rewritten menus).
* [`docs/modding/cookbook.md`](../modding/cookbook.md) — 9 concise recipes: fast boat, value-boosting hat, `step`-hook upgrade, night-only fish, fleeing fish, reading upgrade stacks from a fish hook (`jamm_upgrade_stacks`), multi-item package, edit-check-build-test loop, and a GML API rod/lure example.
* [`docs/modding/troubleshooting.md`](../modding/troubleshooting.md) — Symptom-based fixes for `modkit check`/`build` errors, GM 1.4 compile pitfalls, missing in-game spawns/shop entries, upgrade multiplier/idempotency gotchas, sprite scale/orientation/origins, merge errors, and library re-import naming.

### Top-Level & Player/CLI Docs
* [`README.md`](../../README.md) — Added **"Make your own mods"** section with a 6-line `jamm-mod.json` teaser linking `docs/modding/`, and explicitly noted where console commands are documented.
* [`docs/user-guide.md`](../user-guide.md) — Added **"API mods and scrollable menus"** covering player installation of API/ModKit mods, menu paging controls, `[JAMM_Items]`/`[JAMM_Catalog]` save behavior, and console documentation links.
* [`docs/cli.md`](../cli.md) — Added `modkit new/check/build`, `builtin list/add`, and `add <file.win, file.zip, or modkit dir>` to the CLI reference table plus a **"ModKit commands"** section.

---

## Code vs. Spec Discrepancies

Where the Phase 22 design specifications (`docs/tasks/phase22-modding-api.md` and `docs/tasks/phase22cd-upgrades-modkit.md`) differed from the actual implementation, the documentation reflects the **implemented code**:

1. **`jamm_fish_register` arity and `count` lower bound (`FishRuntime.cs` vs. spec)**
   * *Spec*: Listed 10 parameters `jamm_fish_register(key, object, size_tab, name, desc, x1, x2, y1, y2, count)`.
   * *Code (`FishRuntime.cs`)*: Accepts **10 to 16 parameters**, forwarding optional arguments `10..15` (`desc_caught`, `desc_seen`, `desc_hidden`, `desc_n`, `desc_e`, `exotic`) directly to `jamm_catalog_register`. Also accepts `count >= 0` (`0` is allowed at runtime, whereas ModKit JSON validation requires `spawn.count >= 1` and defaults to `3`). Returns `0` when the archive has no `CatalogData` object, or the `jamm_catalog_register` slot index otherwise.

2. **`jamm_api_init` placement and `jamm_api_done` flag when `FishRuntime` runs standalone (`FishRuntime.cs` vs. `ItemsRuntime` / spec)**
   * *Spec*: Stated `jamm_api_init()` runs right before `if (!loaded)` in `SaveManager` `Step_0` and sets `jamm_api_done = 1` on each `*Data` instance.
   * *Code*:
     * When `ItemsRuntime` is active (`ItemsRuntimeScripts.cs`, `ItemsRuntimePatches.cs`), `jamm_api_init()` is placed before `if (!loaded)` and sets `jamm_api_done = 1` on `BoatData`, `RodData`, `LureData`, `ToolData`, `GearData`, and `UpgradeData`.
     * When `FishRuntime` runs standalone without `ItemsRuntime` (e.g., direct `jamm_fish_register` calls with no `jamm_init_*` scripts or item/upgrade register calls, or when `ItemsRuntime` is skipped because `SaveManager_Other_13..15` are absent), `FishRuntime` prepends the call to the **top** of `SaveManager_Step_0` and stores `jamm_api_done = 1` on **`CatalogData`** (or on `SaveManager` if `CatalogData` does not exist).

3. **Runtime activation triggers (`ItemsRuntime.cs` & `FishRuntime.cs` vs. spec)**
   * *Spec*: Said Item API activates on `jamm_init_*` or `jamm_<type>_register`.
   * *Code*: `ItemsRuntime` activates on any `jamm_init_*` script **or** a function call (`ident(`) to `jamm_{boat,rod,lure,tool,hat,upgrade}_register` (so even a fish-only ModKit mod with `jamm_init_*` activates `ItemsRuntime` on full archives). Meanwhile `FishRuntime.UsesFishApi` scans `GmlReferences.Identifiers` for any identifier occurrence of `jamm_fish_register`.

4. **`jamm_hat_register` `offset_x` / `offset_y` semantics (`ItemsRuntimeScripts.cs` & `ModKitBuilder.cs` vs. spec)**
   * *Spec*: Listed `offset_x, offset_y` in `jamm_hat_register` without distinguishing Hatalog offsets from equip offsets.
   * *Code*: `offset_x` and `offset_y` in `jamm_hat_register` write `GearData.offset_x[slot]` and `GearData.offset_y[slot]` (Hatalog display offsets, populated from `hatalogOffsetX` / `hatalogOffsetY` in ModKit), whereas on-cat wear offsets (`EQUIP_OFFSET_X`, `EQUIP_OFFSET_Y`) are instance variables on the `Gear` child object itself (`offsetX` / `offsetY` in ModKit, defaulting to `14` and `-42`). `jamm_hat_register` also always sets `unlocked[slot] = 0` (no `unlocked` parameter).

5. **`jamm_upgrade_register` required parameter count (`ItemsRuntimeScripts.cs` vs. spec)**
   * *Code*: Strictly requires all **9** arguments (`if (argument_count < 9) return -1;`); callers must pass `-1` explicitly for unused `icon_sprite`, `max_stacks`, `apply_script`, or `step_script`. Always initializes `UpgradeData.unlocks[slot] = 1`.

6. **`jamm_fish_register` / `jamm_catalog_register` not listed in `CgfAssetTypes.ApiFunctions` (`CgfAssetTypes.cs`)**
   * *Code*: `CgfAssetTypes.ApiFunctions` declares typed asset arguments for `jamm_{boat,rod,lure,tool,hat,upgrade}_register`, while `jamm_fish_register` and `jamm_catalog_register` are not in `ApiFunctions` (`FishRuntime` / `CatalogMerge` / Underanalyzer handle them separately).

7. **Menu paging fallback when a mod rewrites a menu (`MenuRuntime.cs` vs. spec)**
   * *Spec*: Described menu paging as unconditionally injected into `CardHandler` and `UpgradeMenu` when those objects exist.
   * *Code (`MenuRuntime.cs`)*: Uses `TryPatch` per menu—if a selected mod rewrote `CardHandler` or `UpgradeMenu` such that required anchors no longer match, `MenuRuntime` gracefully skips paging for that menu and records a build decision rather than failing the build.

8. **Additional ModKit fields implemented beyond the task spec snippet (`ModKitPackage.cs`)**
   * `ModKitPackage.cs` also implements `fish[].hooks.draw`, `fish[].seenDescription`, `fish[].hiddenDescription`, `fish[].exotic`, `fish[].origin`, `fish[].lungeRangeStop`, `boats[].unlocked`, `boats[].origin`, `hats[].hatalogOffsetX`, `hats[].hatalogOffsetY`, `hats[].offsetX`, `hats[].offsetY`, `hats[].weight`, `hats[].rangeMin`, `hats[].rangeMax`, `hats[].mythic`, and `hats[].origin`. All are documented in `docs/modding/modkit-reference.md`.

9. **`eq:boat` slot `0` ("No Boat") handling on load (`ItemsRuntimeScripts.cs`)**
   * `jamm_items_load` destroys any existing `Boat` whenever `jamm_bi <= 0` (covering both `""` / missing mod boat `-1` and `"base:boat0"` slot `0` "No Boat").

10. **Strict JSON parsing & `jamm_upgrade_stacks` return on missing key**
    * `ModKitPackage.cs` disallows JSON comments and trailing commas (matching strict RFC 8259 JSON), while matching property names case-insensitively.
    * `jamm_upgrade_stacks(key)` returns `0` (rather than `-1`) when a key is unknown or `Rod`/`UpgradeData` is missing, and respects `Rod.allowUpgrades`.
