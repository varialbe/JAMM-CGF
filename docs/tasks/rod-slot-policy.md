# Task: rods with more than 8 upgrade slots ("vanilla progression" policy)

Idea from a modder (Vel, TGE author): in vanilla each rod has one more upgrade slot than the previous one; the Master Rod has 8.
When adding a rod, a modder should choose: keep the simple cap (≤ 8 slots, today's behavior) **or** follow the vanilla rule and get
one more slot than the strongest existing rod (9, 10, …). Today `jamm_register_rod` rejects `upgrade_slots > 8`
(`src/Jamm.Core/Building/ItemsRuntimeScripts.cs` ~line 306) and many runtime loops hard-code `jamm_iy < 8`.

## Do
1. API (`jamm_register_rod`, docs `docs/modding/api-reference.md` row 9 `upgrade_slots`):
   * `0..JAMM_MAX_ROD_SLOTS` (constant, 16) explicit count; default stays `8`.
   * `-1` = "vanilla progression": slots = (max slot count of all rods registered/existing before it) + 1, clamped to the max.
   * Store the count per rod (e.g. `RodData.jamm_slots[rod]`) and a global `UpgradeData.jamm_max_slots` (max columns in use).
2. Replace every hard-coded `< 8` slot-column bound in JAMM's runtime (ItemsRuntimeScripts, upgrades runtime, save sidecar
   `slot:<rodkey>:<iy>` in `docs/modding/saves.md`, unlockall/console if relevant) with the per-rod count / `array_length_2d` /
   `jamm_max_slots`. Legacy mods matter: TGE's Proto-Rod already has 9 columns (`upg[6, 0..8]`) — JAMM must never truncate,
   reset or skip column 8 when saving/loading/resetting. Add a test for that.
3. Vanilla `SaveManager_Other_13..15` loops `iy < 8`: when any rod has more columns, raise that bound at build/runtime so slots ≥ 8
   persist in the vanilla save too (or document why the JAMM sidecar alone is sufficient — verify which one actually restores them).
4. UI: find how the rod upgrade menu lays out `UpgradeSlot` instances (decompile the vanilla menu / `UpgradeSlot` / rod menu objects
   with `dotnet run --project src/Jamm.Cli -c Release -- diff <base> <base> --show <code>` or the existing dump tooling). Slots ≥ 8 must be
   visible and usable: wrap to a second row or shrink spacing — pick the least invasive option and describe it.
   If TGE already solved the 9th-slot layout, note how (read-only; base path below).
5. Modkit: if modkit has no rod JSON yet, only add `upgrade_slots` (int or `"vanilla"`) to the rod schema if rods exist there; do not build
   rod modkit support in this task — list it as follow-up.
6. Docs: explain both options in `docs/modding/` in plain words ("simple: 8 slots" vs "vanilla rule: one more than the best rod").

## Tests
Runtime-script tests (see `tests/Jamm.Core.Tests/UpgradesRuntimeTests.cs`): explicit 12 slots accepted; 17 rejected; `-1` after vanilla
→ 9; two `-1` rods → 9 then 10; save/load sidecar round-trips column 8+; legacy 9-column row survives reset/load untouched.

## Rules
AGENTS.md. Zero warnings, `dotnet test -c Release` green. No commit/push, no rm. Only this worktree + /tmp.
Real base data.win for read-only inspection, if present: ask nothing, just skip real-file steps if unavailable.
Summarize at the end: what changed, UI decision, open points.
