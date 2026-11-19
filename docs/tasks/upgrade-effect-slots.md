# Task: relocate colliding Rod.upgrades[] EFFECT slots after upgrade relocation

Read `docs/reports/legacy-slot-relocation.md` (section "Upgrade relocation" and "Known gap — effect-slot collisions") and
`src/Jamm.Core/Building/LegacyUpgradeRelocation.cs`.

Facts: `Rod.upgrades[]` is an effect-slot array. Vanilla `Rod_Other_10` maps upgrade ids to slots inside
`switch (UpgradeData.upg[rod_id, i])` (`case 14: upgrades[18] = 1;` … `case 17: upgrades[21] = 1;`); vanilla max slot = 21.
Real heavy pack: PWB adds `case 18: upgrades[22] = 1;` (Bulbguard, relocated to id 19 by CGF-RELOCATE-003), while TGE's
`UpgradeCard_Other_10` (Zoom, id 18) uses `Rod.upgrades[22]` (`if (array_length_1d(Rod.upgrades) > 22 && Rod.upgrades[22])`,
`Rod.upgrades[22] = 1`). Both mods use effect slot 22 for different upgrades.

## Do
When CGF-RELOCATE-003 relocates a claimant, also detect effect slots K > 21 used in the relocated claimant's UNIQUE hunks
(`Rod.upgrades[K]`, local `upgrades[K]` in `gml_Object_Rod_*`, `array_length_1d(Rod.upgrades) > K` / `>= K+1` guards) that the other
claimant also uses in ITS unique hunks. If so, move the relocated claimant's slot to the next free slot (max used slot + 1 across all
mods) in all its unique hunks, and add `Rod.upgrades[K'] = 0;` initialization next to the vanilla init block if the claimant initializes
its slot there (union like the other slot writes). Shared (byte-identical) hunks stay untouched. Fail closed (warning, no slot move) on
dynamic indices near K. Decision: `[CGF-RELOCATE-004] Effect slot Rod.upgrades[22] of "Bulbguard" (PWB) moved to 23 (also used by "Zoom" (TGE)); rewrote N references.`

## Tests
Synthetic (extend `LegacyUpgradeRelocationTests`): collision → moved; no collision → untouched; vanilla slots ≤21 never moved; shared hunk untouched; guard expression rewritten.

## Real check (local only, never commit private names/content)
`dotnet run --project src/Jamm.Cli -c Release -- build --base ~/cgf-files/legacy/2023-08-16_manifest-9039707068725406600/data.win --order $HOME/cgf-files/private-vel/TGE.win,$HOME/cgf-files/mods/pack-inputs/Antiquity.win,$HOME/cgf-files/private-vel/PWB.win --out /tmp/eff.win`
(several minutes; use $HOME, not ~ after commas). Report decision lines.

## Rules
AGENTS.md. Zero warnings, `dotnet test -c Release` green. No commit/push. Only this worktree + /tmp. Summarize at the end.
