# Scrollable shop and upgrade menus (legacy CGF)

When building from the legacy GM1.4 archive, JAMM automatically injects row-windowed paging into `CardHandler` (Boat, Rod, Lure, and Tool menus) and `UpgradeMenu` when those objects exist in the base archive. Builds without those objects are left untouched.

## Visible window and layout

- **`CardHandler`** (`BoatMenu`, `RodMenu`, `LureMenu`, `ToolMenu`): displays `2` visible rows at a time (`4` columns for `BoatMenu`, `3` columns for `RodMenu`/`LureMenu`/`ToolMenu`).
- **`UpgradeMenu`**: displays `9` visible rows of `2` columns at a time.

On open, the menu collects visible items (`unlocked[i] >= 0` for `CardHandler`, `UpgradeData.unlocks[id]` truthy for `UpgradeMenu`) and spawns only the cards in the active row window `[scroll_row * card_width .. min(n_visible, (scroll_row + visible_rows) * card_width) - 1]`. Wrapping is computed from the position within the visible list rather than raw item IDs, which also fixes the base game's row-wrap misalignment when intermediate items are hidden (`unlocked < 0`).

When `n_visible` fits within a single window (`max_scroll_row == 0`), no paging UI is drawn and menu behavior matches the base game.

## Scrolling controls

When `max_scroll_row > 0` (more items than fit on one page):

- **Mouse wheel** (`mouse_wheel_up()` / `mouse_wheel_down()`), **Up / Down arrow keys**, or the **clickable ▲ / ▼ buttons** drawn to the right of the card grid shift `scroll_row` by `-1` or `+1`, clamped to `[0, max_scroll_row]`.
- Changing `scroll_row` destroys the currently displayed cards (`Card` / `UpgradeCard`), resets `done_spawning = 0`, resets `(x, y)` to `(ORIGIN_X, ORIGIN_Y)`, and respawns the new window via `alarm[0]` (`done_spawning` returns to `1` when the window finishes spawning).
- A **`Page x/y`** label (`scroll_row + 1` / `max_scroll_row + 1`) is drawn between the ▲ and ▼ buttons. On `RodMenu`, `RodMenu_Draw_0` calls `event_inherited()` so the paging UI renders alongside the rod upgrade hint text.

## Dynamic upgrade count (`UpgradeData.nItems`)

The base game hardcodes `18` upgrades (`0..17`) in `UpgradeData` and `iCard <= 17` in `UpgradeMenu_Alarm_0`. JAMM:

1. Adds `nItems = array_length_1d(unlocks);` to `UpgradeData_Create_0` if `nItems` is not already defined.
2. Preserves the base `spawn_order[0..17]` sequence in `UpgradeMenu`, and appends any extra upgrade IDs (`18 .. UpgradeData.nItems - 1`) in ascending order.
