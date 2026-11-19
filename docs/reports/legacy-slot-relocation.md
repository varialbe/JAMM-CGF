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

## Effect-slot relocation (CGF-RELOCATE-004) — added 2026-11-19

Effect slots `Rod.upgrades[K]` with K > 21 that the relocated claimant and another claimant both use in their unique hunks are moved
for the relocated claimant. Real heavy pack: `Effect slot Rod.upgrades[22] of "Bulbguard" (PWB) moved to 23 (also used by "Zoom" (TGE))`.

**Remaining lineage gap:** the byte-identical (shared) `Rod_Step_0` hunk in TGE and PWB reads
`if (CatalogData.count[1, 42] > 0 && bulbguardUnlocked == 0) { if (upgrades[22] < 1) { UnlockUpgrade(18); … } }`. Because shared
hunks keep the original id, this now unlocks Zoom (18), which matches TGE's own behavior, but Bulbguard (19) is no longer unlocked by
that trigger. Needs an in-game check whether Bulbguard is still obtainable; possible fix: emit an extra unlock for the relocated id when
the only unlock site of an upgrade is a shared hunk.

## Lure and tool slot relocation (CGF-RELOCATE-001) — added 2026-11-19

Generalized `LegacySlotRelocation` via `TableDescriptor` across `BoatData`, `LureData`, and `ToolData`:

- **Identity:** `object[i]` when present (`BoatData`), otherwise `name[i]` (`LureData`, `ToolData`). Using `name[i]` for `LureData` avoids colliding on shared item objects (e.g. `cave-expansion` sets `item[6] = BigHook2`, the same object as vanilla `item[4]` "Mega Hook", with `name[6] = "Giga Hook"`). `PackAcceptance` also uses `["object"]` then `["name"]` for `LureData` and `ToolData`.
- **Typed sinks (rewritten only in the relocated claimant's added/changed lines):**
  - Boats: `BoatData.<field>[N]`, bare `<field>[N]` in `BoatData` events / `with (BoatData)`, `boat_was_unlocked(N)`, `boat`/`boat_index`, and `item_id` in `BoatData`/`BoatCard`/`BoatMenu` scope.
  - Lures: `LureData.<field>[N]`, bare `<field>[N]` in `LureData` events / `with (LureData)`, `gear_lure_scalar[N]` and `lure_scalar[N]`, `buy_lure(N)`, `lure_was_purchased(N)`, `lure_index`/`active_lure`, `LureQuest.purchased`/`type`, and `item_id` in `LureData`/`LureCard`/`LureMenu` scope.
  - Tools: `ToolData.<field>[N]`, bare `<field>[N]` in `ToolData` events / `with (ToolData)`, `tool_was_purchased(N)`, `tool_index`/`active_tool`, and `item_id` in `ToolData`/`ToolCard`/`ToolMenu` scope.
- **Unclassified literal guard & table unions:** Any remaining literal `N` on an added/changed line within +/-5 tokens of `"boat"` / `"lure"` / `"tool"` fails closed with a `WARNING` (`boat-slot` / `lure-slot` / `tool-slot`) and skips relocating that claimant. `SlotUnion` handles `gml_Object_BoatData_*`, `gml_Object_LureData_*`, and `gml_Object_ToolData_*` (disjoint slot writes, list insertions, and max `nItems = <int>` assignments).
- **`ToolData_Create_0` placeholders & `nItems`:** Vanilla `ToolData_Create_0` defines `nItems = 3;` plus placeholder lines `unlocked[3] = -1; unlocked[4] = -1;`. When a relocated tool modified an `unlocked[oldSlot]` placeholder in-place, relocation restores `unlocked[oldSlot]` to its base line, places `unlocked[newSlot]` into `unlocked[newSlot]`'s placeholder (or after the `unlocked` block), and bumps literal `nItems` to at least `newSlot + 1`.

## Why rod slot relocation (`RodData`) is deferred

CGF-RELOCATE-006 now addresses the dependencies listed below. Vanilla identifies rods by `name[]` and initializes each `cost_base[id]` from `RodData.cost[id]`. Relocation preserves a claimant's explicit upgrade costs and uses the vanilla formula with that rod's own shop price when a value is missing. Missing Rocket and distance/depth quest values inherit the nearest preceding configured rod's value.

The original investigation inspected vanilla `RodData_Create_0`, `UpgradeData_Create_0`, `SaveManager_Other_13..15`, `Rod_Other_10`, `RodCard_Other_11`, `Rocket_Step_2`, `start_quest_dist`, and real mods (`TGE.win`, `cave-expansion.win`).

1. **2D `UpgradeData.upg[rod, slot]` and `UpgradeData.object[rod, slot]` rows:** Every rod id indexes a row in `UpgradeData_Create_0` (vanilla initializes `0..5 x 0..7`). A relocated rod `rod'` needs row `upg[rod', ...]` and `object[rod', ...]` initialized, yet real mods are asymmetric: `TGE` adds `Proto-Rod` at slot 6 and edits `UpgradeData_Create_0` (`upg[6, 0..8] = -1`, `for (ix = 0; ix < 7; ix++)`), whereas `cave-expansion` adds `Mystic Rod` at slot 6 without touching `UpgradeData_Create_0` at all (so no `upg[6, ...]` row exists in `cave-expansion` to move to row 7).
2. **Hardcoded `SaveManager` 2D loop bounds:** `SaveManager_Other_13/14/15` loop `with (UpgradeData)` with literal `for (ix = 0; ix < 6; ix++)` (`< 7` in `TGE`), which must also be raised to `rod' + 1` or upgrade slots on rod `>= 7` are lost on save/load.
3. **`spr_rod` atlas frame indexing:** Vanilla `RodData` has no `disp[]` array (`RodCard_Other_11` and `Rod_Other_10` index `spr_rod` directly by `item_id`/`rod_id`). Both `TGE` and `cave-expansion` modify `spr_rod`, so rod relocation also requires `spr_rod` frame relocation (like `spr_upgradeicons` in `LegacyUpgradeRelocation`) or injecting `disp[]` patching.
4. **Consumer bounds and `Create_0` quirks:** `Rocket_Step_2` (`target_distance[0..5]`), `start_quest_dist` (`dist[0..5]`, `dpth[0..5]`), and `UpgradeData_Create_0` (`cost_base[0..5]`) assume vanilla rod counts; `RodData_Create_0` also uses hardcoded `nItems = 6` plus placeholder `unlocked[6..7] = -2` (`TGE` keeps `-2` until quest unlock and clamps `unlocked[6] < -1` in `SaveManager_Other_13`; `cave-expansion` leaves `nItems = 6` unchanged and edits vanilla slot 5).
