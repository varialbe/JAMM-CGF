# Phase 20: Built-in JAMM mods (Perf, Smooth, HD Lite, Console) available in the GUI with one click

## Problem (found in a blind new-user test, 2026-11-17)
A new user installs JAMM via scripts/install.sh, sets up the legacy game, installs mods — but the JAMM example mods
(Perf, Smooth, HD Lite, Console with `unlockall`) are nowhere in the app. They only exist as `tools/Jamm.ModMaker`
CLI commands (`dotnet run --project tools/Jamm.ModMaker -- console <original> <out>`), see docs/example-mods.md.
F12 does nothing because the console mod is never installed. Users without terminal knowledge cannot get them.

## Goal
In the GUI mod list there is a "Built-in (JAMM)" group with Perf, Smooth, HD Lite and Console. The user toggles
them on like any other mod; JAMM generates each one in the background from the configured untouched base
(`config.base`, the verified legacy data.win) and imports the result into the mod library. No terminal, no SDK.

## Requirements
1. Move/share the generators (PerfMod, PerfMode, SmoothMod, HdMod without upscaled pages, ConsoleMod, ModObjects and
   their deps) so Jamm.Core (or a new library referenced by Core) can call them. Keep the ModMaker CLI working
   (it should call the shared code). No duplicated generator code.
2. Generation: input = configured base archive (read-only), output in the manager data dir (e.g. `<data>/builtin/<name>-<generatorVersion>-<baseSha>.win`),
   cached by base sha + generator version; regenerate if either changes. Then register via the existing mod import path
   (ModLibrary) with a stable id/name ("JAMM Console", "JAMM Perf", "JAMM Smooth", "JAMM HD Lite") and description incl. hotkeys
   (Console: F12 or backtick, `unlockall`; Smooth: F7; Perf: F8; HD Lite: F11).
3. GUI: built-in group visible after setup (when a base exists), toggle = generate (with progress/cancel via existing RunAsync)
   + enable in active profile; then normal Check/Install. Show a short description and hotkeys per built-in mod.
   Errors are shown with a clear message, never crash.
4. CLI: `cgfmm builtin list` and `cgfmm builtin add <perf|smooth|hd-lite|console>` (generate + import).
5. Built-in mods must combine with third-party mods through the normal merge/conflict pipeline (they are JAMM mods: ModSanitizer.IsJammMod already
   recognizes obj_jamm_console etc.; make sure the others are recognized too and survive sanitizing).
6. Tests: generation from a synthetic/test base (follow existing headless test patterns, e.g. Phase14HeadlessTests), cache hit/miss,
   CLI commands, GUI view-model toggle. Real-file tests stay optional/skipped without JAMM_ORIGINAL.
7. Docs: README (short "Built-in mods" section), docs/example-mods.md and user-guide updated: built-ins come from the app; ModMaker CLI is for developers.
8. `dotnet build -c Release` 0 errors; `dotnet test` for Jamm.Core.Tests and Jamm.Gui.Tests passes. Do not commit.
Write a short report to docs/reports/phase20.md.
