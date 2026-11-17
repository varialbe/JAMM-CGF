# How Saves Work

Adding, reordering, or removing mods in *Cat Goes Fishing* used to be risky because the unmodded game stores everything by **numeric slot index** (for example `boat8` or `f058`). Change your load order and two mods swap slots—or a removed mod's slot points at a different item.

JAMM fixes this for **items, gear, upgrades, and the fish catalog** by storing mod-friendly **name-keyed sections** (`[JAMM_Items]` and `[JAMM_Catalog]`) inside your existing save files alongside the standard base-game keys.

---

## Where *Cat Goes Fishing* stores saves

The legacy 2023 build writes standard INI save files (in your game/Wine/Proton user data folder, per save slot `<N>`):

* `sdata<N>n_beta.ini` — main save file for slot `<N>` (fallback `sdata<N>n.ini` on older saves)
* `sdata<N>n-backup.ini` — automatic backup written alongside `sdata<N>n_beta.ini`
* `meta.ini` — save-slot summary metadata (`[save<N>]` level, dollars, hat name, etc.)

JAMM never edits your save files during `cgfmm build` or `cgfmm apply`—the injected GML reads and writes `[JAMM_Items]` and `[JAMM_Catalog]` cleanly when you load and save inside the game.

---

## `[JAMM_Items]` — items, equipped gear, and rod upgrades

Whenever any API or ModKit item/upgrade mod is in your build, JAMM appends `[JAMM_Items]` saving and loading to `SaveManager` (`Other_14` / `Other_15` on save, `Other_13` on load).

### Keys written to `[JAMM_Items]`

| Key pattern | Meaning | Example value |
| :--- | :--- | :--- |
| `u:boat:<key>` | Unlock state (`1` owned, `0` in shop, `-1` hidden) | `u:boat:my-mod:raft=1` |
| `u:rod:<key>` | Rod unlock state | `u:rod:base:rod0=1` |
| `u:lure:<key>` | Lure availability | `u:lure:base:lure0=1` |
| `u:tool:<key>` | Tool unlock state | `u:tool:base:tool0=0` |
| `u:hat:<key>` | Hat unlock state (`1` unlocked in Hatalog, `0` locked) | `u:hat:my-mod:crown=1` |
| `u:upgrade:<key>` | Upgrade unlock state | `u:upgrade:showcase-upgrades:turbo=1` |
| `eq:boat` | Key of currently equipped boat (`""` for none) | `eq:boat=my-mod:raft` |
| `eq:rod` | Key of currently equipped rod | `eq:rod=base:rod2` |
| `eq:hat` | Key of currently equipped hat (`""` for none) | `eq:hat=my-mod:crown` |
| `slot:<rodkey>:<iy>` | Upgrade installed in rod slot `0..7` (`""` empty, `"#locked"` unavailable) | `slot:base:rod2:0=showcase-upgrades:turbo` |
| `slotobj:<rodkey>:<iy>` | Fish object name for Attract/Repel (`""` otherwise) | `slotobj:base:rod2:1=Goldfish` |

Base-game items automatically receive stable built-in keys: `base:boat0`..`base:boat7`, `base:rod0`..`base:rod5`, `base:lure0`..`base:lure5`, `base:tool0`..`base:tool2`, `base:hat0`..`base:hat29`, and `base:upgrade0`..`base:upgrade17`.

ModKit items receive `"<package-id>:<item-key>"` (for example `"showcase-upgrades:turbo"`).

---

## `[JAMM_Catalog]` — Fishalog progress

Whenever fish catalog changes or `jamm_fish_register` / `jamm_catalog_register` are present, JAMM stores Fishalog stats under `[JAMM_Catalog]` keyed by **object asset name** instead of numeric grid slot:

* `f:<object_name>` — best catch life (`-1` if uncaught, `0..3` for bone/life)
* `li:<object_name>` — whether the fish has been seen/unlocked in the catalog
* `pop:<object_name>` — current live ocean population count

For ModKit fish, the object asset name is `jmk_<id>_<key>` (with hyphens turned into underscores, e.g., `jmk_example_fish_goldie`). See [Catalog API](../catalog-api.md#saves-and-changing-profiles) for full details.

---

## Legacy saves (first time loading with JAMM)

* **Existing vanilla or unmigrated saves** (where `[JAMM_Items]` and/or `[JAMM_Catalog]` do not exist yet):
  * Legacy numeric reads (`[Unlocks]`, `[Main]`, `[Upgrades]`, `[Hatalog]`) are strictly clamped to `jamm_base_count` (vanilla slots only), and legacy writes are clamped the same way so vanilla saves remain clean.
  * Any newly added mod items/slots (`>= jamm_base_count`) start at their `Create_0` defaults instead of accidentally reading leftover numbers.
  * For `[Catalog]`, unambiguous vanilla fish migrate automatically; ambiguous legacy mod slots start undiscovered with a one-time notice, while keeping the original `[Catalog]` keys intact in the file.
* **Once you save in-game**, `[JAMM_Items]` and `[JAMM_Catalog]` are written alongside the clamped legacy sections. On future loads, JAMM reads your progress by stable key.

---

## Removing, reordering, and re-adding mods

* **Reordering mods in a profile**: Completely safe for `[JAMM_Items]` and `[JAMM_Catalog]`—every item, rod upgrade, equipped hat/boat/rod, and fish catalog entry resolves by key/name, not load order.
* **Removing a mod**:
  * JAMM **never deletes** unknown keys from `[JAMM_Items]` or `[JAMM_Catalog]`; keys belonging to unloaded mods simply stay in your `.ini` file untouched.
  * If you were wearing or equipping something from a removed mod, JAMM falls back safely on load:
    * Removed equipped boat → **No Boat**
    * Removed equipped rod → **Basic Rod** (slot `0`)
    * Removed equipped hat → **no hat** (unequipped cleanly without triggering a duplicate-hat sell payout)
    * Removed rod upgrade → that rod slot becomes **empty** (`-1`)
* **Re-adding a mod later**: As long as the mod keeps the same string `key` (and object name for fish), your unlocks, upgrades, and Fishalog records come right back.

> [!WARNING]
> **What is still legacy/numeric**: A few older world-state sections in *Cat Goes Fishing*—specifically fish currently sitting on your boat (`[OnBoat]`), live line/lure fish (`[LF]`, `[Fishes]`), and placed bulb lamps (`[Lamp]`)—still store raw GameMaker object/instance IDs. **Always empty your boat and reel in your lure** before switching mod profiles!

---

## What to back up

1. **Before switching profiles or testing new mods**: Back up your save folder (`sdata*n_beta.ini`, `sdata*n-backup.ini`, `meta.ini`).
2. **Never delete your original game backup**: JAMM keeps SHA-256-verified backups at `data.win.cgfmm-original` inside your modded game folder and under `<data-dir>/backups/`. Use `cgfmm restore` (or **Restore original** in the GUI) to return to vanilla `data.win` at any time.
