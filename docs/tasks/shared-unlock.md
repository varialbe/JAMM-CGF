# Task: keep relocated upgrades obtainable when their unlock lives in a shared lineage hunk

Read `docs/reports/legacy-slot-relocation.md` (sections CGF-RELOCATE-003/004, "Remaining lineage gap") and
`src/Jamm.Core/Building/LegacyUpgradeRelocation.cs`.

Problem: TGE derives from PWB. The byte-identical (shared) `gml_Object_Rod_Step_0` hunk contains `UnlockUpgrade(18);`. After
CGF-RELOCATE-003 moves PWB's "Bulbguard" 18 → 19, shared hunks keep 18, so the trigger unlocks only "Zoom" (TGE, 18) and Bulbguard
can no longer be unlocked by it.

## Do
For every relocation N → N' (CGF-RELOCATE-003): in shared hunks (identical in the relocated claimant and the kept claimant) that the
merged result contains, rewrite each statement `UnlockUpgrade(N);` to `UnlockUpgrade(N); UnlockUpgrade(N');` (same for
`upgrade_was_purchased(N)` only if it is an unlock-type call — check vanilla semantics first; if unsure, only `UnlockUpgrade`).
Only these unlock calls — no other sinks in shared hunks. Implement it so the merged code contains the duplicate call exactly once
(idempotent), and the semantic reference verification still passes. Add a decision line
`[CGF-RELOCATE-005] Shared unlock of upgrade N in <code entry> also unlocks relocated "<name>" (N').`
Make sure `UnlockUpgrade` with an already-unlocked id is harmless (read vanilla `gml_Script_UnlockUpgrade` via
`dotnet run --project src/Jamm.Cli -c Release -- diff <base> <mod> --show gml_Script_UnlockUpgrade` or decompile output) — if it shows a
message/popup each time, guard the extra call with `if (!UpgradeData.unlocks[N'])`.

## Tests
Synthetic, extend `LegacyUpgradeRelocationTests`: shared hunk unlock duplicated once; unique hunks unaffected; no relocation → no change.

## Real check (local only)
`dotnet run --project src/Jamm.Cli -c Release -- build --base ~/cgf-files/legacy/2023-08-16_manifest-9039707068725406600/data.win --order $HOME/cgf-files/private-vel/TGE.win,$HOME/cgf-files/mods/pack-inputs/Antiquity.win,$HOME/cgf-files/private-vel/PWB.win --out /tmp/unlock.win`
Report decision lines.

## Rules
AGENTS.md. Zero warnings, tests green. No commit/push. Only this worktree + /tmp.
