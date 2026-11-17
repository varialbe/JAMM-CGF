# Phase 22c — Custom Upgrades API (`jamm_upgrade_register`, Effects, Icons, Limits, and Persistence)

Implemented sub-task 22c from `docs/tasks/phase22cd-upgrades-modkit.md` by extending the Phase 22b Item API runtime (`ItemsRuntime`, `ItemsRuntimeScripts`, `ItemsRuntimePatches`, `CgfAssetTypes`). No commits, pushes, git config changes, or submodule edits were made.

## Changes

- **Runtime registration & orchestration (`src/Jamm.Core/Building/ItemsRuntime.cs`, `ItemsRuntimeScripts.cs`)**:
  - Added `"jamm_upgrade_register"` to `ItemsRuntime.RegistrationFunctions` so mods that register custom upgrades (either via `jamm_init_*` or directly in code) trigger runtime injection, while non-API builds remain untouched.
  - Added `jamm_upgrade_register(key, name, desc, desc2, cost, icon_sprite, max_stacks, apply_script, step_script)`:
    - Validates all 9 arguments (`key`/`name`/`desc`/`desc2` strings, non-negative real `cost`, `icon_sprite` `-1` or existing sprite, `max_stacks` `-1` or positive integer, `apply_script`/`step_script` `-1` or existing script).
    - Calls `jamm_items_ensure_base()`, deduplicates by key via `jamm_item_find("upgrade", key)`, appends at `array_length_1d(UpgradeData.unlocks)` (never hardcoding `18`), sets `jamm_key`, `name`, `desc`, `desc2`, `cost`, `unlocks = 1`, `jamm_default_unlocked = 1`, `disp`, `jamm_max_stacks`, `jamm_apply`, `jamm_step`, `jamm_is_api = 1`, updates `UpgradeData.nItems`, and initializes `Girl.gear_upgrade_scalar` / `Gear.upgrade_scalar` for the new slot.
  - Updated `jamm_items_ensure_base()` to capture `UpgradeData.nItems = array_length_1d(unlocks)` and `jamm_base_count`, initialize `disp`/`jamm_max_stacks`/`jamm_apply`/`jamm_step`/`jamm_is_api` across all pre-existing upgrade indices (`0..17`, or `0..18+` when legacy mods such as Pondweed Basin append upgrades), and ensure `Girl.gear_upgrade_scalar` covers all pre-existing upgrade indices.
  - Added `jamm_upgrade_stacks(key)` returning the stack count on the current rod from `Rod.jamm_upg` (with a safe fallback scan over `UpgradeData.upg[Rod.rod_id, 0..7]` when `Rod.jamm_upg` is not yet populated, respecting `Rod.allowUpgrades`).
  - Added persistent `obj_jamm_api` controller (`Create_0` singleton guard + `persistent = true`, `Step_1` Begin Step running `script_execute(UpgradeData.jamm_step[id], stacks)` for installed API upgrades once `UpgradeData.jamm_api_done` and `Rod.jamm_upg` are initialized):
    - `ItemsRuntime.Inject` creates/updates `obj_jamm_api` and places an instance first in `NewWorld` and `room11` when those rooms exist (matching `ConsoleMod`).
    - `jamm_api_init()` also creates `obj_jamm_api` if absent and triggers `with (Rod) event_perform(ev_other, ev_user0)` after `jamm_init_*` runs so `Rod.jamm_upg` is populated even on new saves or if `Rod_Step_0` ran earlier on frame 0.
  - Updated `jamm_items_load()` to clear stale numeric upgrade slot indices (`>= UpgradeData.jamm_base_count`) in legacy saves or missing `[JAMM_Items]` slot keys, while keeping `[JAMM_Items]` `u:upgrade:<key>` and `slot:<rodkey>:<iy>` round-tripping and turning removed mod upgrade keys into empty slots (`-1`), followed by a `Rod` `ev_user0` refresh if not already refreshed by `eq:rod`/`eq:hat`.

- **Typed asset relinking (`src/Jamm.Core/Building/CgfAssetTypes.cs`)**:
  - Registered `jamm_upgrade_register` (`9` args: arg `5` -> `sprites`, args `7` and `8` -> `scripts`) and extended `CgfAssetTypes.Register` to resolve `"scripts"` to `Asset.Script`, enabling Underanalyzer decompilation and multi-mod relinking of custom upgrade icon sprites and `apply_script`/`step_script` assets.

- **Icon draw, stack limits, effects, Girl/Gear scalars, and SaveManager patches (`src/Jamm.Core/Building/ItemsRuntimePatches.cs`)**:
  - **Icons**: Patched `UpgradeCard_Other_11`, `UpgradeSlot_Other_11`, and `UpgradeData_Draw_0` via a shared helper so slots with `UpgradeData.disp[id] > -1` draw that custom sprite scaled proportionally to `spr_upgradeicons`, and otherwise clamp `1 + id` against `sprite_get_number(spr_upgradeicons)` (falling back to subimage `0` if out of range).
  - **Limits**: Patched `UpgradeCard_Other_10` right before `if (!allowPurchase) { instance_create(0, 0, Message_TooManyUpgrades); }` to count installed copies of API upgrades (`UpgradeData.jamm_is_api[card_id]`) in `UpgradeData.upg[Rod.rod_id, 0..7]` and reject purchases reaching `jamm_max_stacks[card_id]`, leaving base and legacy `switch (card_id)` rules untouched.
  - **Effects**: Extended `Rod_Other_10` after base resets and `with (Fish)` recomputation to build `Rod.jamm_upg[0..array_length_1d(UpgradeData.unlocks)-1]` (respecting `allowUpgrades`) and invoke `script_execute(UpgradeData.jamm_apply[id], stacks)` for installed API upgrades.
  - **Girl/Gear loops**: Patched `Girl_Other_10` and `Gear_Other_15` using regex anchors (tolerating `17`, `18`, etc. and `++` / `+= 1`) so `gear_upgrade_scalar` and `upgrade_scalar` size dynamically to `array_length_1d(UpgradeData.unlocks)` with bounds-checked copying from `gear.upgrade_scalar`.
  - **Achievements & SaveManager**: Guarded `upgrade_was_purchased` Steam achievements to `< UpgradeData.jamm_base_count`, and clamped `with (UpgradeData)` loops in `SaveManager_Other_13`, `Other_14`, and `Other_15` using balanced-block + regex matching (`jamm_base_count` / `RodData.jamm_base_count`).

- **Tests (`tests/Jamm.Core.Tests/UpgradesRuntimeTests.cs`)**:
  - Added 14 test cases (13 synthetic tests + 1 opt-in `[RealFilesFact]` test) covering direct/init triggering, `obj_jamm_api` creation/placement, placeholder replacement, multi-mod sprite/script relinking, draw/limit/effect/Girl/Gear/save/MenuRuntime composition, Pondweed Basin-style pre-added upgrade `18` and `7`-rod loop robustness, non-API no-op behavior, and `InvalidDataException` reporting for missing anchors across all patched upgrade entries.

## Design Decisions

1. **Extending `ItemsRuntime*` instead of a parallel runtime**: `jamm_upgrade_register` shares `jamm_api_init` discovery, `jamm_items_ensure_base`, `jamm_item_find("upgrade", ...)`, `[JAMM_Items]` save/load (`slot:` / `u:upgrade:`), and `SemanticCode.DefaultScripts` plumbing with 22b. Keeping them together avoids duplicate controller/init pipelines and handles mods that mix items and upgrades or ship only upgrades.
2. **Pondweed-tolerant regex + runtime `array_length_1d(UpgradeData.unlocks)`**: Legacy mods like Pondweed Basin add upgrade `18` and bump loop bounds (`<= 18`, `spawn_order[18]`, `case 18:`). Never assuming `18` at runtime or in anchors (regex for `Girl_Other_10`, `Gear_Other_15`, `SaveManager_Other_13..15` `with (UpgradeData)` loops, and anchoring `UpgradeCard_Other_10` on `Message_TooManyUpgrades` after the `switch`) keeps both vanilla and legacy-extended archives working cleanly.
3. **`> -1` guards on `disp`, `jamm_apply`, and `jamm_step`**: Because `disp` is registered in `cgf-asset-types.json` and `0` is a valid asset index for sprites/scripts, checking `> -1` (never `>= 0`) prevents `SemanticReferences.NameTypedLiterals` from misinterpreting `-1` comparisons and preserves asset index `0`.
4. **Dual `obj_jamm_api` placement + `jamm_api_init()` safeguard**: Placing `obj_jamm_api` first in `NewWorld` and `room11` (when present) matches `ConsoleMod`, while `jamm_api_init()` creating `obj_jamm_api` if absent covers minimal/custom rooms; `Create_0`'s `instance_number(object_index) > 1` check prevents duplicates across `room_restart()`.
5. **Conditional upgrade-scalar patching in `Girl_Other_10` / `Gear_Other_15`**: Existing 22b test stubs in `ItemsRuntimeTests` omit `gear_upgrade_scalar` from `Girl_Other_10` and `Gear_Other_15`. Patching the upgrade-scalar loops whenever `gear_upgrade_scalar` / `upgrade_scalar` appears (and throwing `InvalidDataException` if present with an unexpected shape) enforces strict anchor checking on full archives while keeping existing 22b tests compatible.

## Verification

- **Read-only anchor audit**: Checked all 12 22c literal and regex anchors against decompiled base GML files in `/home/varialbe/wt/audit/base-gml/`; every anchor matched `1:1`.
- **`dotnet build -c Release`**: Succeeded with **0 Warning(s), 0 Error(s)**.
- **`dotnet test tests/Jamm.Core.Tests -c Release --no-build`**: **327 passed, 0 failed, 8 skipped** (`[RealFilesFact]` opt-in tests skipped because `JAMM_ORIGINAL` and `JAMM_MODS_DIR` are unset in this environment). All 13 synthetic tests in `UpgradesRuntimeTests` passed.
- **`dotnet test tests/Jamm.Gui.Tests -c Release --no-build`**: **108 passed, 0 failed, 1 skipped** (`RealGuiTests` opt-in test skipped without `JAMM_ORIGINAL`).

## Risks and In-Game Verification Needs

- **Real-archive build & ModKit integration**: `RealOriginalArchiveWithUpgradeApiModBuildsAndVerifiesCleanly` and the other `[RealFilesFact]` tests were skipped without `JAMM_ORIGINAL` / `JAMM_MODS_DIR`. Run the test suite with those environment variables set and test building + merging the 22d `showcase-upgrades` ModKit package against the legacy 2023 `data.win`.
- **In-game GM VM behavior** (cannot run headless in Linux CI):
  - Custom upgrade icon scaling and alignment across `UpgradeCard` (shop), `UpgradeSlot` (rod slots), and `UpgradeData` (bottom-left HUD), both with custom `icon_sprite` and with `icon_sprite = -1`.
  - Per-upgrade `max_stacks` enforcement showing `Message_TooManyUpgrades` when exceeded, `Rod_Other_10` idempotent `apply_script(stacks)` recompute when installing/removing upgrades or equipping `Tophat` (`no_upgrades = 1`), and `obj_jamm_api` Begin Step `step_script(stacks)` execution.
  - `[JAMM_Items]` save/load round-trip of custom upgrades installed on base and custom rods, save-slot switching (`room_restart()`), and mod removal turning missing upgrade keys into empty slots cleanly.
