# Legacy slot relocation implementation report

## Implemented

- Boat additions are identified from `BoatData_Create_0` by object asset name, with display name retained for reporting. A slot whose identity differs from the base is treated as a replacement only if the old identity is absent elsewhere in that mod's table.
- When multiple mods claim one slot with different identities, the first claimant keeps it and later claimants are moved to the next slot after the highest base or claimed slot.
- The relocation runs before both conflict analysis and build planning. Recognized `BoatData.field[N]` uses, bare array uses in `BoatData` events, and known boat selection variables are rewritten in the relocated patch's added or changed GML. A nearby unclassified boat-typed numeric use causes a warning and cancels that claimant's relocation.
- Pure insertion hunks in `BoatData` events are unioned in deterministic load order. Build decisions include a `[CGF-RELOCATE-001]` line.
- Synthetic tests cover distinct boat identities, typed reference rewriting, unrelated literals and strings, insertion union, same identity, and a vanilla field edit.

## Not implemented

- Legacy upgrade relocation remains unchanged. The current work does not classify typed upgrade references, distinguish shared lineage hunks, choose a lineage origin, normalize count bounds, merge upgrade icon atlas frames, or verify rewritten upgrade save identities. The PWB/TGE example has shared inherited code and `case` labels, so a numeric rewrite based only on literal proximity could move behavior belonging to both upgrades.
- Boat identity fallback to `name[i]` when `object[i]` is unavailable is not implemented. Boat save identity generation is not changed; this implementation relies on existing appended-slot save handling.
- Relocation is not attempted when the relocated claimant has no recognized typed reference in its changed or added code. The existing conflict choice remains available in that case.
- No real archive pack was built. The opt-in real-file test and the private manual pack were not run, so there are no real-pack decision lines to report.

These are deliberate safety limits: the implementation relocates boats only when its current code evidence supports the rewrite. Upgrade relocation should be completed with a typed-reference inventory and validation against synthetic and opt-in archives before it is enabled.

## Upgrade relocation (CGF-RELOCATE-003) — added 2026-11-19

Implemented in `LegacyUpgradeRelocation`. Typed sinks: `UpgradeData.<field>[N]` (not rod-indexed `upg`/`object`/`cost_base` rows),
`UpgradeData.upg[a,b] ==/!=/= N`, `UnlockUpgrade(N)`, `upgrade_was_purchased(N)`, `case N:` in upgrade-id switches,
`card_id ==/!= N`, `argument0 ==/!= N` in `calculate_upgrade_cost`; count bounds merge to the max; icon frame `1+N` is appended as
`1+N'` in `spr_upgradeicons`. Byte-identical (shared lineage) hunks are never rewritten.

**Correction by the orchestrator:** `Rod.upgrades[]` is an *effect-slot* array, not indexed by upgrade id (vanilla `Rod_Other_10`
maps id 14 → `upgrades[18]`, 15 → `upgrades[19]`, …). The first version rewrote `upgrades[18]` and broke vanilla upgrade 14;
effect slots are now left alone and only the `case N:` label moves.

Real heavy pack (TGE, Antiquity, PWB): `Upgrade "Bulbguard" (PWB) moved from slot 18 to 19; rewrote 6 typed references in 3 code
entries; icon frame 19->20`; vanilla `case 14` intact.

**Known gap — effect-slot collisions:** TGE's Zoom purchase code sets `Rod.upgrades[22] = 1`, and PWB maps Bulbguard to effect slot 22
too. After relocation both upgrades exist, but buying Zoom also switches on Bulbguard's effect until the rod is recalculated. Next step:
detect effect slots ≥ the vanilla maximum (21) that two claimants use with different meanings and relocate the slot of the moved
claimant (all `Rod.upgrades[K]` uses in its unique hunks, plus `array_length_1d(Rod.upgrades) > K` guards).
