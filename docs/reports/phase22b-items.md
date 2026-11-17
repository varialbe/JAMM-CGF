# Phase 22b — Item Registration API & `[JAMM_Items]` Persistence

Implemented sub-task 22b from `docs/tasks/phase22-modding-api.md` in `Jamm.Core` without touching menu paging (`CardHandler` / `UpgradeMenu`, 22a), custom upgrades (`jamm_upgrade_register`, 22c), or fish spawning (`jamm_fish_register`, 22d).

## What Changed

- **Runtime injection & orchestration (`src/Jamm.Core/Building/ItemsRuntime.cs`, `ArchiveBuilder.cs`, `SemanticCode.cs`)**:
  - Wired `ItemsRuntime.PreparePlan` and `ItemsRuntime.Inject` into `ArchiveBuilder` alongside `CatalogRuntime` and `HudRuntime`, and added `ItemsRuntimeScripts.DefaultScripts` / `SemanticCode.BaseNames` support in `SemanticCode`.
  - Triggers **only** when the merged archive contains at least one `jamm_init_*` script or a non-API code entry calls a `jamm_<type>_register` function (`jamm_boat_register`, `jamm_rod_register`, `jamm_lure_register`, `jamm_tool_register`, `jamm_hat_register`). Non-API builds remain unchanged.
  - Discovers `jamm_init_*` scripts in deterministic `(Source, Name)` order (profile/load order, then ordinal name), generates `jamm_api_init`, replaces any mod-shipped API placeholder scripts, and injects a `jamm_api_init()` call before `if (!loaded)` in `SaveManager` `Step_0` guarded by `jamm_api_done = 1` per `*Data` instance lifetime.

- **GML API & runtime scripts (`src/Jamm.Core/Building/ItemsRuntimeScripts.cs`)**:
  - `jamm_items_ensure_base`: Lazily populates `jamm_base_count`, `jamm_default_unlocked`, and parallel `jamm_key[]` arrays on `BoatData`, `RodData`, `LureData`, `ToolData`, `GearData`, and `UpgradeData` (`base:boat0..`, `base:rod0..`, `base:lure0..`, `base:tool0..`, `base:hat0..`, `base:upgrade0..`), initializes `disp[] = -1` on `RodData`/`ToolData`, and caches `UpgradeData.jamm_default_upg` and `UpgradeData.nItems`.
  - `jamm_item_find(type, key)`: Resolves `"boat"`, `"rod"`, `"lure"`, `"tool"`, `"hat"`, and `"upgrade"` keys to slot indices or `-1`.
  - `jamm_boat_register`, `jamm_rod_register`, `jamm_lure_register`, `jamm_tool_register`, `jamm_hat_register`: Validate inputs, return existing slots idempotently by key, append to `*Data` arrays, initialize `UpgradeData.upg`/`object` rows for custom rods (default `8` slots), initialize `Girl.gear_lure_scalar`/`Gear.lure_scalar = 1` for custom lures, instantiate `item_object` if needed for custom tools, and rebuild `GearData.map[]` sorting for custom hats.
  - `jamm_items_save` & `jamm_items_load`: Persist and restore `[JAMM_Items]` (`u:<type>:<key>`, `eq:boat`, `eq:rod`, `eq:hat`, `slot:<rodkey>:<iy>` with `""`/`"#locked"`/upgrade key, `slotobj:<rodkey>:<iy>` via `object_get_name` / `asset_get_index`).

- **Typed asset relinking (`src/Jamm.Core/Building/CgfAssetTypes.cs`, `SemanticReferences.cs`)**:
  - Registered multi-arity `UnionMacroType` and `ArgumentFamily` mappings for all five `jamm_*_register` functions so sprite and object arguments decompile as named assets and relink across mods.

- **Draw, save/load, and safety patches (`src/Jamm.Core/Building/ItemsRuntimePatches.cs`)**:
  - Draw patches: `RodCard_Other_11`, `Rod_Other_10`, and `ToolCard_Other_11` draw `disp[id]` when `> -1` and clamp `spr_rod`/`spr_tools` indexing with `sprite_get_number`; `BoatCard_Other_11` and `LureCard_Other_11` guard `disp[item_id] > -1`.
  - Save/load patches: `SaveManager_Other_13`, `Other_14`, and `Other_15` clamp all legacy `*Data` unlock loops, `[Main]` `activerod`/`gear`/`boat` writes, and Steam achievement calls to `< jamm_base_count`. `Other_13` resolves `eq:boat` before `OnBoat` attachment, skips legacy `gear` spawn when `eq:hat` exists (avoiding `Gear_Destroy_0` dollar payout), re-equips boat/rod/hat by key with safe fallbacks (no boat / rod 0 / no hat), keeps Create defaults for missing `[JAMM_Items]` keys, ignores unknown keys, and resets mod slots `>= jamm_base_count` when `[JAMM_Items]` is absent.
  - Safety patches: `BoatCard_Other_10` assigns `boat_index = item_id`; `ToolCard_Other_10` guards missing instances and `message == -1`; `Girl_Other_10` and `Gear_Other_15` size `lure_scalar`/`gear_lure_scalar` dynamically by `LureData.nItems`; `Rocket_Step_2`, `start_quest_dist`, and `start_quest_rods` clamp rod indices; `Rod_KeyPress_88` fixes `rid--` -> `rid++`; `*_was_unlocked` / `*_was_purchased` guard Steam achievements to `< jamm_base_count`.

- **Tests (`tests/Jamm.Core.Tests/ItemsRuntimeTests.cs`)**:
  - 8 synthetic-archive tests covering non-API no-op, deterministic `jamm_init_*` ordering + placeholder replacement, direct-call vs comment detection, all 5 registrations + `jamm_item_find` + multi-mod asset relinking, draw/safety/save/load patches, missing-anchor `InvalidDataException` diagnostics, minimal-stub builds, and Catalog + Items API combination.

## Design Decisions

1. **Lazy base initialization (`jamm_items_ensure_base`) instead of patching `*Data_Create_0`**: Avoids patching six `*Data_Create_0` entries that legacy mods commonly edit, while still guaranteeing `jamm_key[]`, `jamm_base_count`, and `jamm_default_unlocked[]` exist both when `jamm_api_init()` runs in `SaveManager_Step_0` and if a mod calls `jamm_*_register` directly inside `Create_0`.
2. **Dynamic `jamm_base_count` capture**: Capturing each `*Data` count before `jamm_init_*` registrations runs keeps legacy bounds accurate on the full game (`8, 6, 6, 3, 30, 18`) while also working naturally on smaller synthetic test stubs.
3. **Resolving `eq:boat` before `OnBoat` load and skipping legacy `gear` spawn when `eq:hat` exists**: Base `SaveManager_Other_13` attaches `OnBoat` fish right after spawning `Boat`, and `Gear_Destroy_0` pays dollars when destroying an equipped hat (`state == 2`). Resolving `eq:boat` upfront and skipping legacy `gear` spawn when `eq:hat` is present preserves `OnBoat` fish and avoids duplicate-hat payouts.
4. **`> -1` guards on `disp` and `object`**: Because `cgf-asset-types.json` marks `disp` and `object` as unscoped asset containers, comparing against `-1` prevents `SemanticReferences.NameTypedLiterals` and Underanalyzer from treating `0` as a sprite/object asset index.
5. **Conditional optional patches**: Core `SaveManager` entries are required (fail fast if missing), while peripheral code entries (cards, quests, `Rocket`, achievement scripts) are strictly anchor-validated whenever present and skipped only if absent in minimal synthetic stubs.

## Verification Results

- Anchor audit against read-only `/home/varialbe/wt/audit/base-gml/`: checked all 51 anchors across 21 decompiled base GML files with 0 mismatches.
- `dotnet build -c Release`: succeeded with 0 warnings and 0 errors across the full solution.
- `dotnet test tests/Jamm.Core.Tests -c Release`: 308 passed, 0 failed, 7 skipped (`[RealFilesFact]` opt-in tests skipped because `JAMM_ORIGINAL` / `JAMM_MODS_DIR` are unset).

## Risks / What Needs In-Game Verification

- **In-game GML runtime execution**: `ArchiveBuilder` + `ArchiveVerifier` prove every script and patched event compiles cleanly and round-trips through Underanalyzer with matching semantic asset references, but actual GameMaker VM execution cannot run in headless Linux CI. Needs in-game verification for:
  - Custom boat/rod/lure/tool/hat purchasing, equipping, card drawing (`disp_sprite`), and tool toggling.
  - Save/load round-trip in `sdata<N>n_beta.ini` and `sdata<N>n-backup.ini` (`[JAMM_Items]`), save slot switching (`room_restart()` re-running `jamm_api_init()` on re-created `*Data` objects), mod removal fallback behavior, and legacy-save mod slot reset.
  - `OnBoat` fish attachment onto a custom boat and hat unequip/re-equip without triggering `Gear_Destroy_0` payout on load.
