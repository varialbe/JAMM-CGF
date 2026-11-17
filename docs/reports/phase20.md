# Phase 20 — built-in JAMM mods

Implemented Perf, Smooth, HD Lite and Console as bundled generators available from
the GUI and `cgfmm builtin list` / `cgfmm builtin add <id>`. No SDK or terminal is
needed by GUI users. ModMaker remains the developer CLI and references the same
Core generator implementations; no generator code is duplicated. Core has no
console output.

Generation opens the configured base read-only, writes a temporary archive in
manager storage, reloads it before publication and imports through ModLibrary.
The `builtin/<id>-<generatorVersion>-<baseSha>.win` cache has a SHA-256 receipt;
changed bases, changed generator versions and damaged output require regeneration.
Library IDs (`builtin-perf`, etc.) and names remain stable across regeneration,
including profile references. Failed replacement restores the previous library
entry and patches. GeneratorVersion must be incremented for future behavior changes.

The GUI displays descriptions and hotkeys after base setup. A switch generates,
imports and enables the mod in the active profile (creating a default profile if
needed). Disabling persists profile disablement. Existing RunAsync supplies worker
execution, progress, cancellation checkpoints and friendly errors. Simple mode
shows the group above the list; Advanced mode shows it inside the scrollable
library so load-order controls retain their space. Check/Install and Play use the
normal pipeline. The existing obj_jamm_* sanitizer rule already recognizes all
four controllers, so no sanitizer exception or version change was needed.

README, example-mod instructions and the user guide now describe the app route
first, with ModMaker reserved for developer variants and optional upscaled HD.

## Validation

- Release solution build with `dotnet build -c Release -m:1`: zero warnings/errors.
  The default parallel build cannot start reliably in this restricted environment;
  the serial build covers the full solution, including ModMaker and both test projects.
- Required `dotnet test tests/Jamm.Core.Tests -m:1` and
  `dotnet test tests/Jamm.Gui.Tests -m:1` were attempted. VSTest cannot open its local
  communication socket here (`SocketException (13): Permission denied`), so its test
  host aborts before running tests.
- Both complete suites were additionally executed in-process through xUnit's own
  XunitTestFramework discoverer/executor, preserving theory discovery and Avalonia
  headless test cases: Core 306 total, 0 failed, 7 skipped; GUI 109 total, 0 failed,
  1 skipped. The temporary runner was removed after validation.
- New tests cover generation/import/cache hits for every generator using synthetic
  game data, base/version cache misses, damaged-cache recovery, stable profile IDs,
  cancellation, unsupported input errors, CLI success/rejection, sanitizer retention,
  and a verified build combining all four built-ins with an independent third-party
  mod. GUI tests exercise profile toggles, failure handling, group descriptions and
  real mouse clicks in simple/Advanced layouts.
- Real-file tests remain opt-in; no copyrighted game archives are added. No real
  game runtime or hotkey smoke test was performed. No commit was made.
