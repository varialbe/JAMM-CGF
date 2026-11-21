# Task: Cap fish sale values at 100,000,000 in every modded build

Branch `exp/fish-value-cap`, worktree `~/jamm-wt/fish-value-cap`.

## User request (2026-11-21)
"Cuddlefish soll nicht 999m wert sein, alle Fische die über 100m wert sind soll JAMM automatisch auf 100m limitieren."

## Findings
* TGE (`~/cgf-files/private-vel/TGE.win`, sha 5fd293c7…) sets `gml_Object_Cuddlefish_Create_0`:
  `BASE_VALUE = 5000000000; XP_VALUE = 5000000000;` (vanilla: 50 / 50). The game shows it as $999,999,999.
* In the user's build `~/jamm-night/personal-tge/tge-jamm.win` (TGE + perf, hd-lite, console, smooth, silo,
  weather-pack, endless-ocean) Cuddlefish is the only fish whose Create code assigns a literal BASE_VALUE > 100M.

## Required
1. New build step for every build with at least one mod (pure vanilla stays byte-identical): every fish
   object (Fish descendants, including ModKit/Fish-API fish and catalog-registered fish) whose sale value
   would exceed **100,000,000** is capped to exactly 100,000,000.
   * Literal `BASE_VALUE = <n>` assignments in fish code (Create and any other event/script that sets it).
   * Values passed through `jamm_fish_register` / ModKit `fish[].value`.
   * If a mod computes BASE_VALUE at runtime (non-literal), add a runtime clamp right after the assignment.
   * Leave XP_VALUE unchanged (report XP values > 100M in the decision text so the user can decide later).
2. Decision line like `[CGF-VALUE-001] Capped fish sale values above 100,000,000: Cuddlefish 5,000,000,000 -> 100,000,000 (TGE)`.
3. Make the cap configurable in the profile/config (`fishValueCap`, default 100000000, `0` = off) for CLI and GUI;
   document it in docs (user guide + modding docs).
4. Tests: unit tests for literal, ModKit and runtime cases; vanilla byte-identical; full suite green.
5. Verify: rebuild the user's combination with exactly this order and check Cuddlefish is capped:
   `cgfmm build --base ~/cgf-files/legacy/2023-08-16_manifest-9039707068725406600/data.win --order
   ~/jamm-night/personal-tge/{jamm-perf,jamm-hd-lite,jamm-console,jamm-smooth}.win,~/cgf-files/private-vel/TGE.win,~/jamm-night/personal-tge/{jamm-silo,weather-pack,endless-ocean}.win
   --out ~/jamm-night/personal-tge/tge-jamm-capped.win`
   (dump `gml_Object_Cuddlefish_Create_0` with `tools/Jamm.ModMaker dump`). In-game test only if the VPS Steam
   client is logged in (it may currently be logged out; then skip and say so).
6. Report `docs/reports/fish-value-cap.md`. Commit on the branch; do not merge into main.
TGE/PWB stay on the VPS only.

## Addendum (user, 2026-11-21): general "fair play" guard against developer/test values
Vel built TGE with dev values (Cuddlefish 5e9, and `unlockall` then crashes in `Rod_Step_0` reading
`upgrades[22]` out of range `[1,22]`). The user wants JAMM to neutralise unfair OP dev values in general,
not only fish value. Extend the step into a **Fair-Play guard** (`fairPlay` config, default on):
* Derive limits from the **vanilla base** (max vanilla value of each stat x a documented headroom factor,
  e.g. 2x) for: rod stats (cast speed, rod power / rod power flat, reel speed, sinker distance / sink speed),
  boat speed/stats, lure/upgrade effects, fish XP_VALUE, shop prices (no 0 or negative prices for items
  that cost money in vanilla), starting money / money grants in Create events.
  Fish sale value keeps the fixed 100,000,000 cap from above.
* Only clamp literal assignments and registration arguments (and a runtime clamp where values are computed);
  never change vanilla code entries that the mods did not touch. One decision line per clamped value
  (`[CGF-FAIR-001] ...`, mod name, old -> new).
* Make the JAMM Console commands `unlockall` / `unlockgear` safe with mods that change upgrade array sizes:
  analyse the TGE crash (`Rod_Step_0`, `upgrades[22]`) and guard index access so no build can crash there.
* Tests for each category; vanilla byte-identical; the report lists every clamp in the TGE build.
