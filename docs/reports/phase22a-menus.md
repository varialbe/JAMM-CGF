# Phase 22a — Menu paging (`CardHandler` and `UpgradeMenu`)

Implemented row-windowed paging for `CardHandler` and `UpgradeMenu` per `docs/tasks/phase22-modding-api.md` (sub-task 22a) and `docs/reports/phase22-modding-audit.md` (§7.3). No commits, pushes, git config changes, or submodule edits were made.

## Changes

- **`src/Jamm.Core/Building/MenuRuntime.cs`** (new):
  - `PreparePlan(plan, baseline, original)`: detects `gml_Object_CardHandler_Create_0` and/or `gml_Object_UpgradeMenu_Create_0` in `baseline` (no-op when absent) and applies anchor-checked patches and new `Step_0`/`Draw_0` event entries to the build plan.
  - **`CardHandler`** (`BoatMenu`, `RodMenu`, `LureMenu`, `ToolMenu`):
    - Patches `gml_Object_CardHandler_Create_0` (`visible = true; visible_rows = 2;`, `scroll_row`, `max_scroll_row`, `n_visible`, `window_pos`, `window_end`, `jamm_menu_init`).
    - Rewrites `gml_Object_CardHandler_Alarm_0` to collect visible items (`data_object.unlocked[i] >= 0`), compute `max_scroll_row = max(0, ceil(n_visible / card_width) - visible_rows)`, spawn only the active `[window_pos .. window_end - 1]` window, and wrap rows using visible window position rather than raw `iCard` (fixing the base wrap bug when hidden items are skipped).
    - Adds `gml_Object_CardHandler_Step_0` (mouse wheel, `vk_up`/`vk_down`, and left-click hitboxes on the ▲/▼ buttons; destroys `Card` instances, resets `done_spawning = 0`, and respawns via `alarm[0] = 1` when `scroll_row` changes).
    - Adds `gml_Object_CardHandler_Draw_0` (draws ▲/▼ buttons with `draw_triangle` and `"Page x/y"` in view `0` only when `max_scroll_row > 0`).
    - Prepends `event_inherited();` to `gml_Object_RodMenu_Draw_0` when present so `RodMenu` (which overrides `Draw_0`) still draws paging controls.
  - **`UpgradeMenu`**:
    - Patches `gml_Object_UpgradeMenu_Create_0` (`visible = true; ORIGIN_Y = y; card_width = 2; visible_rows = 9;` plus paging state).
    - Rewrites `gml_Object_UpgradeMenu_Alarm_0` to keep base `spawn_order[0..17]`, append `18 .. UpgradeData.nItems - 1`, filter by `UpgradeData.unlocks[id]`, and window to `9` rows.
    - Adds `gml_Object_UpgradeMenu_Step_0` (destroys `UpgradeCard` instances and resets `done_spawning = 0` on page change) and `gml_Object_UpgradeMenu_Draw_0`.
    - Appends `nItems = array_length_1d(unlocks);` to `gml_Object_UpgradeData_Create_0` when `nItems` is not already defined.
  - `Inject(data, recompile)`: attaches the new `Step_0` and `Draw_0` code entries to `CardHandler` and `UpgradeMenu` game objects.
- **`src/Jamm.Core/Building/ArchiveBuilder.cs`**:
  - Wired `MenuRuntime.PreparePlan(..., semantics.Base, original.Data)` before `HudRuntime.PreparePlan(...)` so `HudRuntime`'s draw-state wrappers track `MenuRuntime` draw events cleanly.
  - Wired `MenuRuntime.Inject(data, recompile)` next to `CatalogRuntime.Inject` and `HudRuntime.Inject`.
- **`tests/Jamm.Core.Tests/MenuRuntimeTests.cs`** (new):
  - Covers synthetic full build + reload/verification with `CardHandler`/`Card`/`BoatMenu`/`RodMenu`/`UpgradeMenu`/`UpgradeData` stubs (with and without `fnt_babySmall`), composition with `HudRuntime`, clean no-op when menu objects are absent, and `InvalidDataException` on missing code anchors.
- **`docs/modding/menus.md`** (new):
  - Documents paging behavior, controls, `UpgradeData.nItems` support, and base-compatible single-page behavior.

## Design decisions

1. **Row-windowed respawn via `CardHandler`/`UpgradeMenu` `Step_0` + `Draw_0`**: avoids adding new GameObjects/sprites to the archive and leaves `Card`, `Buttons`, and `UpgradeSlotMenu` untouched. `RowHandler_Create_0` in the base game omits `ORIGIN_Y = y;`, so `UpgradeMenu_Create_0` initializes it explicitly.
2. **Vector ▲/▼ arrows (`draw_triangle`)**: `fnt_babySmall` is a legacy bitmap font that may lack Unicode arrow glyphs; `draw_triangle` guarantees crisp arrows without new sprites.
3. **Font fallback**: `Draw_0` emits `draw_set_font(fnt_babySmall)` when `fnt_babySmall` exists in the base archive and `draw_set_font(-1)` otherwise, keeping minimal synthetic test archives compilable.
4. **`visible = true` in `Create_0`**: ensures `CardHandler` and `UpgradeMenu` always run `Draw_0` even if the base object resource has `Visible == false`.

## Verification

- `dotnet build -c Release`: succeeded with **0 warnings, 0 errors**.
- `dotnet test tests/Jamm.Core.Tests -c Release`: **305 passed, 0 failed, 7 skipped** (`RealFileTests`/`RealBuildTests`/etc. skipped because `JAMM_ORIGINAL` and `JAMM_MODS_DIR` are unset in this environment). All 5 tests in `MenuRuntimeTests` passed.

## Risks and what needs in-game verification

- **In-game UI layout & input**: needs desktop verification in legacy CGF for ▲/▼ button placement and `"Page x/y"` readability next to `BoatMenu` (4 cols), `RodMenu` (alongside the right-hand upgrade hint text), `LureMenu`, `ToolMenu`, and `UpgradeMenu` (9 rows), as well as mouse wheel, Up/Down keys, and click handling with >1 page of items.
- **Single-mod equivalence test (`RealBuildTests`)**: not run without `JAMM_ORIGINAL`/`JAMM_MODS_DIR`; note that `RealBuildTests.SingleModsAndBothLoadOrdersBuildVerifyAndRestore` asserts byte/GML identity against mod code for entries other than `CatalogData_Create_0`, `SaveManager_*`, and `Quest_Draw_0`, so running `RealBuildTests` against real mods will likely require accounting for `MenuRuntime`-patched entries (`CardHandler_*`, `RodMenu_Draw_0`, `UpgradeData_Create_0`, `UpgradeMenu_*`) similarly to `HudRuntime`.
