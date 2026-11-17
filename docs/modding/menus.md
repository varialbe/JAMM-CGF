# Scrollable Shop and Upgrade Menus

Adding multiple mods with new boats, hats, upgrades, rods, lures, or tools quickly fills past the single screen of cards that *Cat Goes Fishing* originally supported. JAMM solves this automatically by turning those menus into scrollable, paged windows.

When building from the legacy 2023 archive, JAMM injects row-windowed paging into **`CardHandler`** (`BoatMenu`, `RodMenu`, `LureMenu`, `ToolMenu`) and **`UpgradeMenu`** whenever those objects exist in the archive.

---

## For players: how scrolling works

* **Menus that page**: Boats, Rods, Lures, Tools, and Rod Upgrades. *(The Fishalog and Hatalog already have their own native scrollbars.)*
* **When everything fits on one page**: No arrows or page numbers appear—the menu looks and behaves identically to the unmodded game.
* **When items overflow onto extra pages**, paging controls appear to the right of the card grid:
  * **Mouse wheel**: Scroll up or down anywhere while the menu is open.
  * **Keyboard**: Press the **Up** or **Down** arrow keys.
  * **On-screen buttons**: Click the **▲** and **▼** buttons, with a **`Page x/y`** indicator drawn between them.

---

## For modders: technical behavior

### Visible window sizes

| Menu | Parent | Columns (`card_width`) | Visible rows (`visible_rows`) | Cards per page |
| :--- | :--- | :---: | :---: | :---: |
| `BoatMenu` | `CardHandler` | 4 | 2 | 8 |
| `RodMenu` | `CardHandler` | 3 | 2 | 6 |
| `LureMenu` | `CardHandler` | 3 | 2 | 6 |
| `ToolMenu` | `CardHandler` | 3 | 2 | 6 |
| `UpgradeMenu` | `RowHandler` | 2 | 9 | 18 |

Note that `Hatalog` and `Fishalog` use surface-based continuous scrolling natively and do not use `CardHandler` or `RowHandler`.

### Visibility filtering and the base wrap bug

On menu open, JAMM scans all items and collects only visible entries into `visible_items[]`:

* **`CardHandler`**: includes every `i` in `0 .. data_object.nItems - 1` where `data_object.unlocked[i] >= 0`.
* **`UpgradeMenu`**: includes every `id` where `UpgradeData.unlocks[id]` is truthy.

Only the active slice `[scroll_row * card_width .. min(n_visible, (scroll_row + visible_rows) * card_width) - 1]` is spawned, and column wrapping is computed from the position inside `visible_items[]`. This also fixes a base-game bug where skipping hidden (`unlocked < 0`) items misaligned subsequent rows.

### Dynamic upgrade count (`UpgradeData.nItems`)

The unmodded game hardcodes `18` upgrades (`0..17`) in `UpgradeData` and `iCard <= 17` in `UpgradeMenu_Alarm_0`. JAMM:

1. Appends `nItems = array_length_1d(unlocks);` to `UpgradeData_Create_0` if `nItems` is not already defined.
2. Preserves the base `spawn_order[]` sequence first, then appends any additional upgrade IDs (`array_length_1d(spawn_order) .. UpgradeData.nItems - 1`) in ascending order.

### Lifecycle details to keep in mind

* **`done_spawning`**: Changing `scroll_row` destroys active `Card` / `UpgradeCard` instances, resets `done_spawning = 0`, resets `(x, y)` to `(ORIGIN_X, ORIGIN_Y)`, and triggers `alarm[0] = 1` to spawn the new window (`done_spawning` returns to `1` once the window finishes). UI scripts that check `CardHandler.done_spawning` (such as `Wallet`) wait cleanly during page transitions.
* **`RodMenu_Draw_0`**: `RodMenu` overrides `Draw_0` to draw upgrade hint text to the right of the 3-column grid. JAMM prepends `event_inherited();` to `RodMenu_Draw_0` so the `CardHandler` paging buttons and `Page x/y` label still render.
* **Custom menu overrides**: Paging is treated as a safe enhancement—if a legacy mod heavily rewrote `CardHandler` or `UpgradeMenu` so the expected anchors are missing, JAMM skips paging for that menu, logs a build decision, and preserves the mod's layout instead of failing the build.
