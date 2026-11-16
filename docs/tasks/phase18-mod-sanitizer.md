# Phase 18 — Mod sanitizer: strip third-party performance hacks, keep content (implementer: agy / Gemini)

Branch `feat/mod-sanitizer` (from main). Do not commit, push or change git config (the orchestrator does that).
Codex works on `feat/hud-layout` in parallel (HUD/ExperienceBar/Quest drawing) — do not edit those code paths.

## User decision (German, verbatim)
"wir modden seine mod, wenn jemand in JAMM die mod runterlädt kriegt er eine angepasste version. diese version
entfernt alle patches für performance generell von allen mods die runtergeladen werden nicht nur von der weil
UNSERE optimierungen sind gut deren sind meistens random und unpredictable. wir reduzieren den inhalt somit aber
das ist halt so"
Plus, for Oops! All Chaws! (OAC): keep its content (new fish, Bulbguard upgrade), remove its hacks: halved physics
timing, fish logic every 6th tick, 5000-px draw/sonar cut-offs, FPS/X/Y debug overlay.

## Evidence: what OAC changes (patch of library mod d73e967c…; see ~/.local/share/cgfmm/patches)
- Girl_Create_0: time_scalar/time_scalar_last 0.00003 -> 0.00006 (DELTA_INSTANT = 1/time_scalar halves).
- Girl_Step_0: `delta = delta_time` / `delta_fixed = clamp(delta_time / 16000, 1, 50)` -> `room_speed / 30`.
  Net effect: everything scaled by Girl.DELTA_INSTANT * Girl.time_scalar (lure flight, reel, fish, boat, camera)
  runs at half speed.
- Fish_Step_2: logic gated by `check_timer++; if (check_timer >= 6)`; Fish_Create_0 adds check_timer.
- Fish_Draw_0, fish_basicdraw, fish_predraw_switchcase, SonarPanel_Draw_0, Weed_Step_0, Dangler_Step_0:
  `point_distance(x, y, Camera.x, Camera.y) < / > 5000` cut-offs (visible = false; exit; skip draw).
- FishThinner_Step_0: fps-based cull_trigger rewritten. CaveRock_Create_0: checkTimer = 0. Object fpsController.
- ExperienceBar_Draw_0: debug overlay "FPS:", "X:", "Y:".
- Content to KEEP: new fish objects (Elder_Chaw, Bassoli, Goldi, Kuito, Luce, Minau, Mulite, ...), roots/tree,
  catalog additions, Bulbguard upgrade (UpgradeData/UpgradeMenu/UpgradeCard/Rod/Lightbulb/Bulbeater changes),
  Boat7 MAX_X, SaveManager changes needed by its content.

## Deliverables
1. `ModSanitizer` in Jamm.Core working on patches (never on the original files): classify each changed-code
   hunk deterministically into categories {timing, tick-throttle, distance-cull, fps-adaptive, debug-overlay,
   content}. Revert hunks of the non-content categories to the base version (hunk-level, not whole entries, so
   content in the same entry survives — e.g. Fish_Step_2 content lines must stay). Remove helper variables/objects
   that only served removed hacks (fpsController, check_timer) when nothing else references them; never remove
   anything referenced by kept content. Rules must be explicit (patterns over the decompiled GML, e.g. writes to
   delta/delta_fixed/time_scalar/DELTA_INSTANT/room_speed, reads of fps/fps_real for behaviour, point_distance
   to Camera/view with visible=false/exit/skip-draw, modulo/counter gates `>= N` around existing logic,
   draw_text of fps/coordinates), documented, and unit-tested. When unsure, keep the hunk and report it as
   "unclassified" — do not guess.
2. Apply it to every imported mod (downloads AND local Add mods) by default; the library stores the sanitized
   patch next to the original patch and records what was removed (category, entry, line summary). Our own mods
   (Smooth, HD/HD Lite, Perf — built by tools/Jamm.ModMaker, identify by their obj_jamm_* objects/marker) are never
   sanitized. Advanced mode: per-mod switch "Keep the author's performance changes" (off by default).
3. UI: on the mod row/card a short line "JAMM removed N performance changes" with an info flyout listing them.
   Provenance/hash identity unchanged (sanitizing is a deterministic function of patch + sanitizer version; bump a
   version so caches/compat DB keys stay correct — tell the orchestrator what the compat DB key needs).
4. Real-file check: build the user's set (Abyss, Cave, Colour, HD Lite, Perf, Smooth, OAC) with sanitizing on;
   report per mod what was removed; for OAC the timing, 6-tick, 5000-px and debug hunks must be gone and the
   content kept; Abyss/Cave/Colour: report honestly (probably nothing or little removed). cgfmm verify must pass.
   Mods: ~/Cat-Goes-Fishing-Mods/*.win, OAC library copy under ~/.local/share/cgfmm/mods/d73e967c*/.
5. Build with 0 warnings; run tests if the environment allows; report docs/reports/phase18.md.
