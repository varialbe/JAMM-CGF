# Phase 23b — Newcomer UX Polish: Window Sizing, Dialog Footers, Folder Clarity

Branch: `exp/ux-polish`. Follow-up to Phase 23 (`docs/reports/phase23-newcomer-ux.md`), addressing visual and layout findings from blind testing on a 1280x800 display (screenshots in `/home/varialbe/wt/ux-shots/`: `gui-firstrun.png`, `gui-2.png`, `gui-3.png`).

---

## Issues Identified & Solutions

### 1. Main Window Width & Header Button Overflow on 1280px Displays
- **Problem**:
  - The previous default window size was `1320x860` with `MinWidth="1040"`. On a 1280x800 screen, the initial window was wider than the screen (1320 > 1280), causing the right edge—including the "Settings" and "Hotkeys" buttons—to be cut off off-screen (`gui-firstrun.png`).
- **Solution**:
  - In `src/Jamm.Gui/MainWindow.axaml`, updated default dimensions to `Width="1200" Height="800"` and `MinWidth="960" MinHeight="560" WindowStartupLocation="CenterScreen"`.
  - In `src/Jamm.Gui/MainWindow.axaml.cs`, added static helper `MainWindow.CalculateBounds(PixelRect workingArea, double scaling, double requestedWidth, double requestedHeight, double minWidth = 960, double minHeight = 560)` and instance method `ClampToScreen(Screen? screen = null)`.
  - Window automatically clamps width and height to the screen's working area (respecting taskbars/docks and high-DPI scaling factors) and centers the window within the working area.
  - Invoked `ClampToScreen()` during window construction and in the `Opened` event handler.
  - Verified that at 1200x720 (and 1200x800), all header and top-right elements (Active profile indicator, Simple/Advanced mode switch, Play, Install, Conflicts, Hotkeys, and Settings) fit comfortably within the window bounds without horizontal overflow in both Simple and Advanced modes.

### 2. Setup Dialog Action Buttons Pushed Off-Screen
- **Problem**:
  - Action buttons (`Done`, `Skip`, `← Back to game folder` on the extras step; `Use this`, `Choose folder…`, `Check again`, `Cancel setup` on the game detection step) were placed at the end of the scrollable content inside `<ScrollViewer MaxHeight="520">`.
  - On smaller screens or when detection/extras content expanded, the primary action buttons scrolled out of view, forcing the user to scroll down to complete onboarding (`gui-2.png`, `gui-3.png`).
- **Solution**:
  - In `src/Jamm.Gui/MainWindow.axaml`, restructured the setup modal dialog (`ZIndex="10" Border`) into a two-row Grid:
    - **Row 0 (`*`)**: `<ScrollViewer MaxHeight="430">` housing the scrollable step contents (detection results, depot instructions, built-in mod cards, and in-game hotkeys reference).
    - **Row 1 (`Auto`)**: A dedicated fixed footer `<Border Background="#0A888888" BorderBrush="#20888888" BorderThickness="0,1,0,0" Padding="16,12">` placed outside the `ScrollViewer`.
  - Moved both sets of action buttons into this fixed footer:
    - Step 1 (DetectGame): `Use this`, `Choose folder…`, `Check again`, `Cancel setup`.
    - Step 2 (Extras): `Done`, `Skip`, `← Back to game folder`.
  - The action buttons remain persistently visible at all times regardless of window size, step content, or scroll position.

### 3. "Other folders found (not usable)" Dropdown Default State & Label
- **Problem**:
  - The unusable folders dropdown was always rendered, even when 0 unusable folders were detected, displaying `"Other folders found (not usable)"`. On first run without game files, this read like an error or failed state (`gui-firstrun.png`).
- **Solution**:
  - In `src/Jamm.Gui/ViewModels/MainViewModel.Setup.cs`:
    - Added `HasOtherGames => OtherGames.Count > 0`.
    - Added neutral label property `OtherGamesHeader`:
      - 1 folder: `"Show 1 other folder that was found (not usable)"`
      - N folders: `$"Show {OtherGames.Count} other folders that were found (not usable)"`
  - In `src/Jamm.Gui/ViewModels/MainViewModel.cs`:
    - Subscribed to `OtherGames.CollectionChanged` to raise `PropertyChanged` notifications for `HasOtherGames` and `OtherGamesHeader`.
  - In `src/Jamm.Gui/MainWindow.axaml`:
    - Bound `Expander.IsVisible` to `{Binding HasOtherGames}`.
    - Bound `Expander.Header` to `{Binding OtherGamesHeader}`.
  - Result: completely hidden when 0 unusable folders are found; displays neutral count-based phrasing when unusable folders are present.

### 4. Modded Copy Path Visibility & Explanation
- **Problem**:
  - Before any game was selected, the dialog displayed `"Separate modded copy:"` above a blank space, creating confusion about what action was needed (`gui-firstrun.png`).
- **Solution**:
  - In `src/Jamm.Gui/ViewModels/MainViewModel.Setup.cs`:
    - Added `HasModdedDirectory => !string.IsNullOrWhiteSpace(ModdedDirectory)`.
    - Updated `DetectedLegacy` setter to reset `ModdedDirectory = ""` when set to null.
    - Updated `ModdedDirectory` setter to fire `OnPropertyChanged(nameof(HasModdedDirectory))`.
  - In `src/Jamm.Gui/MainWindow.axaml`:
    - Wrapped the modded copy block in `<StackPanel IsVisible="{Binding HasModdedDirectory}" Spacing="2">`.
    - Added the explanatory text block directly below the path:
      `"JAMM installs mods into this copy; your original game stays untouched."` (using muted text styling).
  - Result: the entire block remains hidden until a valid game is selected, and when visible, immediately reassures the user that their original game install will remain pristine.

---

## Automated Test Coverage

Added comprehensive tests in `tests/Jamm.Gui.Tests/NewcomerUxTests.cs`:
1. `WindowSizeCalculationClampsToScreenWorkingAreaAndCenters`:
   - Validates clamping and centering across diverse screen profiles: 1280x800, oversized 1320x860, 1280x720, 1280x680 (with 40px taskbar), 1920x1080 (1080p), 2560x1440 at 2.0x scaling, and 1024x600 small displays.
2. `MainWindowHeadersAndButtonsDoNotOverflowAt720p`:
   - Headless Avalonia test rendering `MainWindow` at 1200x720 in both Simple and Advanced modes.
   - Verifies that both the "Settings" button and the "Hotkeys" button are effectively visible and their horizontal bounds fit strictly within the window width (`pos.X + width <= window.Bounds.Width`).
3. `SetupDialogActionButtonsStayInFixedFooterOutsideScrollViewer`:
   - Verifies in the visual tree that setup dialog action buttons (`Use this`, `Choose folder…`, `Check again`, `Cancel setup`, `Done`, `Skip`, `← Back to game folder`) reside inside the fixed footer `Border` on Grid Row 1, and are not descendants of the `ScrollViewer`.
4. `OtherFoldersDropdownHiddenWhenEmptyAndShowsNeutralCountLabel`:
   - Verifies the `Expander` is hidden (`IsVisible == false`, `IsEffectivelyVisible == false`) when `OtherGames` has 0 items.
   - Verifies singular neutral header `"Show 1 other folder that was found (not usable)"` when 1 item is added.
   - Verifies plural neutral header `"Show 3 other folders that were found (not usable)"` when 3 items are added.
   - Verifies it hides again when `OtherGames.Clear()` is called.
5. `SeparateModdedCopyHiddenUntilGameSelectedWithExplanation`:
   - Verifies that `"Separate modded copy:"` and `"JAMM installs mods into this copy; your original game stays untouched."` are hidden before a game is selected.
   - Verifies both are visible along with the `-modded` path once `DetectedLegacy` is assigned.
   - Verifies re-hiding when `DetectedLegacy` is cleared to null.

---

## Verification & Build Results

- **Compiler warnings**:
  - `dotnet build -c Release`: **0 Warning(s), 0 Error(s)** across all projects (`Jamm.Core`, `Jamm.Cli`, `Jamm.Gui`, `Jamm.ModMaker`, `Jamm.Core.Tests`, `Jamm.Gui.Tests`).
- **Unit test suite**:
  - `dotnet test`:
    - `Jamm.Gui.Tests`: **117 passed, 0 failed, 1 skipped** (real-file test skipped when `JAMM_ORIGINAL` unset).
    - `Jamm.Core.Tests`: **314 passed, 0 failed, 7 skipped** (real-file tests skipped when `JAMM_ORIGINAL` unset).
    - Total: **431 passed tests**.
- **Git rules**:
  - Clean working copy; no git commit, push, or branch modification performed.
