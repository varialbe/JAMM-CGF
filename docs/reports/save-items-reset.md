# Save item reset investigation

## Finding

The reset is a `[JAMM_Items]` write before the current save slot has finished loading, followed by the existing name-keyed loader treating that write as authoritative.

`ItemsRuntimePatches.PatchSaveWrite` inserts `jamm_items_save()` into both `gml_Object_SaveManager_Other_14` and `gml_Object_SaveManager_Other_15` just before each INI close (`src/Jamm.Core/Building/ItemsRuntimePatches.cs:137-165`). Before this fix, the generated `jamm_items_save` script called `jamm_items_ensure_base()` and wrote every registered item's current `unlocked` / `unlocks` values and equipped keys without checking whether `gml_Object_SaveManager_Other_13` had loaded a slot (`src/Jamm.Core/Building/ItemsRuntimeScripts.cs`, `BuildSave`). Consequently, an early save event can serialize fresh runtime defaults into `[JAMM_Items]`. `BuildLoad` sees the section and applies its values, including zero unlock flags, over the values read from legacy `[Unlocks]` and `[Upgrades]` by `Other_13` (`ItemsRuntimeScripts.cs:715-728`, with the old replacement logic at `BuildLoad`'s `LoadUnlockBlock`). Its section-present branch is why the defaults continue to win on later loads.

This behavior matches the supplied save evidence: `backup-good-20261123-0117.ini` has the progressed legacy state and no `[JAMM_Items]`; `main-corrupted-20261126.ini` has both reset legacy item keys and a defaults-filled `[JAMM_Items]`. The build logs show that the personal pack injected Items API persistence and included silo, weather, submarine, and Harbor Town; the Harbor Town runtime adds writes on load and both native save events (`HarborRuntime.Apply`, `src/Jamm.Core/Building/HarborRuntime.cs:18-29`). Those logs establish the available save paths but do not record the in-game order that triggered the first bad write, so the exact initiating event cannot be recovered from these files alone.

## Fix

Both `SaveManager` write events now require the per-instance `jamm_items_loaded` flag before entering their save block (`ItemsRuntimePatches.cs:141-144`). This protects vanilla keys and metadata as well as `[JAMM_Items]`. `Other_13` clears the flag before processing each actual slot load, and the generated `jamm_items_load` sets it only after its load/migration work completes. The `jamm_items_save` script also exits unless the flag is set (`ItemsRuntimeScripts.cs:607-612`), so direct calls cannot bypass the guard (`ItemsRuntimeScripts.cs:936-941`).

When a `[JAMM_Items]` item key is present, loading now compares it with the value already restored by the legacy load for base items and keeps the higher unlock value. A missing item key also starts from the base default and performs the same comparison (`ItemsRuntimeScripts.cs:944-958`). For base rods and legacy upgrade columns, explicit stale `#locked` / empty slot keys no longer erase a vanilla slot that already contains a purchased upgrade. Mod-added items still use their `[JAMM_Items]` value/default because they have no vanilla legacy slot. This makes base ownership and purchases monotonic across stale or partial sidecar data.

## Regression coverage

`ItemSaveWaitsForLoadAndStaleItemFlagsCannotRelockVanillaProgress` (`tests/Jamm.Core.Tests/UpgradesRuntimeTests.cs:650-674`) exercises the failing order: a save before load must not create a sidecar ownership key; a later load with stale locked item and upgrade values must keep a vanilla-owned rod and purchased upgrade; after load, the sidecar save records that owned state.

## Verification

Required command: `dotnet test tests/Jamm.Core.Tests -c Release` was attempted and could not compile the project because `external/UndertaleModTool/UndertaleModLib/UndertaleModLib.csproj` is absent in this worktree (MSBuild then reports missing `UndertaleModLib` / `Underanalyzer` references). The unmodified required command also hit the sandbox's denied named-pipe socket when MSBuild attempted parallel nodes. Retrying with `-m:1 -p:NuGetAudit=false` avoids those environment issues but still fails on the absent submodule. No tests ran.

Real-build decompilation was attempted by building `tools/Jamm.ModMaker`, but this worktree's `external/UndertaleModTool` submodule directory is empty, so the tool cannot build. The initial build also hit NuGet network audit access; disabling audit exposed the missing submodule reference. No game or GUI process was launched, and the protected personal saves/build directories were only read/listed.
