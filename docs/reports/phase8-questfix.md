# Phase 8 — Smooth quest label fix

Quest Draw increments `QuestEngine.quest_counter` to position labels. Smooth freezes
Begin Step on render-only frames, skipping the reset and letting the counter grow
across draws. The controller's Pre Draw now resets it to zero only when
`jamm_render` is true and a QuestEngine instance exists. A WHY comment explains
the dependency; logic frames and the other render behavior are unchanged.

Before modifying the archive, Smooth checks that
`gml_Object_QuestEngine_Step_1` decompiles to exactly `quest_counter = 0;`
(ignoring whitespace). A missing or changed event fails with guidance to use the
original legacy 2023 input instead of assuming its behavior.

Validation:

- Solution and ModMaker Release builds passed: **0 warnings, 0 errors**, using
  `dotnet build -c Release -m:1 --disable-build-servers -p:NuGetAudit=false`
  (with `tools/Jamm.ModMaker` as the project for the second build).
  The initial unqualified solution build exited unsuccessfully without diagnostics;
  the explicit single-worker build above succeeded.
- Rebuilt from the original legacy 2023 archive into new files:
  `out/phase8/jamm-smooth-fps.win` (44,619,654 bytes) and
  `out/phase8/jamm-smooth-fps-debug.win` (44,623,238 bytes, `smooth --debug`).
- `cgfmm verify` passed for both: **1,835 / 1,839** code entries respectively
  decompile and compile, references valid, no verification warnings.
- Decompiled the generated release Pre Draw and confirmed the reset sits inside
  `if (jamm_render)` with the instance-existence guard.
- `git diff --check` passed. Input game files were opened read-only. No commits,
  pushes, Git configuration changes or external submodule source edits.

In-game visual confirmation remains for the orchestrator; no game session was run here.
