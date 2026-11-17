# Phase 23 — Newcomer UX: discoverable built-in mods, hotkey help

Branch `exp/newcomer-ux`. Source: the user's blind new-user test on 2026-11-17 (README -> install.sh -> GUI).

1. **Built-in mods during first run.** The GUI first-run flow (see docs/tasks/phase9c-firstrun.md and src/Jamm.Gui) must
   make the "Built-in (JAMM)" group (Perf, Smooth, HD Lite, Console; see src/Jamm.Core/Management/BuiltinMods.cs and
   docs/reports/phase20.md) discoverable: after the base game is detected, show a short step/panel
   "Optional JAMM extras" listing the four built-ins with one-line descriptions and a toggle each (default off),
   which adds them to the active profile exactly like the existing built-in toggles. Skippable.
2. **Hotkeys explained in the GUI.** A visible "Hotkeys" help (e.g. a small "?" / Help button or an info section near the
   built-in group and in the first-run panel) listing: F8 Perf overlay, F7 Smooth, F11 HD Lite fullscreen, F12 or backtick
   Console, plus the console commands (help, unlockall, unlockgear, maxlevel, level <n>, catalog all, money <n>).
   Single source of truth: put the hotkey/command list in Jamm.Core (e.g. `BuiltinMods.Hotkeys`) and use it in GUI and CLI
   (`cgfmm builtin list` prints the hotkeys).
3. **README**: a "Hotkeys & console" section near the top-level usage, same content; mention the built-in group in the
   quick start.
4. Walk README -> scripts/install.sh -> GUI as a newcomer (read the code/text) and fix any other confusing text you find
   (wrong paths, missing next steps). List what you changed in the report.
5. Tests: GUI tests exist in tests/Jamm.Gui.Tests — add/adjust tests for the first-run built-ins step and the hotkey list.
   `dotnet build -c Release` zero warnings, `dotnet test` green (both test projects). Report: docs/reports/phase23-newcomer-ux.md.
