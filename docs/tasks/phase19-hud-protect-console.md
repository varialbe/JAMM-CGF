# Phase 19 — protect the HUD, fix the F8 toast, optional console with unlockall (implementer: agy / Gemini)

Branch `feat/hud-protect-console` (from main). Do not commit, push or change git config.

## User feedback (German, verbatim)
"wenn man f8 drückt ist da ein schwarzer block. hud elemente sind lila, die sollen auch geschützt sein. mach ein
unlock all cheat (nur mit console)"

## Facts (orchestrator)
- Purple HUD: The Abyss replaces UI sprites with its own purple-themed art: spr_catalog_arrow, spr_catalog_btn,
  spr_catalog_tab, spr_icon, spr_iconsmall, spr_lurecard, spr_radarbutton, spr_rodcard, spr_savecard_btn,
  spr_sonarpanel, spr_wallet. Oops! All Chaws! changes spr_upgradeicons (adds the Bulbguard upgrade icon frame —
  that frame is content and must stay), spr_lightbulb, spr_boat_6. No mod changes Colors.ui_*.
- F8 = Perf mod's experimental performance-mode toggle (tools/Jamm.ModMaker/PerfMod.cs / PerfMode.cs): its toast
  shows only a black rectangle (no text) in the user's merged build (HD Lite active: application surface scaled,
  GUI 1200x720). Likely missing draw_set_font/colour/alpha/halign state or GUI coordinates; compare with the Smooth
  toast (works) in SmoothMod.cs.
- The game already has `UnlockUpgrade(n)` and a hidden cheat flow in Girl_Step_0 (`cheat_armed`, `cheat_fired`).

## Deliverables
1. HUD protection in the mod sanitizer (src/Jamm.Core/Patching/ModSanitizer.cs, Phase 18): new category
   `hud-style`. Define the HUD/UI asset set from evidence (sprites/fonts/backgrounds drawn by HUD/UI objects:
   wallet, sonar panel, catalog UI, cards, upgrade menu, buttons, icons, level bar, message boxes, title/menus);
   document the list. Third-party mod changes to those assets revert to the base asset. If a mod APPENDS frames to
   a HUD sprite (more frames than base, e.g. spr_upgradeicons for Bulbguard), keep the base frames unchanged and
   keep only the appended frames from the mod (verify the mod's code references the new frame index). New HUD
   sprites added by mods (not replacing base ones) stay. Report per mod in the UI list like other removals;
   respect the existing "Keep the author's performance changes" opt-out style with a separate Advanced switch
   "Keep the author's HUD design". JAMM's own mods are never touched.
2. Fix the F8 toast (PerfMod) so it shows readable text like the Smooth toast in all modes (HD Lite fullscreen
   and windowed); also check every other JAMM toast/overlay (Smooth, HD, HUD runtime) for the same state bug.
3. New optional mod `console` in tools/Jamm.ModMaker (`jamm-modmaker console <original.win> <out.win>`): an
   in-game console overlay (open/close with the backtick/grave key or F12 — pick one that the game does not use;
   check all keyboard events), single text input line (keyboard_string), command history, `help`, and
   `unlockall`: unlock every upgrade/rod/lure/boat/gear/hat/nightfall flag the game has (audit Girl_Step_0
   cheat code, UnlockUpgrade, RodData/LureData/BoatData/HatData unlock flags and saves); does NOT add money and
   does NOT touch the catalog; prints exactly what was unlocked and a warning that the next autosave keeps it.
   No hotkey cheat without the console. Build from the ORIGINAL data.win like the other ModMaker mods, never
   sanitized (obj_jamm_* marker). Docs in docs/example-mods.md.
4. Tests (sanitizer hud-style incl. appended frames; console generator), real-file check with the user's set
   (Abyss, Cave, Colour, HD Lite, Perf, Smooth, OAC): Abyss HUD art reverted, OAC Bulbguard icon frame kept,
   cgfmm verify passes. Build with 0 warnings: `dotnet build -c Release` AND `dotnet build tools/Jamm.ModMaker -c
   Release` (the tool is not in the solution — add it to JAMM-CGF.sln so normal builds include it).
Report docs/reports/phase19.md.
