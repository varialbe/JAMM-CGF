# Upgrade-Scalar Loop Bounds (`UpgradeData` > 18 Entries)

Branch `exp/upgrade-loop-bounds`.

## Findings
- In the 2023-08-16 legacy base (`UpgradeData_Create_0`), `UpgradeData` defines 18 entries (`0..17`). Three external loops iterate over upgrade indices with hardcoded 18-entry bounds: `gml_Object_UpgradeMenu_Alarm_0` (`iCard`), `gml_Object_Girl_Other_10` (`gear_upgrade_scalar` reset in `gear < 0` and `gear.upgrade_scalar` copy in `else`), and `gml_Object_Gear_Other_15` (`upgrade_scalar` reset).
- `MenuRuntime.PreparePlan` already rewrites `gml_Object_UpgradeMenu_Alarm_0` to iterate `array_length_1d(UpgradeData.unlocks)` in all final modded builds.
- `ItemsRuntimePatches.PatchGirlGearRefresh` and `PatchGearStats` rewrite the `gear_upgrade_scalar` / `upgrade_scalar` loops in `gml_Object_Girl_Other_10` and `gml_Object_Gear_Other_15` to `array_length_1d(UpgradeData.unlocks)` (with `array_length_1d(gear.upgrade_scalar)` bounds checks on the copy loop), but only ran when Items API registrations were present.
- `PackAcceptance.CheckUpgradeExternalLoopBounds` previously checked only the first literal `for` loop bound per entry and did not distinguish an unguarded `Girl_Other_10` hat-copy loop exceeding `Gear_Other_15`'s bound (out-of-bounds read crash risk) from a matched short bound (missing scalar init only).

## Changes
1. **Shared upgrade-scalar loop rewrites (`ItemsRuntimePatches` + `UpgradeLoopBounds`)**:
   - Extracted `ItemsRuntimePatches.PatchGirlUpgradeScalarLoops` and `ItemsRuntimePatches.PatchGearUpgradeScalarLoop` so both `ItemsRuntimePatches` and non-Items-API builds reuse the exact same regexes and `array_length_1d(UpgradeData.unlocks)` / `array_length_1d(gear.upgrade_scalar)` replacements without duplication.
   - Added `src/Jamm.Core/Building/UpgradeLoopBounds.cs` and wired `UpgradeLoopBounds.PreparePlan` into `ArchiveBuilder.BuildLayer` for final modded builds (`!intermediate` and `inputs.Count > 0`, leaving vanilla-only builds byte-identical).
   - When the merged `gml_Object_UpgradeData_Create_0` has more than 18 entries and `gml_Object_Girl_Other_10` / `gml_Object_Gear_Other_15` were not already patched (for example by `ItemsRuntimePatches`), `UpgradeLoopBounds.PreparePlan` rewrites any matching hardcoded `gear_upgrade_scalar` / `upgrade_scalar` loops and records decision `[CGF-UPGRADE-LOOP-001]`.
2. **Acceptance diagnostics (`PackAcceptance.CheckUpgradeExternalLoopBounds`)**:
   - Inspects `gml_Object_UpgradeMenu_Alarm_0` (`iCard` literal bound), `gml_Object_Girl_Other_10` (reset loop vs. `gear.upgrade_scalar` copy loop and `array_length_1d(gear.upgrade_scalar)` guard), and `gml_Object_Gear_Other_15` (`upgrade_scalar` reset loop) by matching loop bodies.
   - Emits no `TABLE-LENGTH` warnings when the loops have been repaired to dynamic bounds.
   - Distinguishes `Crash risk:` (unguarded `Girl_Other_10` copy loop reading past `Gear_Other_15`'s bound / `UpgradeData` length) from `Missing scalar init:` (short reset or bounded copy loop with no out-of-bounds read).
3. **Unit tests (`tests/Jamm.Core.Tests/UpgradeLoopBoundsTests.cs`)**:
   - Added 5 synthetic tests covering: 19-entry legacy table loop rewrites (`[CGF-UPGRADE-LOOP-001]`, no `TABLE-LENGTH` risk, `lure_scalar` untouched), 18-entry table unchanged, Items-API 19-entry build not double-patched, vanilla-only byte-identical copy on a 19-entry base, and `PackAcceptance` crash-risk vs. missing-scalar-init vs. repaired-loop classification.

## Verification
- `dotnet build -c Release`: succeeded with 0 warnings, 0 errors.
- `dotnet test -c Release --no-build`: all non-real-file tests green (`Jamm.Core.Tests`: 579 passed, 0 failed, 16 skipped; `Jamm.Gui.Tests`: 130 passed, 0 failed, 1 skipped — skipped tests are `[RealFilesFact]` requiring `JAMM_ORIGINAL` / `JAMM_MODS_DIR`).
- Real build of `~/cgf-files/mods/pack-inputs/*.win` (`Abyss.win`, `Antiquity.win`, `Colour.win`, `Verdant.win`) on `~/cgf-files/legacy/2023-08-16_manifest-9039707068725406600/data.win` into `.audit/pack.win`: built and verified (`86,671,218` bytes, 44 decisions, `risks: []`, `[CGF-UPGRADE-LOOP-001]` did not trigger because none of the four `pack-inputs` mods expand `UpgradeData` past 18 entries).
