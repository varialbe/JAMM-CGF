# Task: Vel's rod/boat points – ModKit JSON rods + legacy unlock checker

Two requests from Vel (TGE author, tester). Branch `exp/vel-rods-boats` in `~/jamm-wt/vel-rods-boats`, from main.
Report `docs/reports/vel-rods-boats.md`.

## Part A – rods in ModKit JSON
ModKit JSON (`src/Jamm.Core/ModKit/ModKitPackage.cs`) supports `boats` and `hats`; rods exist only as GML API
(`jamm_rod_register(key, name, cost, cast_speed, rod_power, rod_power_flat, reel_speed, sinker_distance, disp_sprite [, upgrade_slots, unlocked])`,
docs/modding/api-reference.md). Add a `rods` array with the same fields, validated with path-precise errors like boats/hats:
* `key`, `name`, `cost`, `cast_speed`, `rod_power`, `rod_power_flat`, `reel_speed`, `sinker_distance`, `sprite` (PNG imported like boat/hat
  sprites, same sprite import pipeline and origin rules), optional `upgrade_slots` (integer `0..16`, or the string `"vanilla"` = `-1` rule:
  best existing rod + 1, clamped 16; default 8), optional `unlocked` (bool), optional `unlock` block consistent with whatever boats use
  for purchase/unlock (shop purchase price; reuse the boat/hat schema, do not invent a second style).
* Generates exactly what the GML API would do (call `jamm_rod_register` in the generated init), so behaviour is shared and the Rod-slot
  policy, scrolling shop and relocation all apply.
* GUI ModKit editor (`exp/gui-modkit` work is on main): add rods next to boats/hats if the editor lists item kinds; keep it small.
* Docs: modkit-reference.md + cookbook example (with both `upgrade_slots` forms). Example mod under `examples/modkit/` with one rod.
* Tests: schema validation (bad slot values, unknown keys, missing sprite), generated GML snapshot, build integration on the legacy base,
  and an in-game check on the VPS: build vanilla + the example rod mod, run `~/jamm-night/bin/run.sh <win> velrod dock 600` (RECORDED)
  and, if the bench can open the shop/equip a rod, show the rod in the shop with a screenshot.

## Part B – legacy mod checker: boats/rods without an unlock path
Common legacy-mod bug: a new boat (or rod) is added to the tables but nothing ever unlocks/sells it, so the game glitches or the item is
unreachable. In `check` (and the GUI check view):
* For each mod, detect boats/rods (BoatData/UpgradeData rows, objects, relocated slots – reuse the existing table/slot analysis) and look
  for any unlock path: shop purchase entry/price usable by the shop code, quest/achievement reward, save-unlock flag set somewhere in code,
  explicit `unlocked` default. Be conservative and explain the evidence (which code/table makes it reachable).
* No path found → warning `[CGF-UNLOCK-001] <mod>: boat '<name>' (slot N) has no unlock path (not buyable, no quest, never unlocked in code)`.
  Same for rods (`CGF-UNLOCK-002`). Run it on all real mods (Antiquity, Abyss, Colour, Verdant, Cave, PWB, TGE) and list the results in the report;
  hand-check every hit and every "reachable" claim for at least two mods so the heuristic is trustworthy (false positives are worse than misses).
* Optional fix, opt-in per mod (profile setting + GUI toggle + CLI `cgfmm profile unlockfix <profile> <mod> on|off`): make the affected item
  buyable in the shop with a sensible price (derived from neighbouring slots), via the existing shop/slot API. Off by default. The decision line
  of the build names what was changed.
* Tests: synthetic legacy fixtures (boat with/without shop row, rod unlocked only by quest code, rod unlocked by a save flag), checker output,
  fix on/off build output, profile persistence.

## Rules
AGENTS.md. Zero warnings, all tests green. In-game runs only via `~/jamm-night/bin/run.sh`, one game at a time: other agents share the VPS,
check `pgrep -f "Cat Goes Fishing.exe"` and wait. Never copy TGE/PWB off the VPS. Commit on the branch, no push, no merge to main.
