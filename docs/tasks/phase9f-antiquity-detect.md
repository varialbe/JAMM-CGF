# Phase 9f — Antiquity texture item + detection polish (implementer: Codex)

Continue on `ui/browse-mods`, same rules.

1. Antiquity (Litterbox id 4, Game Jolt build 1700162, 56,078,502 bytes) now downloads, but analysis says
   "Incompatible: The patch cannot apply: Texture item 'PageItem 1297' has pixels but no embedded page."
   This exact file runs in the user's game (it is the data.win in their external Steam library), so the game
   tolerates that item. Make JAMM tolerate it as well without corrupting anything: find why the item has pixels
   but no page (e.g. a TPAG entry whose page reference is null/out of range in the mod), decide the safe
   handling (keep the item as-is when unused, or carry the mod's page/region if referenced), and keep
   verification meaningful. If a narrow, verifiable fix is not possible, classify it as "JAMM cannot process
   this mod yet (tool limitation)" with the yellow icon and the exact reason — never "Incompatible".
   Regression test with a synthetic archive reproducing the condition.
2. Detection polish from the orchestrator's real-desktop run (Phase 9d):
   - The 2023-08-15 public manifest build (GEN8 ts 1692057440) is a legacy build that the known mods were made
     from; classify it yellow ("older legacy build, usable"), not red.
   - Sort the green group so a plain original folder comes before data.win.cgfmm-original backups inside JAMM
     copies; preselect the plain original.
Report: docs/reports/phase9f.md. No commits/pushes/git config.
