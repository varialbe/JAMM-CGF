# Endless Ocean sonar crash

## Cause

In the August 2023 base, `SonarPanel`'s Step subtype 1 initializes fish sonar draw state: `s1` through `s4` select the sprite, frame, and scale, while `s5` is the sonar alpha. The panel's Draw event later reads these instance variables from visible Fish instances. A fish streamed by Endless Ocean is created synchronously in a Begin Step callback. If that creation occurs after the panel's initialization pass, the fish can reach Draw without `s5`. Smooth's extra render frames expose this ordering gap between logic updates and draws. Perf does not initialize or alter these fields.

## Fix

The Endless Ocean generator now initializes `s1`–`s5` for each newly created fish immediately, using the same regular sonar and advanced sonar values as the vanilla panel. This runs before the fish is tagged and exposed to later draw work. Vanilla fish and vanilla sonar events are unchanged.

The ModMaker `worldgen` scene now enables the sonar view during its streaming route and records `sonar_missing` for generated fish at each checkpoint.

## Verification

- `dotnet build -c Release`: passed, 0 warnings and 0 errors.
- `dotnet test -c Release`: passed, 618 core and 130 GUI tests; 17 optional real-file tests skipped.
- Synthetic archive regression test compiles the new sonar initializer and checks regular and advanced draw state plus its call from the chunk loader.
- Personal package rebuilt with `TAG=sonar-fix`; archive verification passed. SHA-256: `745d8937b08904b0c8a9bafa9d0b60c449e765cb321c93c51730b92ddefa2e00`.
- `run.sh ... sonarfix worldgen 900`: completed and recorded all worldgen checkpoints. `sonar_missing` was 0 at every checkpoint, including chunks 19–504.
- Autoplay v2, seed 11002, 15 minutes: `PLAYED`, 145 actions, no error.
- Autoplay v2, seed 11003: startup-only `STEAM` failure (`SteamAPI_InitSafe() failed`), before gameplay.
- Autoplay v2, seed 11004, 15 minutes: `PLAYED`, 96 actions, no error. Reached x=81,704 with five chunks loaded; it did not travel to the original crash coordinate.

The autoplay bot could not select Boat7 on seed 11002 (`boat icon unavailable`) and stayed at the dock. Seed 11004 selected Boat7 and entered Endless Ocean but did not reach x≈152,600; the dedicated worldgen/sonar scene covered the deeper generated checkpoints.

## Round 2

- **Instance existence guards (`jmk_endless_ocean_sonar.gml`)**: Verified in the August 2023 legacy base that `SonarPanel` is placed as a non-persistent room instance in `NewWorld` (and `room11`, absent in `room2` and `ArcadeRoom`), and creates `SonarPanelAdvanced` in `SonarPanel_Create_0`. `jmk_endless_ocean_sonar.gml` now looks up both via `asset_get_index` and checks `instance_exists` before reading `advancedDrawEnabled` or `cave_sonar`, falling back to the basic sonar branch if either instance is missing.
- **No hardcoded asset indices (`jmk_endless_ocean_sonar.gml`)**: Replaced vanilla object and sprite IDs (`132`, `135`, `225`, `238`, `242`, `334`, `424`) with runtime `asset_get_index` lookups (`Abyssal`, `Dragon`, `Problematicus`, `spr_abyssal_head`, `spr_dragon_head`, `spr_problem_head`, `spr_sonarblip`) matching `SonarPanel_Step_1` semantics even when mods add or reorder assets.
- **Fish parity follow-ups (Endless Ocean generator only)**:
  - `jmk_endless_ocean_load.gml` now initializes `base_lunge_mod` and `base_lunge_offset` at fish creation using the exact `Rod_Other_10` upgrade and gear formula (defaulting to `1` and `0` when `Rod`/`Girl` are absent or uninitialized).
  - `jmk_endless_ocean_species.gml` no longer streams `Torby` (paired/extinction-tracked) or `Ancient_Kingfish` (singleton boss/save-tracked), substituting `Stankfin` and `Underfin` respectively.
  - `jmk_endless_ocean_dispose.gml` now clears `deactivated = 0` whenever it keeps a reactivated fish (`state == 4 || state == 15`, hooked on `Lure`, or held by `CatSwim`) so `Fish_Step_2` does not re-deactivate it after a `FishThinner` pass.
- **Verification**:
  - Updated `EndlessOceanTests` to shift asset indices past 500, assert `instance_exists` and `asset_get_index` usage, assert zero bare vanilla indices in decompiled GML and bytecode instructions, and verify `base_lunge_mod`, singleton exclusions, and `deactivated = 0` on preserved fish.
  - Updated `tests/worldgen/chunks.test.mjs` to stub the sonar initializer and assert `Torby`/`Ancient_Kingfish` exclusion (8/8 passing).
  - `dotnet build -c Release` (0 warnings, 0 errors) and `dotnet test -c Release` green (626 core and 130 GUI tests passed; 17 optional real-file tests skipped).
