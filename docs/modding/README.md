# Modding Cat Goes Fishing with JAMM

Welcome! JAMM lets you add **new fish, boats, hats, rod upgrades, rods, lures, and tools** to the legacy 2023 VM build of *Cat Goes Fishing*—and run multiple mods side by side without editing shared game objects or breaking each other's saves.

## Which path is for me?

Choose the workflow that fits how you want to work:

| Path | Best for | Tools needed | What you ship |
| :--- | :--- | :--- | :--- |
| **[JAMM ModKit](getting-started.md)** | Most creators — adding fish, boats, hats, and rod upgrades quickly | Any text editor, an image editor for PNGs, and `cgfmm` | A small folder (`jamm-mod.json`, PNGs, optional `.gml` hooks) |
| **[GML Modding API](api-reference.md)** | UndertaleModTool authors, or anyone adding **rods, lures, or tools** (plus custom rooms/scripts) | UndertaleModTool + JAMM | A `.win` mod containing only new assets and a `jamm_init_<id>` script |
| **Legacy whole-`data.win` mods** | Existing mods built by editing base objects directly | UndertaleModTool | A full modified `data.win` (merged via JAMM's semantic diff/merge + [Catalog API](../catalog-api.md)) |

> [!TIP]
> **New to modding Cat Goes Fishing?** Start with **[Getting Started](getting-started.md)**—you can have a custom fish swimming near the dock in about 10 minutes.

## How JAMM merges mods without conflicts

Traditional *Cat Goes Fishing* mods edit `BoatData`, `RodData`, `UpgradeData`, `CatalogData`, `SaveManager`, or the `NewWorld` room directly—so two mods overwrite each other's items and fight over numeric slot IDs.

API and ModKit mods avoid shared assets completely:

1. **Isolated assets**: Your mod only adds *new* sprites, objects, hook scripts, and one or more `jamm_init_<modid>` scripts.
2. **Placeholders at compile time**: If your `.win` ships `return -1;` placeholder scripts for `jamm_*_register`, it compiles and runs standalone in UndertaleModTool or `cgfmm modkit build`.
3. **Runtime wiring at merge time**: When JAMM builds a profile, it generates `jamm_api_init` to call every `jamm_init_*` script in deterministic order (profile load order, then script name), replaces the placeholders with the real runtime API, injects [`obj_jamm_api`](api-reference.md#how-jamm_api_init-runs) for upgrade step hooks, and keeps `SaveManager`, shops, and `CatalogData` synchronized.
4. **Coexistence with legacy mods**: You can mix ModKit mods, GML API mods, built-in JAMM mods, and older `.win` mods in the same profile.

## Compatibility promise

* **Input files stay untouched**: JAMM always opens your legacy base `data.win` and mod archives read-only, and writes builds to new files after backing up your original.
* **Zero-config builds stay clean**: If a profile contains no API/ModKit mods, JAMM does not inject the item/upgrade/fish APIs (only [menu scrolling](menus.md) is added when the base shop menus are present).
* **Stable string keys, not fragile slot numbers**: Items are saved by stable string keys (`"mymod:sunfin"`) in `[JAMM_Items]` and by object asset name in `[JAMM_Catalog]`.
* **Safe to reorder, remove, and re-add**: Reordering mods keeps your unlocked items, equipped gear, and Fishalog counts intact. Removing a mod leaves its keys safely in your save file; if you add the mod back later, your progress returns.

## Documentation index

1. **[Getting Started (Your First Fish in 10 Minutes)](getting-started.md)** — scaffold, draw, validate, build, and catch your first fish.
2. **[ModKit Reference](modkit-reference.md)** — every `jamm-mod.json` field, defaults, validation errors & fixes, GML hook entry points, and art specs.
3. **[GML Runtime API Reference](api-reference.md)** — every `jamm_*_register` function (fish, boat, rod, lure, tool, hat, upgrade, catalog), `jamm_item_find`, `jamm_upgrade_stacks`, placeholders, and reserved namespaces.
4. **[Cookbook](cookbook.md)** — 9 copy-paste recipes (fast boat, value-boosting hat, step-hook upgrade, night-only fish, fleeing fish, reading upgrade stacks from a fish hook, multi-item mod, testing workflow, custom rod/lure).
5. **[How Saves Work](saves.md)** — `[JAMM_Items]`, `[JAMM_Catalog]`, legacy save migration, switching profiles, and backups.
6. **[Scrollable Menus](menus.md)** — how paging works in shops and upgrade menus for players and modders.
7. **[Troubleshooting](troubleshooting.md)** — common build, GML 1.4, sprite, gameplay, and merge fixes.
8. **[Catalog API](../catalog-api.md)** — deep dive into `jamm_catalog_register` and legacy fish-catalog relocation.
9. **[Chunk streaming](chunks.md)** for seeded layouts and instance ownership in legacy rooms.

## Examples in this repository

* [`examples/modkit/example-fish/`](../../examples/modkit/example-fish/) — **Goldie** (small shallow fish) and **Glowfin** (night-only medium fish with `create` and `step` hooks that pulse `image_blend` and dart away from moving lures).
* [`examples/modkit/showcase-upgrades/`](../../examples/modkit/showcase-upgrades/) — five idempotent rod upgrades (**Turbo Reel**, **Deep Diver**, **Iron Line**, **Fish Magnet**, **Golden Touch**) demonstrating both `apply` and `step` hooks.
* [`examples/modkit/harbor-pack/`](../../examples/modkit/harbor-pack/) — two boats (**Paddle Raft** and **Comet Cruiser**, triggering page 2 of the Boat shop alongside the 8 vanilla boats) and two hats (**Fisher's Beanie** and **Lucky Captain Hat**, with an `effect` hook boosting fish sale value).
* [`examples/modkit/make-art.py`](../../examples/modkit/make-art.py) — Python standard-library script used to generate the example pixel art PNGs.
* [Endless Ocean](../../examples/modkit/endless-ocean/) adds a seeded ocean floor and vanilla fish east of NewWorld.
