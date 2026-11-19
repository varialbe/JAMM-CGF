# Task: relocate colliding LEGACY UPGRADE slots (keep both upgrades)

Read first: `docs/tasks/legacy-slot-relocation.md` (full background, real TGE/PWB facts in section "Upgrade slot 18") and
`docs/reports/legacy-slot-relocation.md` (what the boat part does and what is missing). The boat relocation is implemented in
`src/Jamm.Core/Building/LegacySlotRelocation.cs` (runs before CatalogMerge/conflict analysis; `SlotUnion` unions disjoint slot
writes for BoatData). Extend the same design to `UpgradeData` (keep the code readable: separate class `LegacyUpgradeRelocation`
or a table-descriptor abstraction shared with boats).

## Must do
1. Identity = `name[N]` string in `gml_Object_UpgradeData_Create_0`; added slot N (absent from base). Distinct names on the same
   N across mods → relocate one claimant to `max(claimed)+1`.
2. Choose the relocated claimant by lineage: hunks byte-identical in both claimants are SHARED and keep N (never rewrite them).
   Relocate the claimant that has unique (non-shared) typed references; if both have unique ones, relocate the later in load order.
3. Rewrite only in the relocated mod's unique added/changed code, only typed sinks (list in legacy-slot-relocation.md §3 Upgrade):
   `UpgradeData.<field>[N]`, local `<field>[N]` inside `gml_Object_UpgradeData_*` except `upg[ROW, …]` rows (rod ids!),
   `UpgradeData.upg[a,b] ==/!=/= N`, `Rod.upgrades[N]` / `upgrades[N]` inside `gml_Object_Rod_*`, `UnlockUpgrade(N)`,
   `upgrade_was_purchased(N)`, `case N:` only inside switches on a proven upgrade-id subject (check real shapes of
   `UpgradeCard_Other_10`, `Rod_Other_10`: `switch (card_id)` / similar), `argument0 ==/!= N` inside `calculate_upgrade_cost`.
4. Count bounds `for (i = 0; i <= N; i++)`, `iCard <= N` in upgrade contexts: merged result must use the max bound
   (extend `SlotUnion`-style resolution or normalize the relocated mod's bound to the new max before merging).
5. Icon atlas: icon = frame `1+id` of `spr_upgradeicons`. Append the relocated mod's frame `1+N` as frame `1+N'` in the merged atlas
   (see `TryApplyHybridSpriteFrames` in ArchiveBuilder). If impossible, keep the item with a blank/duplicated icon frame + WARNING.
6. Fail closed with a WARNING (no relocation, previous group-choice behavior) on unclassified typed uses.
7. Decision `[CGF-RELOCATE-003] Upgrade "<name>" (<mod>) moved from slot N to N'; rewrote K typed references in M code entries; icon frame …`.

## Tests
Synthetic only (see `LegacySlotRelocationTests`, `UpgradesRuntimeTests`): shared identical hunk not rewritten; unique hunk rewritten;
rod-row index untouched; bound merged to max; `case` rewritten only in upgrade switch; unclassified use → warning; same name → no-op.

## Real check (local only, never commit private names/content)
`dotnet run --project src/Jamm.Cli -c Release -- build --base ~/cgf-files/legacy/2023-08-16_manifest-9039707068725406600/data.win --order ~/cgf-files/private-vel/TGE.win,~/cgf-files/mods/pack-inputs/Antiquity.win,~/cgf-files/private-vel/PWB.win --out /tmp/upg.win`
(several minutes). Report the decision lines.

## Rules
AGENTS.md. Zero warnings, `dotnet test -c Release` green. No commit/push. Work only in this worktree and /tmp. Summarize at the end.
