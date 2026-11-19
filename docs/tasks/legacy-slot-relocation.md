# Task: keep BOTH items when legacy mods claim the same boat / upgrade slot (relocation)

## User goal (verbatim intent)
When two mods use the same ID (upgrade, boat, rod, fish, ...), one of them gets a new ID and ALL its references are moved
along, so both contents survive. If a mod puts a completely new thing on a vanilla ID it gets its own ID. It must be smart:
a mod may intentionally modify a vanilla item — that is a merge, not a relocation.
Fish are already handled: `CatalogMerge` + `CatalogRuntime` re-register legacy catalog fish by object identity
(`jamm_catalog_register`, "registered 69 mod fish by object identity"). Use that design as the precedent.
This task: **boats and legacy upgrades** (rods/lures/tools afterwards if the abstraction makes it cheap — optional).

## Real collisions (verified by decompiling the real mods, 2026-11-19)
Heavy pack `TGE + Antiquity + PWB` (TGE/PWB are private files, never commit their content or names of their assets into tests;
use synthetic archives in tests):

### Boat slot 8 — distinct identities
`gml_Object_BoatData_Create_0` vanilla has slots 0..7 (`unlocked[7] = -1`, `cost[i]`, `name[i]`, `object[i]`, `disp[i]`, `desc[i]`).
* TGE: `unlocked[7] = -2; unlocked[8] = -1; cost[8] = 10000000; name[8] = "Echo-9"; object[8] = <TGE boat object>; disp[8] = <sprite>; desc[8] = ...`
  plus TGE-only code `gml_Object_BoatData_Step_0`: `if (BoatData.unlocked[8] < 0) { BoatData.unlocked[8] = -1; ... }`.
* Antiquity: `unlocked[8] = -1; cost[8] = 9000000; name[8] = "Dark Boat"; object[8] = <Antiquity boat object>; disp[8] = <sprite>; desc[8] = ...`
* Identity: the boat **object asset** (and the name) differ → two different added boats. Today only one survives.

### Upgrade slot 18 — distinct names, shared lineage
`gml_Object_UpgradeData_Create_0` vanilla: `for (i = 0; i <= 17; i++)` init, `name/desc/desc2/cost[i]`, rod slot table `upg[rod, slot]`
(value = upgrade id, -1 empty, -2 locked), icons = frame `1 + upgrade_id` of sprite `spr_upgradeicons` (index 422; see
`UpgradeCard_Other_11`, `UpgradeSlot_Other_11`, `UpgradeData_Draw_0`).
* PWB: `name[18] = "Bulbguard"`, loop bound `<= 18`, refs: `Rod.upgrades[18]` (Bomb*/buy_lure), `UnlockUpgrade(18)` (Rod_Step_0),
  `case 18:` in `UpgradeCard_Other_10` and `Rod_Other_10`, `calculate_upgrade_cost`: `if (argument0 != 18)`, `UpgradeMenu_Alarm_0`: `if (iCard <= 18)`,
  `Rod_Other_10`: `upgrades[18] = 0/1`, loops `for (i = 0; i <= 18; i++)` in `Girl_Other_10`, SaveManager.
* TGE (derived from PWB!): `name[18] = "Zoom"`, also extends rod table to 7 rods / 9 slots, TGE-only refs: `BoatData_Step_0`
  `UpgradeData.unlocks[18]`/`UnlockUpgrade(18)`, `Camera_Step_0`: `UpgradeData.upg[Rod.rod_id, _s] == 18`. TGE ALSO contains the
  PWB-inherited hunks (`Rod.upgrades[18]` in Bomb code etc.) byte-identical to PWB.
* So the literal 18 is ambiguous even inside TGE. Rule to implement: **hunks that are textually identical in both claimants are shared
  lineage and keep the original (non-relocated) ID; only hunks unique to the relocated claimant are rewritten.** Pick as relocated
  claimant the mod whose claim is NOT the lineage origin: if mod A's typed refs to the slot are a strict superset of shared hunks
  plus unique ones and mod B's refs are all shared, relocate A. Otherwise relocate the later mod in load order. Document the choice
  in a build decision line.

## Required design
1. **Identity detection** (pure, testable): for each indexed legacy table (BoatData: key = `object[i]` asset name, fallback name;
   UpgradeData: key = `name[i]` string) find added slots (index absent from base, or base slot whose identity changed completely →
   "replacement of a vanilla item": treat as new item needing its own slot ONLY when the identity key differs from base AND the base
   item's object/name no longer exists anywhere in the mod's table; if only fields like cost/desc changed → modification → normal merge).
2. **Collision**: two mods claim the same slot with distinct identities → relocate one claimant to the next free slot
   (`max(all claimed slots) + 1`, deterministic order). Same identity in both → no relocation (normal merge/dedupe).
3. **Typed reference inventory + rewrite** (on the relocated mod's patch copy, BEFORE conflict analysis/plan, so the rest of the pipeline
   sees non-colliding slots). Rewrite the literal slot only in recognized typed sinks inside code that mod added/changed:
   * Boat: `BoatData.<field>[N]` (any field) and inside `gml_Object_BoatData_*`: `<field>[N]`; comparisons/assignments of boat index
     variables known from base code (`Girl.boat`, `boat_index`, `BoatCard item_id`) with literal N; `steam_set_achievement("boat_" + ...)` untouched.
   * Upgrade: `UpgradeData.<field>[N]`, inside `gml_Object_UpgradeData_*`: `<field>[N]` and `upg[N, …]` (row) only when N is an
     upgrade (row index of `upg` is a ROD id — do NOT rewrite rod indices); `UpgradeData.upg[a, b] == N` / `!= N` / `= N` (value is an upgrade id);
     `Rod.upgrades[N]` and inside `gml_Object_Rod_*`: `upgrades[N]`; `UnlockUpgrade(N)`, `upgrade_was_purchased(N)`;
     `calculate_upgrade_cost` `argument0 == N` / `!= N`; `case N:` only inside a `switch` whose subject is a proven upgrade-id expression
     (`card_id`, `UpgradeData.upg[...]`, the variable assigned from it) — check the real `UpgradeCard_Other_10` / `Rod_Other_10` shapes.
   * Count bounds (`for (i = 0; i <= N; i++)` over upgrade tables, `iCard <= N`, `nItems`): these are NOT identity refs; after relocation
     the merged bound must be the max over all claimants. Make the merge produce the max bound instead of a conflict (implement as a
     post-relocation normalization or a resolver rule; whichever fits the existing CodeMerge/BuildPlan best).
   * **Fail closed**: if the relocated mod's unique hunks contain the literal N in a context that looks upgrade/boat-typed but is not
     classified (dynamic index from a local initialized to N, `argument` passing, string building of save keys with N, etc.), do not
     relocate; keep today's behavior (group choice) and add a WARNING decision naming file + line. Unrelated literals (e.g.
     `lengthdir_x(18, …)`, `fish_limit = 8`) are not typed sinks and are left alone.
4. **Insertion merges**: after relocation, both mods insert index writes for different slots at the same place
   (`name[18] = ...` vs `name[19] = ...`). These pure-insertion hunks must union automatically (both kept, deterministic order) instead of
   conflicting. Check how `CatalogMerge` handles this for `CatalogData_Create_0` and reuse/generalize.
5. **Sprites**: boats use separate `disp` sprites (no atlas). Upgrade icons: append the relocated mod's frame `1+N` from its
   `spr_upgradeicons` as frame `1+N'` of the merged atlas, keeping all other frames from the selected atlas (union of appended frames).
   Look at `TryApplyHybridSpriteFrames` in `ArchiveBuilder` — today it keeps appended frames only from one source.
   If frame sizes/texture data cannot be merged safely, fall back to a default/blank icon frame and add a WARNING — never drop the item.
6. **Saves**: JAMM's items runtime already clamps legacy numeric save keys to vanilla counts and gives appended slots identity keys
   (see docs/modding/saves.md, ItemsRuntimeScripts). Make relocated slots go through the same identity mechanism (key e.g.
   `legacy:<modname>:<identity>`), so a relocated boat/upgrade survives save/load. Verify with a test on the generated GML.
7. **Reporting**: every relocation adds a decision, e.g.
   `[CGF-RELOCATE-001] Upgrade "Zoom" (TGE) moved from slot 18 to 19 (slot 18 kept by "Bulbguard" (PWB)); rewrote 7 typed references in 4 code entries; icon frame 19→20.`
   The conflict UI/`check` should no longer report a CONFLICT for a relocated group (INFO instead).

## Tests (required)
* Synthetic archives/patches only (see `CatalogArchiveTests`, `UpgradesRuntimeTests`, `BuildingTests` for builders):
  two boat claimants slot 8 → both present (slots 8 and 9), typed refs rewritten, unrelated literal 8 untouched;
  upgrade claimants with a shared identical hunk → shared hunk not rewritten, unique hunk rewritten; rod-index row not rewritten;
  count bound merged to max; unclassified typed use → no relocation + warning; same identity → no relocation; vanilla modification
  (only cost changed) → no relocation.
* Real-file test (opt-in, skipped unless `JAMM_ORIGINAL` + `JAMM_MODS_DIR` set; must not hardcode private file names — read an optional
  env var like `JAMM_RELOCATION_PACK=a.win,b.win` and assert only generic properties: build succeeds, every claimant identity present).

## Manual verification you may run (read-only inputs, write outputs under /tmp or the worktree's ignored dirs)
`dotnet run --project src/Jamm.Cli -c Release -- build --base ~/cgf-files/legacy/2023-08-16_manifest-9039707068725406600/data.win --order <TGE>,<Antiquity>,<PWB> --out /tmp/reloc-test.win`
with TGE/PWB at `~/cgf-files/private-vel/` and Antiquity at `~/cgf-files/mods/pack-inputs/Antiquity.win`. Print the decisions. Never copy
those files or their decompiled code into the repo.

## Rules
AGENTS.md applies. Zero warnings, `dotnet test -c Release` green. Do NOT commit or push. Keep classes small with XML docs.
If you conclude part of this is unsafe, implement the safe subset (boats first, then upgrades) and document precisely what is missing
in `docs/reports/legacy-slot-relocation.md`. Finish with a summary: files changed, tests, real-pack result (decision lines).
