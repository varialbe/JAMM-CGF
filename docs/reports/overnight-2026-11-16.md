# Overnight pipeline log (2026-11-16)

Orchestrator: Claude (review + coordination). Implementation: Codex via `.orchestrator/jamm-pipeline.sh`.
This file is updated regularly during the night; the pipeline's own status lines are mirrored below.

## Pipeline status
    2026-11-16 00:13:28  pipeline started (phases 2 3 4)
    2026-11-16 00:13:29  phase 2: implementation started
    2026-11-16 00:48:07  phase 2: agy unavailable -> review by a second Codex instance
    2026-11-16 01:04:38  phase 2: DONE - committed and pushed to experimental (6efed81 Phase 2 (pipeline): Implemented Phase 2: `check`, three-way GML previews, field merges, indirect conflict detection, reusable resolution cho)
    2026-11-16 01:04:38  phase 3: implementation started
    2026-11-16 01:48:33  phase 3: review by Gemini (agy)
    2026-11-16 02:12:51  phase 3: DONE - committed and pushed to experimental (c741b36 Phase 3 (pipeline): Implemented Phase 3 on `experimental`, including build resolutions, dry-run, verification, guarded installation and rest)
    2026-11-16 02:12:51  phase 4: implementation started
    2026-11-16 02:39:09  phase 4: review by Gemini (agy)
    2026-11-16 02:56:31  phase 4: DONE - committed and pushed to experimental (ccba099 Phase 4 (pipeline): Phase 4 is implemented on `experimental`.)
    2026-11-16 02:56:31  pipeline finished: all phases done. In-game test of the merged data.win on the desktop is still required.
    2026-11-16 02:56:51  pipeline started (phases 3b 5)
    2026-11-16 02:56:52  phase 3b: implementation started

## Orchestrator notes
- 00:20 Took over; Phase 2 implementation running.
- 00:48 Phase 2 implementation passed the gate on first try (80 tests, 83 with real files; 52 conflicts for all three mods). Review by second Codex (agy not logged in).
- 01:08 Phase 2 reviewed by orchestrator on real files: build 0 warnings; check A,C,L = 50 CONFLICT / 7 AUTO / 13 INFO / 6 WARNING (exit 3), --details works from cached patches (10 s); game files unchanged. Minor follow-ups noted (single-mod 'unsupported' sprites listed as CONFLICT; per-hunk duplicate rows). Real clash: Abyss and Cave both add boat slot 8.
- 01:49 Phase 3 implementation passed the gate (112 tests with real files). Review by Gemini (agy now logged in): 13 findings incl. load-order/resolve edge cases in BuildPlan, action metadata order, room instance order, symlink resolution. Codex is fixing.
- 02:22 Phase 3 reviewed on real files. Safety OK: backup created once and SHA-verified, reinstall over own build accepted, foreign data.win refused, restore returns the original hash; game files unchanged. BLOCKER found: numeric asset indices in GML (e.g. catalog object[2,58] = 641) are not remapped. Cave removes 3 base objects and all mods append at the same indices, so 573-677 object indices per input name a different object in the merged A,C,L build (641 = Overbite in Abyss, Shados in merged). The archive loads and passes verify but references the wrong assets. Fix task phase3b (index stability + remap + semantic verification) runs after Phase 4, before the GUI.
- 03:01 Phase 4 reviewed: quick start walkthrough with temp data dir and temp game folder works end to end (config, add x3 with base compatibility, list, profile, apply --yes in 68 s, restore to original hash, no leftover builds). Minor: keep-both cannot be applied to an entry that also has an overlapping hunk (F4.1). Started pipeline for 3b (blocker fix) then 5 (GUI).
