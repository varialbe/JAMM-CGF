# Phase 25 — GUI ModKit: First-Class ModKit Folder Support for Non-Developers

Branch: `exp/gui-modkit`.  
Implements first-class ModKit folder mod support in the JAMM-CGF Avalonia GUI, making creating, importing, inspecting, and rebuilding source-folder mods seamless for creators and non-developers alike.

---

## 1. Overview & Objectives

In Phase 22d, the ModKit package system, starter templates, sprite packaging, and compilation pipeline were introduced in `Jamm.Core` and the CLI (`cgfmm modkit`). However, using ModKit previously required CLI commands (`cgfmm modkit build` or `cgfmm add <folder>`).

This phase elevates ModKit folder mods into first-class citizens in the GUI:
1. **Add Dialog & Folder Import**: "+ Add mods" offers "Add mod folder (ModKit)…" alongside "Add mod files (.win, .zip)…". Selecting a folder with `jamm-mod.json` validates, builds against the configured base game, and imports it with UI progress reporting.
2. **Friendly Error Handling**: Validation failures (malformed JSON, missing files, dimension mismatches, path traversal) show human-readable remediation messages from `ModKitPackage` without raw stack traces.
3. **Drag & Drop**: Dropping a folder containing `jamm-mod.json` onto the window automatically triggers the ModKit folder import workflow.
4. **Badges & Metadata**: ModKit mods display a distinctive `"ModKit"` pill badge in both collapsed list rows and expanded details, showing the package's name, version, author, description, and source directory path.
5. **In-Place Rebuild**: ModKit mods feature a "Rebuild" action that recompiles the mod directly from its source folder after editing, preserving profile enablement. If the source folder was moved or deleted, clear explanations and folder re-selection prompts guide the user.
6. **Documentation Help Text**: The Add dialog and empty-state mod list feature guidance: `"Made your own mod with ModKit? Add its folder."` with a direct one-click launcher to `docs/modding/getting-started.md`.

---

## 2. Changes Implemented

### Jamm.Core (`src/Jamm.Core/Management/`)

- **`ManagerModels.cs`**:
  - Extended `ManagedMod` with XML-documented properties:
    - `string? SourceFolder`: Full path to the author's ModKit workspace on disk.
    - `string? Author`: Author name from `jamm-mod.json`.
    - `string? Version`: Semantic version from `jamm-mod.json`.
    - `bool IsModKit`: True when the mod was imported from or built with ModKit.
- **`ModLibrary.cs`**:
  - Updated `Add(string inputPath, ...)`:
    - When importing a directory, reads `jamm-mod.json` to extract `Author`, `Version`, and `Description`.
    - Persists `SourceFolder`, `IsModKit = true`, `Author`, `Version`, and `Description` into `mod.json` metadata.
    - On rebuild (when `stableId` is provided), preserves profile enablement, updates metadata fields in place, and writes the refreshed metadata.
  - Added `SetSourceFolder(string modIdOrName, string sourceFolder)`:
    - Allows updating or relinking the source folder path if a mod author moves their project folder.
  - Added `Rebuild(string modIdOrName, string? overrideSourceFolder = null, IProgress<BuildProgress>? progress = null, CancellationToken cancellationToken = default)`:
    - Validates that the source folder exists and contains `jamm-mod.json`.
    - Builds against `config.base` and replaces the existing mod in the library atomically using `stableId: mod.Id`.
    - Returns `ModLibraryAddResult` indicating success, warnings, or already-added status.

### Jamm.Gui (`src/Jamm.Gui/`)

- **`AddModDialog.cs`**:
  - Created a dedicated modal dialog presenting two primary choices:
    1. **"Add mod files (.win, .zip)…"**: Opens the multi-file picker for standalone `.win` and `.zip` archives.
    2. **"Add mod folder (ModKit)…"**: Opens the single-folder picker for ModKit project directories.
  - Features the guidance banner: `"Made your own mod with ModKit? Add its folder."` with a `"Getting started guide ↗"` button launching `docs/modding/getting-started.md`.
- **`ViewModels/ModRow.cs`**:
  - Added reactive properties:
    - `IsModKit`, `SourceFolder`, `PackageVersion`, `Author`, `Description`.
    - Computed display lines: `HasAuthor`, `HasDescription`, `ModKitVersionLine` (`"v1.0.0"`), `ModKitAuthorLine` (`"by Author"`), and `SourceFolderLine` (`"Source: /path/to/folder"`).
  - Implemented `INotifyPropertyChanged` notifications when metadata updates during rebuild.
- **`ViewModels/MainViewModel.cs`**:
  - `ReloadAsync`: Populates ModKit properties (`IsModKit`, `SourceFolder`, `PackageVersion`, `Author`, `Description`) when building `ModRow` items from library records.
  - `AddReadyAsync`: Intercepts `ModKitException`, `JsonException`, `ArgumentException`, and `FileNotFoundException` during folder builds. Formats clean, friendly status messages (file, JSON path, remediation steps) and dialogs without raw call stacks.
  - `RebuildModAsync(string modId, Func<Task<string?>>? promptFolder = null)`:
    - Executes folder validation and compilation off the UI thread with progress reporting.
    - If the saved `SourceFolder` is missing or deleted, explains clearly: `"Source folder no longer exists at <path>. Would you like to locate it?"` and prompts for the new directory.
    - If no source folder was previously linked, prompts the author to select their ModKit folder and saves the link via `SetSourceFolder`.
    - Automatically reloads the library and updates mod list UI on completion.
- **`ModListRow.axaml` & `ModListRow.axaml.cs`**:
  - Added a compact `"ModKit"` pill badge (`#1E3A8A` background, cyan foreground, rounded corner) in the collapsed row header next to the mod title.
  - In expanded details:
    - Displays `"ModKit Mod"` badge.
    - Shows version, author, description, and source folder path.
    - Added a `"Rebuild"` button styled with accent theme, wired to `MainWindow.RebuildModAsync`.
- **`MainWindow.axaml` & `MainWindow.axaml.cs`**:
  - Toolbar: "+ Add mods" opens `AddModDialog`; added adjacent "Add mod folder (ModKit)…" button for quick 1-click folder importing.
  - Empty State: When no mods are installed, displays ModKit guidance: `"Made your own mod with ModKit? Add its folder."` with a `"Getting started guide ↗"` action button.
  - Drag & Drop: Extended window drop handler to recognize dropped directories containing `jamm-mod.json` and route them through `AddFolderAsync`.
  - Advanced Library View: Added ModKit badge indicators and row/toolbar Rebuild buttons.
  - Documentation Launcher: `OpenModKitGuideAsync` locates `docs/modding/getting-started.md` relative to the application directory and opens it with the default system viewer, browser, or terminal reader.

---

## 3. Automated Test Coverage

### Jamm.Core.Tests (`tests/Jamm.Core.Tests/ModKitRebuildTests.cs`)
1. **`AddFolder_SetsModKitMetadataAndRebuildPreservesId`**:
   - Scaffolds a ModKit upgrade mod, adds it to `ModLibrary`, and verifies `IsModKit = true`, `SourceFolder`, `Version`, `Author`, and `Description` are persisted.
   - Enables the mod in a profile, edits `jamm-mod.json` (bumps version to 1.1.0 and changes author/name), and calls `Rebuild`.
   - Verifies mod ID remains identical, profile enablement is preserved, and metadata updates cleanly in `mod.json`.
2. **`Rebuild_MissingSourceFolder_ThrowsDirectoryNotFoundException`**:
   - Deletes the source folder and verifies `Rebuild` throws `DirectoryNotFoundException` with the missing path clearly identified.
3. **`Rebuild_InvalidManifest_ThrowsModKitException`**:
   - Corrupts `jamm-mod.json` and verifies `Rebuild` throws a structured `ModKitException` detailing the JSON error.
4. **`SetSourceFolder_UpdatesMetadata`**:
   - Verifies `SetSourceFolder` successfully updates the recorded source path on disk.

### Jamm.Gui.Tests (`tests/Jamm.Gui.Tests/ModKitGuiTests.cs`)
1. **`AddModDialog_PresentsBothOptionsAndModKitHelp`**:
   - Inspects `AddModDialog` layout: verifies presence of both file and folder buttons, help text `"Made your own mod with ModKit? Add its folder."`, and the guide launcher button.
2. **`AddFolderAsync_PopulatesModKitRowAndPersistsMetadata`**:
   - Runs `AddFolderAsync` with a scaffolded ModKit package; verifies resulting `ModRow` in `MainViewModel` displays `IsModKit = true`, correct version, author, description, and source folder.
3. **`AddFolderAsync_InvalidPackage_DisplaysFriendlyErrorMessageWithoutStackTrace`**:
   - Tests three error conditions: missing `jamm-mod.json`, invalid JSON syntax, and missing sprite PNG.
   - Asserts friendly messages explaining the issue and file paths are shown in `vm.Status` without stack traces (`at ... in ...`).
4. **`RebuildModAsync_EditsSourceFolder_RebuildsInPlace`**:
   - Edits package metadata in the source folder and invokes `vm.RebuildModAsync(mod.Id)`.
   - Confirms UI updates with the new name, version, and status `"Rebuilt <Name>"`.
5. **`RebuildModAsync_MissingFolder_ExplainsMissingFolderFriendly`**:
   - Deletes the source directory and runs `RebuildModAsync`.
   - Confirms friendly status explanation: `"Rebuild failed: Source folder no longer exists at ..."` without throwing raw exceptions.
6. **`RebuildModAsync_NoSavedFolder_PromptsForFolder`**:
   - Tests unlinked ModKit mod; verifies user prompt function is invoked to select a new folder and the rebuild proceeds.
7. **`MainWindow_RendersModKitBadgesAndEmptyStateGuide`**:
   - Headless Avalonia test rendering `MainWindow` with and without mods.
   - Confirms ModKit badge pills, Rebuild buttons, empty state help text, and guide launcher render properly in the visual tree.

---

## 4. Verification & Build Results

- **Compiler warnings**:
  - `dotnet build -c Release`: **0 Warning(s), 0 Error(s)** across all projects (`Jamm.Core`, `Jamm.Cli`, `Jamm.Gui`, `Jamm.ModMaker`, `Jamm.Core.Tests`, `Jamm.Gui.Tests`).
- **Test execution**:
  - `dotnet test`:
    - `Jamm.Gui.Tests`: **124 passed, 0 failed, 1 skipped** (1 real-file test skipped when `JAMM_ORIGINAL` unset).
    - `Jamm.Core.Tests`: **341 passed, 0 failed, 9 skipped** (9 real-file tests skipped when `JAMM_ORIGINAL` unset).
    - Total: **465 tests passing cleanly**.
- **Repository hygiene**:
  - No game files (`*.win`, extracted assets) created or committed.
  - Input files treated read-only.
  - Git working copy preserved without pushing.
