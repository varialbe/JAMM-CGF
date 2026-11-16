# Phase 17 — HUD flex layout: mod texts never overlap quests/level (implementer: Codex)

Branch `feat/hud-layout` (from main). No commits/pushes/git config, no submodule edits. Another Codex fixes
tools/Jamm.ModMaker/SmoothMod.cs in parallel — do not touch it.

## User request (German, verbatim)
"der scheiß dev von oops all chaws bro. mach dass sämtliche rendered texte die in dem bereich um quests/lvl etc wie
eine flexbox platziert werden"

## Facts (orchestrator)
- Oops! All Chaws! (Litterbox 30, Game Jolt build 2020716; in the user's library) changes
  `gml_Object_ExperienceBar_Draw_0` and adds three debug lines right-aligned above the level bar:
  `draw_text_shadowed(draw_x + 430, draw_y - 60, "FPS:" + string(fps))`, `... - 40, "X:" + string(Camera.x)`,
  `... - 20, "Y:" + string(Camera.y)` (draw_x/draw_y = view position + ExperienceBar offsets).
- Base quests: `gml_Object_Quest_Draw_0` draws each label right-aligned at
  `x = view_xview + view_wview - 25`, `y = view_yview + 452 - 20 * QuestEngine.quest_counter` (counter reset in
  QuestEngine Begin Step) — so quests stack upwards from y=452 and collide with the OAC lines.
- Other HUD drawers relative to the view: BoatData_Draw_0 (top), UpgradeData_Draw_0 (bottom-left),
  ExperienceBar, BreathBar, Message/MessageTwoSize, Lightbulb (GUI layer), quest GUI objects (Draw_64).

## Deliverables
1. Audit the HUD area around quests/level in base + all mods available locally (~/Cat-Goes-Fishing-Mods/*.win and
   the library copy of Oops! All Chaws!): every draw_text*/draw_sprite* in Draw/Draw GUI events that targets that
   region (view-relative bottom-right column above the level bar). Write the list into the report.
2. Runtime HUD layout API injected by JAMM into merged archives (like CatalogRuntime): a per-frame "flex column"
   anchored above the level bar on the right, laid out bottom-to-top with a fixed gap; scripts e.g.
   `jamm_hud_reserve(height[, owner])` -> y (and x anchor) for the current frame, reset once per frame for every
   view/draw pass (careful: Smooth renders extra frames; the reset must happen per draw frame, not per logic tick).
   Quests use it (each quest reserves its line), so the column order is: level-related lines, then mod lines, then
   quests (document the order; deterministic).
3. Merge-time adapter: rewrite mod-added draw_text* calls whose coordinates resolve into that region (literal
   offsets from draw_x/draw_y of ExperienceBar or view-relative bottom-right) into reservations from the column, so
   OAC's FPS/X/Y lines no longer overlap quests. Only rewrite calls you can prove are in that region; leave
   everything else untouched and report what was rewritten.
4. docs/hud-layout-api.md for mod authors. Verification: synthetic tests (overlap-free placement, deterministic
   order, per-frame reset), real-file build with all mods incl. OAC passes cgfmm verify; describe what the user will
   see. Report docs/reports/phase17.md. The orchestrator checks in game.
