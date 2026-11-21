# Fair-Play Audit Report

Audit of `ValueGuard` (`CGF-FAIR-*` and `CGF-VALUE-*` fish sale cap decisions) across all public mods built individually against the August 2023 legacy base (`2023-08-16_manifest-9039707068725406600/data.win`) using `cgfmm build --base <legacy> --order <mod.win> --out <tmp>/x.win`.

## Public Mod Audit Results

| Mod | Value | Decision | Verdict |
| --- | --- | --- | --- |
| `cave-expansion` | `Ancient_Rana XP_VALUE` = `10,000,000` (`elderana` line 15; `BASE_VALUE` = `9,000,000`) | `INFO [CGF-FAIR-002] possible debug value kept: Ancient_Rana XP_VALUE (cave-expansion, elderana line 15) = 10,000,000 (supported by the mod's other items)` | Intended balancing (correct: kept untouched) |
| `cave-expansion` | `Green_Cusper XP_VALUE` = `30,000,000` (`greencusper` line 11; `BASE_VALUE` = `3,858,000`) | `INFO [CGF-FAIR-002] possible debug value kept: Green_Cusper XP_VALUE (cave-expansion, greencusper line 11) = 30,000,000 (supported by the mod's other items)` | Intended balancing (correct: kept untouched) |
| `colour-expansion` | — | No `CGF-FAIR-*` or `CGF-VALUE-*` decisions | Clean (no changes) |
| `the-abyss-beta` | — | No `CGF-FAIR-*` or `CGF-VALUE-*` decisions | Clean (no changes) |
| `pack-inputs/Abyss` | — | No `CGF-FAIR-*` or `CGF-VALUE-*` decisions | Clean (no changes) |
| `pack-inputs/Antiquity` | — | No `CGF-FAIR-*` or `CGF-VALUE-*` decisions | Clean (no changes) |
| `pack-inputs/Colour` | — | No `CGF-FAIR-*` or `CGF-VALUE-*` decisions | Clean (no changes) |
| `pack-inputs/Verdant` | — | No `CGF-FAIR-*` or `CGF-VALUE-*` decisions | Clean (no changes) |

Zero false positives were observed across the audited public mods: no intended balancing value was clamped or altered. `cave-expansion`'s two high-tier fish XP values (`10,000,000` and `30,000,000`) exceed `10×` the vanilla maximum (`500,000`, headroom `5,000,000`), so Fair-Play logged informational `CGF-FAIR-002` notes and preserved both values because they sit within the `5×` peer progression window (`10M * 5 = 50M >= 30M`) and are below `1e9`.

## Acceptance Warning Investigation: Gear Event Iterates 18 Entries vs. 19-Entry Upgrade Table

When a merged archive has 19 entries in `UpgradeData` (slots `0..18`), `PackAcceptance.CheckUpgradeExternalLoopBounds` (`src/Jamm.Core/Acceptance/PackAcceptance.cs`) emits:

```text
TABLE-LENGTH (WARNING) UpgradeData/gml_Object_Gear_Other_15: UpgradeData has 19 entries, but gml_Object_Gear_Other_15 still loops to a hardcoded bound of 18.
```

### Cause

1. **Vanilla baseline structure**: In the legacy 2023 `data.win`, `UpgradeData_Create_0` defines 18 upgrades (indices `0..17`). Three external loops iterate over upgrade indices with hardcoded `18`-entry bounds (`i <= 17` or `i < 18`):
   - `gml_Object_UpgradeMenu_Alarm_0` (`iCard`)
   - `gml_Object_Girl_Other_10` (`gear_upgrade_scalar[i] = 1` in the `gear < 0` branch, and `gear_upgrade_scalar[i] = gear.upgrade_scalar[i]` in the `else` branch)
   - `gml_Object_Gear_Other_15` (`upgrade_scalar[i] = 1`)
2. **Legacy mod edits leave `Gear_Other_15` at 18**: Legacy mods that append a 19th upgrade (index `18`) in `UpgradeData_Create_0` typically update `UpgradeMenu_Alarm_0` and the `gear < 0` reset loop in `Girl_Other_10` to `<= 18`, but leave `gml_Object_Gear_Other_15` untouched at `<= 17` (18 entries) — and likewise leave `Girl_Other_10`'s `else` copy loop (`gear_upgrade_scalar[i] = gear.upgrade_scalar[i]`) at `<= 17` so equipping a hat does not read past `gear.upgrade_scalar[17]`.
3. **Why JAMM does not rewrite `Gear_Other_15` in non-Items-API builds**:
   - `ItemsRuntimePatches.PatchGearStats` (`src/Jamm.Core/Building/ItemsRuntimePatches.cs`) *does* rewrite `gml_Object_Gear_Other_15` (and `PatchGirlGearRefresh` rewrites both loops in `gml_Object_Girl_Other_10` with bounds-checked `array_length_1d(gear.upgrade_scalar)`) to iterate `array_length_1d(UpgradeData.unlocks)`. However, `ItemsRuntime.PreparePlan` (`src/Jamm.Core/Building/ItemsRuntime.cs`) only activates when the build contains an Items API registration call (`jamm_boat_register`, `jamm_rod_register`, `jamm_lure_register`, `jamm_tool_register`, `jamm_hat_register`, or `jamm_upgrade_register`). Legacy-only builds and packs that only use fish/area APIs skip `ItemsRuntimePatches`.
   - `LegacyUpgradeRelocation.NormalizeUpgradeBounds` (`src/Jamm.Core/Building/LegacyUpgradeRelocation.cs`) only runs when an upgrade slot collision triggered relocation (`anyRelocated == true`), and even then `RewriteUpgradeCountBounds` only edits `changedLines` (lines the mod itself changed relative to base), so an untouched `Gear_Other_15` stays at `18`.
4. **Runtime effect**: While both `Gear_Other_15` and `Girl_Other_10`'s `gear >= 0` copy loop stop at `18` (`0..17`), equipping a hat does not crash, and slot `18` does not receive `upgrade_scalar` initialization on `Gear`. Any mod that bumps `Girl_Other_10`'s `else` loop to `19` without also updating `Gear_Other_15` would trigger an out-of-bounds read on `gear.upgrade_scalar[18]` when wearing a hat.

### Options

1. **Keep the warning as-is (status quo)**: Benign when hats do not touch upgrade slot `18` and `Girl_Other_10`'s copy loop also stays at `18`; surfaces the loop mismatch clearly in acceptance reports.
2. **Run dynamic `Gear_Other_15` / `Girl_Other_10` upgrade-scalar loop patching whenever `UpgradeData` grows past 18**: Reuse the regex patches in `ItemsRuntimePatches.PatchGearStats` and `PatchGirlGearRefresh` (`array_length_1d(UpgradeData.unlocks)` with `array_length_1d(gear.upgrade_scalar)` bounds check) whenever `inputs.Count > 0` and `UpgradeData` has `> 18` entries, independent of Items API injection. Vanilla-only builds (`inputs.Count == 0`) remain byte-identical.
3. **Extend `LegacyUpgradeRelocation.NormalizeUpgradeBounds`**: Allow `NormalizeUpgradeBounds` to also bump `Gear_Other_15` (and `Girl_Other_10` with bounds safety) when any mod expands `UpgradeData` past 18 slots, even without a relocation collision or mod-changed lines in `Gear_Other_15`.
4. **Refine `PackAcceptance.CheckUpgradeExternalLoopBounds`**: Differentiate whether `Girl_Other_10`'s `gear >= 0` copy loop exceeds `Gear_Other_15`'s bound (crash risk) vs. both stopping at `18` (missing scalar init for slot `18+` only).

## Verification

- Added synthetic borderline and peer-supported coverage in `tests/Jamm.Core.Tests/ValueGuardTests.cs` (`BorderlineAndPeerSupportedValuesStayUntouched`), covering:
  - Fish sale value at the exact `100,000,000` cap (`BASE_VALUE = 100000000`).
  - Boat speed at the `10×` vanilla headroom boundary (`move_speed = 100` vs vanilla `10`).
  - `XP_VALUE` at the `10×` vanilla headroom boundary (`5,000,000` vs vanilla `500,000`) and peer-supported XP values (`10,000,000` and `30,000,000`).
  - Lure sink speed just below `TrivialSpeed` (`9,999 < 10,000`).
  - Rod stats at `10×` headroom (`rod_power = 30`, `reel_speed = 150`), normal progression (`cast_speed = 18`), paid minimum (`cost[0] = 1`), and free slot (`cost[1] = 0`).
- `dotnet build -c Release`: 0 warnings, 0 errors.
- `dotnet test -c Release`: green.
- Vanilla-only build (`cgfmm build --base <legacy> --out <tmp>/vanilla.win`) verified byte-identical to the legacy base archive.
