# Task: upgrade-scalar loop bounds when UpgradeData grows past 18 (branch `exp/upgrade-loop-bounds`)

Background: `docs/reports/fair-play-audit.md`, section "Acceptance Warning Investigation". Legacy mods that add a 19th
upgrade leave `gml_Object_Gear_Other_15` (and the `else` copy loop in `gml_Object_Girl_Other_10`) at a hardcoded 18.

## Do (option 2 + 4 of that report)
1. In final modded builds (never in vanilla-only builds, which must stay byte-identical), when the merged
   `UpgradeData` has more than 18 entries and the Items runtime did not already patch these entries, apply the same
   bounded loop rewrites as `ItemsRuntimePatches.PatchGearStats` / `PatchGirlGearRefresh`
   (`array_length_1d(UpgradeData.unlocks)`, reads bounded by `array_length_1d(gear.upgrade_scalar)`). Reuse that code,
   do not duplicate it. Add a decision `[CGF-UPGRADE-LOOP-001]` describing what was rewritten.
2. Refine `PackAcceptance.CheckUpgradeExternalLoopBounds`: no warning when the build repaired the loops; distinguish
   crash risk (copy loop longer than Gear loop) from missing scalar init.
3. Unit tests with synthetic data (no game files): 19-entry table gets rewritten loops; 18-entry table unchanged;
   Items-API build not double-patched; vanilla-only byte-identical.
4. Verify with a real build of `~/cgf-files/mods/pack-inputs/*.win` on the legacy base
   (`~/cgf-files/legacy/2023-08-16_manifest-9039707068725406600/data.win`); outputs only under `.audit/`, delete
   the .win files afterwards. Short report `docs/reports/upgrade-loop-bounds.md`.
5. `dotnet build -c Release` zero warnings, `dotnet test` green.

## Rules
AGENTS.md applies. Simple single commands (no `&&`, pipes, redirections). Do not commit; the orchestrator commits.
