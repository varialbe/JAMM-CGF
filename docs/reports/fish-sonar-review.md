# Code Review: Shared Fish Sonar Initializer (`fix/fish-sonar`, `3db05c6`)

Reviewed commit `3db05c6` on branch `fix/fish-sonar` in `/home/varialbe/jamm-wt/fish-sonar` against [docs/tasks/fish-sonar-init.md](file:///home/varialbe/jamm-wt/fish-sonar/docs/tasks/fish-sonar-init.md), [docs/reports/fish-spawn-audit.md](file:///home/varialbe/jamm-wt/fish-sonar-scene/docs/reports/fish-spawn-audit.md), and the vanilla August 2023 `SonarPanel` GML dumps.

## Verdict

**Approve with minor findings.** The shared `jamm_fish_sonar_init(inst)` implementation covers every dynamic JAMM `Fish` spawn path, guards callers and callee with `instance_exists`, matches vanilla `SonarPanel` Step subtype 1 semantics without bare vanilla asset indices, leaves vanilla fish and events untouched, and passes all unit tests. There are no blocking bugs; one medium test-coverage gap and two low hardening/test issues are noted below.

## Checklist Verification

1. **Every spawn path from the audit calls the initializer only when the instance exists — Pass (for all runtime spawners)**
   - **Weather API** ([`src/Jamm.Core/Building/Weather/population.gml#L25-L28`](file:///home/varialbe/jamm-wt/fish-sonar/src/Jamm.Core/Building/Weather/population.gml#L25-L28), [`src/Jamm.Core/Building/WeatherRuntime.cs#L29`](file:///home/varialbe/jamm-wt/fish-sonar/src/Jamm.Core/Building/WeatherRuntime.cs#L29)): calls `jamm_fish_sonar_init(fish)` immediately after `instance_create(sx, sy, species)` inside `if (instance_exists(fish))`.
   - **Fish API** ([`src/Jamm.Core/Building/FishRuntime.cs#L169`](file:///home/varialbe/jamm-wt/fish-sonar/src/Jamm.Core/Building/FishRuntime.cs#L169), [`src/Jamm.Core/Building/FishRuntime.cs#L276-L282`](file:///home/varialbe/jamm-wt/fish-sonar/src/Jamm.Core/Building/FishRuntime.cs#L276-L282)): calls `jamm_fish_sonar_init(jamm_inst)` inside `if (instance_exists(jamm_inst))` before initializing `start_x` / metadata.
   - **Endless Ocean** ([`src/Jamm.Core/Builtins/Worldgen/jmk_endless_ocean_load.gml#L30-L32`](file:///home/varialbe/jamm-wt/fish-sonar/src/Jamm.Core/Builtins/Worldgen/jmk_endless_ocean_load.gml#L30-L32)): now checks `if (!instance_exists(swimmer)) continue;` before calling `jamm_fish_sonar_init(swimmer)`.
   - **Callee guard** ([`src/Jamm.Core/Builtins/Worldgen/jamm_fish_sonar_init.gml#L1-L2`](file:///home/varialbe/jamm-wt/fish-sonar/src/Jamm.Core/Builtins/Worldgen/jamm_fish_sonar_init.gml#L1-L2)): returns `0` immediately if `!instance_exists(swimmer)`.
   - **Static room placement** ([`src/Jamm.Core/Building/AreaPlacement.cs#L147`](file:///home/varialbe/jamm-wt/fish-sonar/src/Jamm.Core/Building/AreaPlacement.cs#L147), [`src/Jamm.Core/Building/StructuredAssets.cs#L166`](file:///home/varialbe/jamm-wt/fish-sonar/src/Jamm.Core/Building/StructuredAssets.cs#L166)): unchanged (see Info note below).

2. **Semantics match vanilla `SonarPanel` Step subtype 1 exactly — Pass**
   - Compared [`src/Jamm.Core/Builtins/Worldgen/jamm_fish_sonar_init.gml#L1-L50`](file:///home/varialbe/jamm-wt/fish-sonar/src/Jamm.Core/Builtins/Worldgen/jamm_fish_sonar_init.gml#L1-L50) against vanilla `gml_Object_SonarPanel_Step_1.gml`:
     - Resolves `SonarPanelAdvanced` (`advancedDrawEnabled`) and `SonarPanel` (`cave_sonar`) with `asset_get_index`, `instance_exists`, and `instance_find(..., 0)`, defaulting `advanced` to `false` when either panel is absent.
     - Advanced branch sets `s1 = sprite_index`, overrides `s1` to `spr_abyssal_head` / `spr_dragon_head` / `spr_problem_head` for `Abyssal` / `Dragon` / `Problematicus`, and sets `s2 = image_index`, `s3 = image_xscale`, `s4 = image_yscale`, `s5 = 1`.
     - Regular branch sets `s1 = asset_get_index("spr_sonarblip")`, `s2 = display_size - 1`, `s3 = 10 + (5 * (display_size - 1))`, `s4 = 10 + (3 * (display_size - 1))`, `s5 = (size + 0.5) / 3.5`.

3. **No bare vanilla asset indices — Pass**
   - All 9 vanilla object/sprite references in [`jamm_fish_sonar_init.gml`](file:///home/varialbe/jamm-wt/fish-sonar/src/Jamm.Core/Builtins/Worldgen/jamm_fish_sonar_init.gml#L4-L43) use `asset_get_index("...")`. `EndlessOceanTests.GeneratedFishInitializeSonarStateBeforeTheyCanBeDrawn` shifts object and sprite tables past index 500 and verifies both decompiled code and `Int16`/`Int32` bytecode operands against `[132, 135, 225, 238, 242, 334, 424, 429, 430]`.

4. **The script is emitted once — Pass (with Low defensive gap)**
   - [`FishSonarRuntime.Ensure`](file:///home/varialbe/jamm-wt/fish-sonar/src/Jamm.Core/Building/FishSonarRuntime.cs#L10-L29) only creates the `UndertaleScript` when missing and writes one entry in `recompile`/`generated`.
   - [`EndlessOceanMod.Apply`](file:///home/varialbe/jamm-wt/fish-sonar/src/Jamm.Core/Builtins/EndlessOceanMod.cs#L46-L71) exempts `jamm_fish_sonar_init` from its duplicate-script error check and reuses any pre-existing `gml_Script_jamm_fish_sonar_init` code entry.

5. **Vanilla fish and events unchanged — Pass**
   - `git show --stat 3db05c6` touches only `FishSonarRuntime.cs`, `FishRuntime.cs`, `WeatherRuntime.cs`, `Weather/population.gml`, `EndlessOceanMod.cs`, `Worldgen/jamm_fish_sonar_init.gml` (renamed from `jmk_endless_ocean_sonar.gml`), `Worldgen/jmk_endless_ocean_load.gml`, `docs/reports/fish-sonar-init.md`, and tests. No vanilla `Fish` objects, `SonarPanel` events, or other vanilla events are modified.

6. **Tests meaningful — Pass with Medium gap**
   - Verified locally in `/home/varialbe/jamm-wt/fish-sonar`:
     - `dotnet test /home/varialbe/jamm-wt/fish-sonar -c Release --no-build`: `Jamm.Core.Tests` 645 passed, 17 skipped (real-file opt-in tests), 0 failed; `Jamm.Gui.Tests` 130 passed, 1 skipped, 0 failed.
     - `node --test /home/varialbe/jamm-wt/fish-sonar/tests/worldgen/chunks.test.mjs`: 8 passed, 0 failed.
   - Note: `tests/worldgen/chunks.test.mjs` stubs `jamm_fish_sonar_init` in JS, and the xUnit synthetic archive tests verify compiled/decompiled GML rather than executing the GM1.4 bytecode directly (end-to-end runtime execution was covered by Codex's autoplay runs reported in [`docs/reports/fish-sonar-init.md`](file:///home/varialbe/jamm-wt/fish-sonar/docs/reports/fish-sonar-init.md)).

## Findings

### 1. [Medium] Multi-feature shared script deduplication is not tested end-to-end across Fish API, Weather API, and Endless Ocean
- **Location**: [`tests/Jamm.Core.Tests/FishRuntimeTests.cs#L98-L112`](file:///home/varialbe/jamm-wt/fish-sonar/tests/Jamm.Core.Tests/FishRuntimeTests.cs#L98-L112), [`src/Jamm.Core/Builtins/EndlessOceanMod.cs#L45-L72`](file:///home/varialbe/jamm-wt/fish-sonar/src/Jamm.Core/Builtins/EndlessOceanMod.cs#L45-L72)
- **Details**: `SharedSonarInitializerIsAddedOnlyOnceForMultipleFishFeatures` only calls `FishSonarRuntime.Ensure` twice on an empty `UndertaleData` with empty dictionaries. No synthetic archive test builds Fish API + Weather API together via `ArchiveBuilder.Build`, nor applies `EndlessOceanMod.Apply` onto an archive that already received `jamm_fish_sonar_init` from `FishRuntime`/`WeatherRuntime` (leaving the exemption at `EndlessOceanMod.cs:46` and the existing-code reuse branch at `EndlessOceanMod.cs:65-71` uncovered by automated tests).
- **Concrete fix**: Add an integration test in `tests/Jamm.Core.Tests` that builds a synthetic archive containing both a `jamm_fish_register` mod and a Weather mod via `ArchiveBuilder.Build`, asserts `Assert.Single(merged.Data.Scripts, s => s.Name.Content == FishSonarRuntime.Script)` and `Assert.Single(merged.Data.Code, c => c.Name.Content == FishSonarRuntime.Code)`, and then runs `EndlessOceanMod.Apply(merged.Data)` (on a world containing the required Endless Ocean placeholder objects/scripts) and re-asserts single script/code emission.

### 2. [Low] `FishSonarRuntime.Ensure` does not check `data.Code` before calling `UndertaleCode.CreateEmptyEntry`
- **Location**: [`src/Jamm.Core/Building/FishSonarRuntime.cs#L13-L18`](file:///home/varialbe/jamm-wt/fish-sonar/src/Jamm.Core/Building/FishSonarRuntime.cs#L13-L18)
- **Details**: `FishSonarRuntime.Ensure` only checks `data.Scripts.ByName(Script) is null` before calling `UndertaleCode.CreateEmptyEntry(data, Code)`. If an input archive contains an orphaned `gml_Script_jamm_fish_sonar_init` entry in `data.Code` without a matching `UndertaleScript` entry (or an existing script whose `Code` property is `null`/different), `CreateEmptyEntry` will append a second `UndertaleCode` with the same name, causing `ArchiveBuilder.cs:437` (`data.Code.SingleOrDefault(...)`) to throw `InvalidOperationException`. By contrast, [`FishRuntime.EnsureScript`](file:///home/varialbe/jamm-wt/fish-sonar/src/Jamm.Core/Building/FishRuntime.cs#L358-L366) and [`EndlessOceanMod.Apply`](file:///home/varialbe/jamm-wt/fish-sonar/src/Jamm.Core/Builtins/EndlessOceanMod.cs#L65-L70) look up `data.Code` first.
- **Concrete fix**: Reuse `data.Code.ByName(Code)` and sync `existing.Code` just like `FishRuntime.EnsureScript`:
  ```csharp
  var code = data.Code.ByName(Code) ?? UndertaleCode.CreateEmptyEntry(data, Code);
  if (data.Scripts.ByName(Script) is { } existing)
      existing.Code = code;
  else
      data.Scripts.Add(new UndertaleScript
      {
          Name = data.Strings.MakeString(Script),
          Code = code
      });
  ```

### 3. [Low] `EndlessOceanTests` checks `jmk_endless_ocean_load` via raw `.gml` source rather than compiled/decompiled output
- **Location**: [`tests/Jamm.Core.Tests/EndlessOceanTests.cs#L172-L173`](file:///home/varialbe/jamm-wt/fish-sonar/tests/Jamm.Core.Tests/EndlessOceanTests.cs#L172-L173), [`tests/Jamm.Core.Tests/EndlessOceanTests.cs#L219-L225`](file:///home/varialbe/jamm-wt/fish-sonar/tests/Jamm.Core.Tests/EndlessOceanTests.cs#L219-L225)
- **Details**: `GeneratedFishInitializeSonarStateBeforeTheyCanBeDrawn` asserts `jamm_fish_sonar_init(swimmer)` and `if (!instance_exists(swimmer)) continue;` on `EndlessOceanMod.Source("jmk_endless_ocean_load")` (uncompiled text). While `GeneratorInitializesLungeModifiersExcludesSingletonFishAndClearsDeactivatedFlag` compiles `jmk_endless_ocean_load` into `compiledLoad`, it does not assert the `jamm_fish_sonar_init` call or `instance_exists` guard on `compiledLoad`.
- **Concrete fix**: Assert `Assert.Contains("jamm_fish_sonar_init(swimmer)", compiledLoad)` and `Assert.Contains("instance_exists(swimmer)", compiledLoad)` (ordering the guard before `jamm_fish_sonar_init` and `base_lunge_mod = 1`) on the compiled/decompiled `compiledLoad` in `EndlessOceanTests.cs#L219`.

### 4. [Info] Static room-placed `AreaPlacement` fish (`silo`) rely on room-start `SonarPanel_Step_1`
- **Location**: [`src/Jamm.Core/Building/AreaPlacement.cs#L147`](file:///home/varialbe/jamm-wt/fish-sonar/src/Jamm.Core/Building/AreaPlacement.cs#L147), [`src/Jamm.Core/Building/AreaPlacement.cs#L234-L239`](file:///home/varialbe/jamm-wt/fish-sonar/src/Jamm.Core/Building/AreaPlacement.cs#L234-L239)
- **Details**: `AreaPlacement` places `UndertaleRoom.GameObject` instances (`veyl`, `radik`, `ferrex`, `garrusk` in `NewWorld`) with no `CreationCode`. Because they exist from room start and `SmoothMod` sets `jamm_tick_now = 1` in `RoomStart` ([`src/Jamm.Core/Builtins/SmoothMod.cs#L213`](file:///home/varialbe/jamm-wt/fish-sonar/src/Jamm.Core/Builtins/SmoothMod.cs#L213)), tick 1 runs `SonarPanel_Step_1` before `SonarPanel_Draw_0`. No change is needed today, though if `AreaPlacement` ever supports deferred/dynamic spawning, those fish would need `jamm_fish_sonar_init(id)` as well.
