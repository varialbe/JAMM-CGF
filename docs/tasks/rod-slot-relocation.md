# Task: relocate colliding legacy rods (CGF-RELOCATE-006)

Context: `docs/reports/legacy-slot-relocation.md` section "Why rod slot relocation (`RodData`) is deferred" and
`docs/reports/overnight-2026-11-19.md`. Boats/lures/tools already relocate via `LegacySlotRelocation` + `TableDescriptor`;
upgrades via `LegacyUpgradeRelocation`. Rods are the last gap.

## Real collision
All-7 pack: TGE adds "Proto-Rod" at RodData slot 6, cave-expansion adds "Mystic Rod" at slot 6 → Mystic Rod is lost.
TGE also extends `UpgradeData_Create_0` (`upg[6, 0..8] = -1`, rod loop `ix < 7`, 9 slots on the Proto-Rod) and
`SaveManager_Other_13..15` (`ix < 7`); cave-expansion adds the rod without an `upg[6, ...]` row.

## Do
1. Add a `RodData` table descriptor (identity `name[i]` unless an object field exists — verify in vanilla `RodData_Create_0`).
   Later claimant moves to the next free slot; rewrite typed sinks only in its own added/changed lines
   (`RodData.<field>[N]`, bare fields in RodData events / `with (RodData)`, `rod_was_purchased(N)`-style helpers,
   `rod_id`/`item_id` in RodData/RodCard/RodMenu/Rod scope — verify actual names). Unclassified literals near "rod" → fail closed with a warning.
2. `UpgradeData.upg[rod', iy]` / `object[rod', iy]` row: ensure a row exists for the relocated rod. If the claimant has no row
   (cave-expansion), emit one with the same column count the base/other claimant uses for vanilla rods (`-1`), never shrink TGE's 9 columns.
   Also `cost_base[rod']` if the code reads it (copy the claimant's value or the nearest vanilla default; document the choice).
3. Raise every hard-coded rod-count bound to cover the highest rod id: `SaveManager_Other_13/14/15` `ix < K`, `UpgradeData_Create_0`
   loops, `Rocket_Step_2` `target_distance[]`, `start_quest_dist` `dist[]/dpth[]`, `RodData.nItems`, placeholder `unlocked[]` lines.
   Raise slot (`iy`) bounds to the max column count any claimant uses (TGE: 9), never lower.
4. `spr_rod` frames: rods index `spr_rod` by id. Append the relocated claimant's frame as frame `rod'` (like `spr_upgradeicons`
   in `LegacyUpgradeRelocation`), keeping the other claimant's frame at its slot.
5. Decision line `[CGF-RELOCATE-006] Rod "Mystic Rod" (cave-expansion) moved from slot 6 to 7; rewrote N typed references; spr_rod frame 6->7`.
   Pack acceptance (`PackAcceptance`) shows it as Relocated.

## Tests
Synthetic in `LegacySlotRelocationTests` (or a new `LegacyRodRelocationTests`): two rods on one slot → both present; row emitted for a
claimant without one; bounds raised in SaveManager stubs; 9-column row of the other claimant untouched; same identity → no-op;
unrelated literal untouched; unclassified literal → warning, no relocation.

## Real check (VPS; use $HOME, run one build at a time — memory)
`dotnet run --project src/Jamm.Cli -c Release -- build --base $HOME/cgf-files/legacy/2023-08-16_manifest-9039707068725406600/data.win --order $HOME/cgf-files/private-vel/TGE.win,$HOME/cgf-files/mods/cave-expansion.win --out /tmp/rod.win`
(find cave-expansion's real path under ~/cgf-files if different). Report decision lines + acceptance summary. Then, if time permits,
the all-7 build. Optional in-game smoke test: `~/jamm-night/bin/run.sh /tmp/rod.win rod dock 120`.

## Rules
AGENTS.md. Zero warnings, `dotnet test -c Release` green. No commit/push. Only this worktree + /tmp. Never copy TGE/PWB anywhere else.
Summarize at the end (what works, what is still open, exact decision lines).
