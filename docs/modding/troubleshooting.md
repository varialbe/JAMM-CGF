# Modder Troubleshooting

Quick fixes for the most common issues when building or testing ModKit and GML API mods.

---

## `cgfmm modkit check` or `build` fails

* **`Invalid JSON in 'jamm-mod.json'`**
  * Standard JSON does not allow trailing commas (`[1, 2,]` or `{ "a": 1, }`) or comments (`// ...`). Remove them and re-run `cgfmm modkit check`.
* **`PNG '...' is not a valid PNG image`**
  * The file isn't a real PNG (for example a `.jpg` or `.webp` renamed to `.png`). Open it in an image editor (GIMP, Krita, Aseprite, Paint.NET) and **Export As PNG**.
* **`PNG '...' is WxH, which is not evenly divisible by frames=N`**
  * Your horizontal sprite strip's total width must equal `frameWidth * frames`. Either widen the canvas so every frame has the exact same width, or adjust `"frames"`.
* **`Asset path '...' must be a relative path inside the package directory`**
  * Move sprites and `.gml` hooks inside the mod folder and write paths relative to `jamm-mod.json` (like `"art/fish.png"`, never `"/home/..."` or `"../art/fish.png"`).
* **`Set the configured legacy base first`**
  * Run `cgfmm config set base /path/to/legacy-original/data.win` (or pass `--base /path/to/legacy/data.win` to `cgfmm modkit build`).
* **`'...' is not a supported legacy VM bytecode 16 archive`**
  * You pointed `--base` at the current Steam YYC build or an already-modded/wrong archive. Point `base` at your untouched **August 2023 legacy `data.win`**.

---

## GML compile errors in `.gml` hooks

* **`Variable ... not set before reading` or syntax error on `var`**
  * GM:S 1.4 requires declaring local variables **before** assigning them:
    ```gml
    // Good (GM 1.4):
    var speed_bonus;
    speed_bonus = 2 * argument0;

    // Bad (GMS2+ syntax — fails to compile in GM 1.4):
    var speed_bonus = 2 * argument0; // Note: inline var-init fails in some GM1.4 contexts; separate lines are safest!
    ```
  * Do not use GMS2+ features: structs (`{}`), `function() {}`, `??`, or chained ternary operators.
* **Literal `\n` shows up in descriptions**
  * GameMaker: Studio 1.4 uses **`#`** for line breaks inside UI strings, not `\n`. (ModKit converts actual newlines in JSON strings to `#` automatically, or you can write `#` directly.)
* **Runtime error in `real(...)`**
  * In GM:S 1.4, `real()` throws on non-numeric strings. Validate digits or only call `real()` on verified numeric strings.

---

## Mod builds cleanly, but nothing appears in-game

* **My fish isn't near the dock**
  * Check `spawn`: in `NewWorld`, the dock is around `x = 815`, water surface is `y = 1244`, and deep water extends downward (`y > 1244`). If you set `y: [200, 600]`, the fish spawned in the sky and fell/pushed below `y = 1294`! Use `y: [1400, 1950]` for shallow water near the dock.
* **My fish disappears as soon as night falls (or I only see it at night)**
  * If `"night": false` (the default), the fish retreats at night (`!stayOutAtNight`). If `"night": true` with a daytime-hiding `step` hook (like Glowfin), test at night or use a lamp/time cycle.
* **My boat / hat / rod doesn't show up in the shop**
  * Check `"unlocked"`: `-1` hides an item from the shop until something unlocks it; `0` makes it visible and purchasable; `1` marks it already owned.
  * For hats (`Hatalog`), `unlocked` starts at `0` (locked until dropped in the ocean or bought in Hatalog—wait, Hatalog shows unlocked hats, or use `unlockgear` in **JAMM Console** to inspect all hats/shops immediately!).
  * If you already have >6 or >8 items in a shop, look for the **`Page x/y`** arrows to the right of the card grid, or scroll with the mouse wheel / Up & Down arrows!
* **My `jamm_init_*` script (GML API mod) never ran**
  * Make sure the script name starts with **`jamm_init_`** (exact lowercase prefix) and that your mod actually appears in the enabled profile.

---

## Upgrades don't apply, double-stack, or crash

* **Upgrade effect keeps multiplying every time I swap rods or slots**
  * `apply` hooks run inside `Rod` `Other_10` every time upgrades are recomputed. Only modify fields that `Rod` `Other_10` resets beforehand (such as `Rod.upgrades[]`, `Fish.base_lunge_mod`, `Fish.base_lunge_offset`), or store a dedicated resettable field rather than mutating permanent variables with `+=` / `*=`.
* **Upgrade costs `$300` instead of what I typed (or costs way too much)**
  * `UpgradeData.cost` is a **multiplier** of `RodData.cost[rod_id]`, not a flat dollar price! For example, `"cost": 0.25` means 25% of the current rod's cost (with a `$300` minimum in `calculate_upgrade_cost`). Putting `"cost": 5000` makes the upgrade cost `5000x` your rod's price!
* **Cannot buy more than 1 copy**
  * `maxStacks` defaults to `1`. Increase `"maxStacks"` in `jamm-mod.json` (or pass a higher `max_stacks` / `-1` to `jamm_upgrade_register`).

---

## Sprites look huge, tiny, sideways, or misaligned

* **Fish swims backwards or upside down**
  * Base *Cat Goes Fishing* fish sprites face **left** (head on the left, tail on the right). Flip your PNG horizontally if your fish swims tail-first.
* **Boat looks half-size in the water**
  * `Boat` draws its world sprite at **`0.5x`** scale (`image_xscale = 0.5`). Export the world boat PNG at **2x** the pixel size you want in-game (e.g. `200x72` renders as `100x36`).
* **Hat looks tiny or floating off the cat's head**
  * `Gear` draws hat sprites at **`0.25x`** scale (`4x` larger PNG -> normal on-screen size, e.g. `96x72` renders as `24x18`).
  * Adjust `"offsetX"` / `"offsetY"` (`EQUIP_OFFSET_X/Y`, default `[14, -42]`) to move the hat on the cat's head, and `"hatalogOffsetX"` / `"hatalogOffsetY"` to center its icon inside the Hatalog list.
* **Upgrade icon is clipped or offset**
  * ModKit sets upgrade icon origin to `(0, 0)` automatically and recommends `50x50`. If using the GML API directly, set your sprite's origin to `(0, 0)`.

---

## Area placement and composed sprites

* **`[CGF-AREA-001]` in check or build** — Review the named area, room and other mod. Use a `follow` anchor to keep the area's offsets when a landmark moves. Inspect both layouts if the other mod also edits the surrounding terrain.
* **Area cannot find its anchor** — Confirm the object and exact coordinates in the original archive. If a selected mod removes it or replaces it with several instances, choose a unique landmark or a fixed location.
* **No free-water spawn points** — Enlarge `spawn.within` or the area itself, move the region deeper, or reduce `count`. JAMM samples the merged room after the area pieces and lights are placed.
* **Recipe cannot find a base sprite or frame** — Use the exact sprite asset name and a zero-based frame index from the same legacy build. An object's name can differ from its sprite name.
* **Layer crop is outside the source frame** — Inspect the padded dimensions of the referenced frame. Other frames of the same sprite can use different texture dimensions.

## Merge & Verification Errors

* **`Object 'jmk_...' already exists` / `Script 'jamm_init_...' already exists`**
  * Two mods used the exact same `id` (or `id` + `key`). Change your package `id` in `jamm-mod.json` to something unique to your mod.
* **`Semantic reference verification failed`** (GML API mods)
  * Make sure every sprite, object, and script passed to `jamm_*_register` uses its asset name (not a hardcoded number), and avoid passing `0` where `-1` is expected for "none".
* **Updated ModKit files don't show up in-game (or `Mod name '...' already exists`)**
  * `cgfmm add` imports a compiled copy keyed by archive SHA-256. After editing `jamm-mod.json`, sprites, or hooks, delete profiles referencing the old build (`cgfmm profile delete dev`), run `cgfmm remove "My Mod"` (or pass `--name "My Mod v2"`), and re-run `cgfmm add my-mod`.
