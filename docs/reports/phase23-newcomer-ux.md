# Phase 23 — Newcomer UX: discoverable built-in mods, hotkey help

Branch `exp/newcomer-ux`. Source: the user's blind new-user test on 2026-11-17 (README -> install.sh -> GUI).

## Summary of Changes

### 1. Built-in mods during first run
- Extended the first-run setup flow (`MainViewModel.Setup.cs` and `MainWindow.axaml`):
  - When the user selects a detected legacy game and clicks **Use this**, JAMM sets up the base, creates the modded game copy, creates the default profile, and advances the setup dialog to the **Optional JAMM extras** step (`SetupStep.Extras`) instead of immediately closing.
  - The **Optional JAMM extras** panel lists all four built-in mods (**JAMM Perf**, **JAMM Smooth**, **JAMM HD Lite**, **JAMM Console**) with one-line descriptions and toggle switches (default off).
  - Toggling any of these switches immediately invokes `ToggleBuiltinAsync`, which generates/imports the mod and enables it in the active profile ("default"), identical to the toggles in the Manager tab.
  - The step is fully skippable: players can click **Done** or **Skip** to close the setup dialog and continue to the mod manager. A **← Back to game folder** button allows returning to the game folder selection step if needed.
  - If setup was opened by a deferred mod action (such as clicking "+ Add mods" or "Download" before configuring a base), that action resumes seamlessly.

### 2. Hotkeys explained in the GUI & Single Source of Truth in Jamm.Core
- Defined `BuiltinMods.Hotkeys` in `src/Jamm.Core/Management/BuiltinMods.cs` as the single source of truth for all in-game hotkeys and console commands:
  - Record: `BuiltinHotkey(string Key, string Description, bool IsCommand = false)`
  - Hotkeys:
    - **F8**: Perf overlay (switches modes)
    - **F7**: Smooth (cycles 60/120/off)
    - **F11**: HD Lite fullscreen (toggles fullscreen)
    - **F12 or backtick**: Console (opens in-game console; ` also toggles)
  - Console commands (from `ConsoleMod.cs`):
    - `help`: list commands
    - `unlockall`: unlock gear, max level, and full fish catalog
    - `unlockgear`: unlock equipment, upgrades, hats, HUD, nightfall
    - `maxlevel`: set level to max
    - `level <n>`: set level to n (0..max)
    - `catalog all`: mark all fish caught (alias: `unlockfish`)
    - `money <n>`: set dollars (0..999999999); money +<n> adds
    - `clear`: clear console log
    - `close/exit`: close console
  - Provided `BuiltinMods.HotkeyEntries`, `BuiltinMods.ConsoleCommands`, and formatted string `BuiltinMods.HotkeysHelpText`.
- Exposed on `MainViewModel` (`Hotkeys`, `HotkeysHelpText`).
- Added a visible **Hotkeys** help button next to the "Built-in (JAMM)" header in both Simple mode and Advanced mode with a Flyout displaying the formatted hotkeys and console commands.
- Added a visible, formatted "In-game hotkeys & console commands" section directly inside the first-run **Optional JAMM extras** panel.
- Updated `cgfmm builtin list` in `src/Jamm.Cli/ManagementCommand.cs` to print the hotkeys and console commands below the catalog.

### 3. README updates
- Updated the quick start in `README.md` to mention the **Built-in (JAMM)** group.
- Added a prominent top-level `## Hotkeys & console` section listing all shortcuts and console commands with a warning note about game saves.
- Clarified that `Play` launches the game directly from the desktop app (direct on Windows, Wine/Proton on Linux), removing outdated wording claiming Windows required manual launch after Install.

### 4. Newcomer walkthrough fixes (README -> scripts/install.sh -> GUI)
- `scripts/install.sh`:
  - Fixed confusing references to `install.sh --get-game`: when installed via curl or cloned to the default location (`$HOME/.local/share/jamm-cgf/src`), running plain `install.sh` failed if not in PATH. Updated output and manual instructions to specify `"$src/scripts/install.sh --get-game"` (and `~/.local/share/jamm-cgf/src/scripts/install.sh --get-game`).
  - Added an explicit post-install hint printing the exact command to re-check or wait for the legacy depot download.
- `src/Jamm.Gui`:
  - Clarified setup messages and progress during game detection and modded game creation.
  - Added tooltips and clear labels for Done, Skip, and navigation buttons in the first-run panel.

### 5. Automated Tests
- Updated `tests/Jamm.Core.Tests/BuiltinModTests.cs`:
  - Verified `cgfmm builtin list` outputs both the hotkeys (F8, F7, F11, F12 or backtick) and console commands (`unlockall`, `unlockgear`, `maxlevel`, `level <n>`, `catalog all`, `money <n>`).
- Updated `tests/Jamm.Gui.Tests/BuiltinViewModelTests.cs`:
  - `SingleSourceOfTruthHotkeysListCoversAllRequiredKeysAndCommands`: verifies `BuiltinMods.Hotkeys`, `HotkeyEntries`, `ConsoleCommands`, and `HotkeysHelpText`.
  - `MainWindowDisplaysHotkeysHelpButtonNearBuiltinGroup`: verifies the Hotkeys button with flyout and tooltip is effectively visible in both simple and advanced modes.
  - `FirstRunShowsOptionalExtrasPanelAfterGameUseWithTogglesHotkeysAndIsSkippable`: verifies the full first-run journey:
    1. Base detection opens setup dialog.
    2. Clicking "Use this" advances to "Optional JAMM extras" (`SetupStep.Extras`).
    3. Toggles default to off.
    4. Hotkeys help text is visible.
    5. Toggling a mod (e.g. `console`) immediately generates, imports, and enables it in the active profile (`builtin-console`).
    6. Navigation between game detection and extras steps works.
    7. Clicking "Skip" (or "Done") completes setup and closes the overlay.

## Validation Results

- `dotnet build -c Release`: **0 Warning(s), 0 Error(s)**.
- `dotnet test`:
  - `Jamm.Core.Tests`: **300 passed, 0 failed, 7 skipped** (real-file tests skipped as expected).
  - `Jamm.Gui.Tests`: **112 passed, 0 failed, 1 skipped** (real-file test skipped as expected).
- Git state: No commits or pushes made.
