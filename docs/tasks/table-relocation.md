# Task: extend legacy slot relocation from boats to lures, rods and tools

Read `docs/reports/legacy-slot-relocation.md` and `src/Jamm.Core/Building/LegacySlotRelocation.cs` (boats: CGF-RELOCATE-001,
`SlotUnion` for disjoint slot writes). Generalize it with a small table descriptor (create entry, identity field, qualified
prefix, local event prefix, known index variables) instead of copying code.

## Real collision (verified 2026-11-19, all-7 pack)
`gml_Object_LureData_Create_0`: vanilla slots 0..5 (`name[5] = "Trawler"`). TGE adds `name[6] = "Lantern"`, cave-expansion adds
`name[6] = "Giga Hook"` → today one is lost (acceptance: "TGE lost: lure Lantern"). Check the vanilla LureData/RodData/ToolData
Create code (decompile via `dotnet run --project src/Jamm.Cli -c Release -- diff <base> <mod> --show gml_Object_LureData_Create_0`)
to find the identity field (`object[i]` if present, else `name[i]`), the count field (`nItems` or array_length) and typed
references (e.g. `LureData.<field>[N]`, `Girl.lure`/`active_lure`-style variables, `buy_lure(N)` — verify actual names in vanilla code).
Rods: careful — `UpgradeData.upg[rod, slot]` rows are indexed by rod id; a relocated rod needs its `upg[rod', …]` row too.
If rods turn out to be too entangled, implement lures + tools and document rods precisely.

## Do
* Relocate later claimant to next free slot for distinct identities; rewrite only typed sinks in its own added/changed code; fail closed
  with a warning on unclassified uses; union disjoint slot writes in the table's events (extend `SlotUnion`).
* Decisions `[CGF-RELOCATE-001] Lure "Giga Hook" (cave-expansion) moved from slot 6 to 7; rewrote N typed references.`
  (reuse 001 with the item kind in the text).
* Pack acceptance (`src/Jamm.Core/Acceptance/PackAcceptance.cs`) should then show the lure as Relocated/Present.

## Tests
Synthetic (extend `LegacySlotRelocationTests`): two lures on one slot → both survive; typed refs rewritten; unrelated literal untouched;
same identity → no-op; tools similarly; rods per what you implement.

## Real check (local only; several minutes; use $HOME not ~ after commas)
`dotnet run --project src/Jamm.Cli -c Release -- build --base ~/cgf-files/legacy/2023-08-16_manifest-9039707068725406600/data.win --order $HOME/cgf-files/private-vel/TGE.win,$HOME/cgf-files/mods/cave-expansion.win --out /tmp/lure.win`
Report decision lines and the acceptance summary.

## Rules
AGENTS.md. Zero warnings, `dotnet test -c Release` green. No commit/push. Only this worktree + /tmp. Summarize at the end.
