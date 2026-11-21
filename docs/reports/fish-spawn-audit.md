# Fish Spawn Audit (`s1`–`s5` Sonar Initialization)

Audit of every code path in `src/Jamm.Core` and `examples/modkit` that creates instances of `Fish` descendants, checking whether each path initializes `s1`–`s5` (used by `SonarPanel_Draw_0`).

In the August 2023 base, `SonarPanel` Step subtype 1 initializes `s1`–`s4` (sprite, frame, scale) and `s5` (sonar alpha) on active `Fish` instances. Any `Fish` descendant created after `SonarPanel` Step subtype 1 runs in a frame—or rendered on a `SmoothMod` render frame before `SonarPanel` Step subtype 1 runs—will crash `gml_Object_SonarPanel_Draw_0` with `Variable <fish>.s5 not set before reading it` unless `s1`–`s5` are initialized at spawn time.

## Summary

| Location | Feature | Creation Mechanism | Sets `s1`–`s5` today? |
| --- | --- | --- | --- |
| [`src/Jamm.Core/Building/Weather/population.gml#L25`](file:///home/varialbe/jamm-wt/fish-sonar-scene/src/Jamm.Core/Building/Weather/population.gml#L25) | Weather API / Weather Pack | Runtime `instance_create` in `jamm_weather_population()` (`BeginStep`) | **No** |
| [`src/Jamm.Core/Building/FishRuntime.cs#L274`](file:///home/varialbe/jamm-wt/fish-sonar-scene/src/Jamm.Core/Building/FishRuntime.cs#L274) | Fish API (`jamm_fish_register`) | Runtime `instance_create` in `jamm_api_init()` (`SaveManager_Step_0`) | **No** |
| [`src/Jamm.Core/Builtins/Worldgen/jmk_endless_ocean_load.gml#L30`](file:///home/varialbe/jamm-wt/fish-sonar-scene/src/Jamm.Core/Builtins/Worldgen/jmk_endless_ocean_load.gml#L30) | Endless Ocean chunk streaming | Runtime `instance_create` in `jmk_endless_ocean_load()` (`BeginStep`) | **Yes** (`jmk_endless_ocean_sonar` at line 33) |
| [`src/Jamm.Core/Building/AreaPlacement.cs#L147`](file:///home/varialbe/jamm-wt/fish-sonar-scene/src/Jamm.Core/Building/AreaPlacement.cs#L147) | Area API (`spawn.area` / The Silo) | Build-time `UndertaleRoom.GameObject` room placement (`Place` at line 236) | **No** (relies on room-start `SonarPanel` Step subtype 1) |
| [`src/Jamm.Core/Building/StructuredAssets.cs#L166`](file:///home/varialbe/jamm-wt/fish-sonar-scene/src/Jamm.Core/Building/StructuredAssets.cs#L166) | Third-party patch room instances | Build-time `UndertaleRoom.GameObject` deserialization in `Instances()` | **No** (preserves source mod creation code) |

## Detailed Findings

### 1. Weather API (`jamm_weather_population`) — missing `s1`–`s5`
- **Spawn site**: [`src/Jamm.Core/Building/Weather/population.gml#L25`](file:///home/varialbe/jamm-wt/fish-sonar-scene/src/Jamm.Core/Building/Weather/population.gml#L25) (`var fish = instance_create(sx, sy, species);`).
- **Triggers**:
  - [`src/Jamm.Core/Building/Weather/change.gml#L70`](file:///home/varialbe/jamm-wt/fish-sonar-scene/src/Jamm.Core/Building/Weather/change.gml#L70) when switching active weather (`jamm_weather_change`).
  - [`src/Jamm.Core/Building/Weather/step.gml#L80`](file:///home/varialbe/jamm-wt/fish-sonar-scene/src/Jamm.Core/Building/Weather/step.gml#L80) every 600 ms in `obj_jamm_weather` Begin Step (`Step_1`).
- **ModKit wiring & examples**:
  - [`src/Jamm.Core/ModKit/ModKitWeatherBuilder.cs#L27`](file:///home/varialbe/jamm-wt/fish-sonar-scene/src/Jamm.Core/ModKit/ModKitWeatherBuilder.cs#L27) and [`src/Jamm.Core/Building/WeatherRuntime.cs#L171-L180`](file:///home/varialbe/jamm-wt/fish-sonar-scene/src/Jamm.Core/Building/WeatherRuntime.cs#L171-L180) populate `weather_fish` with `Fish` descendants.
  - [`examples/modkit/weather-pack/jamm-mod.json#L1136-L1321`](file:///home/varialbe/jamm-wt/fish-sonar-scene/examples/modkit/weather-pack/jamm-mod.json#L1136-L1321) defines the eight weather fish (`bask`, `flaret`, `drizzit`, `lowbelly`, `kest`, `tack`, `mote`, `veilmouth`).
- **Status**: Sets `base_lunge_mod`, `jamm_weather_owner`, `jamm_weather_school_anchor`, and `stayOutAtNight` (`population.gml:29-32`), but **does not set `s1`–`s5`**. This caused the autoplay crash on `jmk_weather_pack_lowbelly.s5` (`~/jamm-night/autoplay/finalb-11002/error.txt`).

### 2. Fish API (`jamm_fish_register`) — missing `s1`–`s5`
- **Spawn site**: [`src/Jamm.Core/Building/FishRuntime.cs#L274`](file:///home/varialbe/jamm-wt/fish-sonar-scene/src/Jamm.Core/Building/FishRuntime.cs#L274) (`jamm_inst = instance_create(jamm_sx, jamm_sy, jamm_obj);`).
- **Triggers**:
  - Generated into `gml_Script_jamm_fish_register` by `FishRuntime.BuildFishRegisterGml`.
  - Called during `jamm_api_init()` from `gml_Object_SaveManager_Step_0` ([`src/Jamm.Core/Building/FishRuntime.cs#L113-L146`](file:///home/varialbe/jamm-wt/fish-sonar-scene/src/Jamm.Core/Building/FishRuntime.cs#L113-L146) or [`src/Jamm.Core/Building/ItemsRuntimeScripts.cs#L90-L128`](file:///home/varialbe/jamm-wt/fish-sonar-scene/src/Jamm.Core/Building/ItemsRuntimeScripts.cs#L90-L128)).
- **ModKit wiring & examples**:
  - [`src/Jamm.Core/ModKit/ModKitBuilder.cs#L221-L225`](file:///home/varialbe/jamm-wt/fish-sonar-scene/src/Jamm.Core/ModKit/ModKitBuilder.cs#L221-L225) emits `jamm_fish_register(...)` with `count > 0` for standalone ocean-spawning `fish[]` entries (when `spawn.area == null` and not in `weathers[].fish`).
  - [`examples/modkit/example-fish/jamm-mod.json#L20`](file:///home/varialbe/jamm-wt/fish-sonar-scene/examples/modkit/example-fish/jamm-mod.json#L20) (`goldie`, count 4) and [`examples/modkit/example-fish/jamm-mod.json#L37`](file:///home/varialbe/jamm-wt/fish-sonar-scene/examples/modkit/example-fish/jamm-mod.json#L37) (`glowfin`, count 3). (For `silo` and `weather-pack`, `ModKitBuilder.cs:223` passes `count = 0`.)
- **Status**: Sets `start_x`, `start_y`, `TRUE_START_X`, `TRUE_START_Y`, `nothungry_x`, `nothungry_y`, `catalog_index`, and `exotic` (`FishRuntime.cs:280-285, 329-333`), but **does not set `s1`–`s5`**. Because `SaveManager_Step_0` runs in Step subtype 0 (after `SonarPanel` Step subtype 1), newly spawned fish can reach `SonarPanel_Draw_0` on the same frame without `s1`–`s5`.

### 3. Endless Ocean Worldgen (`jmk_endless_ocean_load`) — sets `s1`–`s5`
- **Spawn site**: [`src/Jamm.Core/Builtins/Worldgen/jmk_endless_ocean_load.gml#L30`](file:///home/varialbe/jamm-wt/fish-sonar-scene/src/Jamm.Core/Builtins/Worldgen/jmk_endless_ocean_load.gml#L30) (`var swimmer = instance_create(sx, sy, species);`).
- **Triggers**: [`src/Jamm.Core/Builtins/Worldgen/jamm_chunk_stream.gml#L31`](file:///home/varialbe/jamm-wt/fish-sonar-scene/src/Jamm.Core/Builtins/Worldgen/jamm_chunk_stream.gml#L31) from `obj_jmk_endless_ocean` Begin Step ([`src/Jamm.Core/Builtins/Worldgen/step.gml#L25`](file:///home/varialbe/jamm-wt/fish-sonar-scene/src/Jamm.Core/Builtins/Worldgen/step.gml#L25)), configured via [`examples/modkit/endless-ocean/jamm-recipe.json`](file:///home/varialbe/jamm-wt/fish-sonar-scene/examples/modkit/endless-ocean/jamm-recipe.json) and [`src/Jamm.Core/Builtins/EndlessOceanMod.cs`](file:///home/varialbe/jamm-wt/fish-sonar-scene/src/Jamm.Core/Builtins/EndlessOceanMod.cs).
- **Status**: **Sets `s1`–`s5` today.** Line 33 immediately calls `jmk_endless_ocean_sonar(swimmer)` ([`src/Jamm.Core/Builtins/Worldgen/jmk_endless_ocean_sonar.gml#L1-L48`](file:///home/varialbe/jamm-wt/fish-sonar-scene/src/Jamm.Core/Builtins/Worldgen/jmk_endless_ocean_sonar.gml#L1-L48)), covering both basic sonar (`spr_sonarblip`) and advanced/cave sonar (`sprite_index` / head sprites).

### 4. Area API Room Placement (`AreaPlacement`) — build-time room instances
- **Spawn site**: [`src/Jamm.Core/Building/AreaPlacement.cs#L147`](file:///home/varialbe/jamm-wt/fish-sonar-scene/src/Jamm.Core/Building/AreaPlacement.cs#L147) calling `Place(placement.Room, obj, x, y)` ([`AreaPlacement.cs#L234-L239`](file:///home/varialbe/jamm-wt/fish-sonar-scene/src/Jamm.Core/Building/AreaPlacement.cs#L234-L239)).
- **ModKit wiring & examples**:
  - [`src/Jamm.Core/ModKit/ModKitBuilder.cs#L330-L340`](file:///home/varialbe/jamm-wt/fish-sonar-scene/src/Jamm.Core/ModKit/ModKitBuilder.cs#L330-L340) emits area declarations for `fish[]` entries with `spawn.area`.
  - [`examples/modkit/silo/jamm-mod.json#L583-L699`](file:///home/varialbe/jamm-wt/fish-sonar-scene/examples/modkit/silo/jamm-mod.json#L583-L699) places `veyl` (3), `radik` (3), `ferrex` (2), and `garrusk` (1) in `launch-shaft` (`NewWorld`).
- **Status**: **Does not set `s1`–`s5` at build time** (places plain `UndertaleRoom.GameObject` instances with no `CreationCode`). Because they exist from room start, vanilla `SonarPanel` Step subtype 1 initializes `s1`–`s5` before `SonarPanel_Draw_0` on normal steps, unless a render-only frame draws before the first Step subtype 1.

### 5. Third-Party Patch Room Instances (`StructuredAssets`)
- **Spawn site**: [`src/Jamm.Core/Building/StructuredAssets.cs#L166`](file:///home/varialbe/jamm-wt/fish-sonar-scene/src/Jamm.Core/Building/StructuredAssets.cs#L166) (`new UndertaleRoom.GameObject()` added to `room.GameObjects` when applying room diffs from `.win` mod patches).
- **Status**: **Does not add `s1`–`s5`** (preserves whatever creation code or events the source `.win` mod shipped).

## Other Inspected Paths (No `Fish` Instantiation)
- [`src/Jamm.Core/Builtins/FishGrid.cs#L231-L236`](file:///home/varialbe/jamm-wt/fish-sonar-scene/src/Jamm.Core/Builtins/FishGrid.cs#L231-L236): Wraps `instance_create`, `instance_copy`, and `instance_change` (`jamm_fg_*`) to invalidate the `FishLarge` spatial grid snapshot; does not originate new fish spawns or modify `s1`–`s5`.
- Other `examples/modkit` packages (`example-rod`, `harbor-pack`, `harbor-town`, `showcase-upgrades`, `submarine`): None define `fish[]` or instantiate `Fish` descendants (`submarine` creates only `Light` and `CastShield`; `harbor-town/scripts/fish.gml` only reads caught sprites from `CatalogData`).
