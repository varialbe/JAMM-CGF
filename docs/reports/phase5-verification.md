# Phase 5 verification

Implemented on `experimental` on 16 November 2026 with .NET SDK 10.0.404.
No commit, push, release, GitHub setting or Git configuration change was made.
The UndertaleModTool submodule remains unchanged. No game process was launched.

## Implemented

- `cgfmm-gui`: Avalonia desktop application targeting .NET 10, using Fluent and
  CommunityToolkit.Mvvm, with OS light/dark theme selection. Versions are pinned to
  [Avalonia 11.3.23](https://www.nuget.org/packages/Avalonia/11.3.23) (latest stable
  11.x at implementation) and [MVVM Toolkit 8.4.2](https://www.nuget.org/packages/CommunityToolkit.Mvvm/8.4.2).
- First-run/settings dialog with native folder/file pickers, immediate read-only
  legacy validation, legacy download guide link and all existing Wine/Proton keys.
  GEN8's stored `1.0.0.9999` is accepted using detected GameMaker 1 format and
  bytecode 16, rather than incorrectly requiring a literal stored `1.4` version.
- Local mod library with file sizes, compatibility/cache status, multi-file button
  imports and file drops. A failed import is reported while other imports can
  continue. Removal asks for confirmation and retains Core's profile safeguards.
- Persistent profile creation, switching, renaming, deletion, checkboxes and load
  order. Mouse dragging and keyboard up/down buttons save the same order.
  Optional `disabledMods` extends existing profile JSON; CLI-created profiles
  remain fully enabled. Changing order or enablement clears old choices.
- CONFLICT/AUTO/INFO/WARNING findings and counts, explanations and persistent
  choices. Individual code hunks offer participating mods and `keep-both` for
  insertions. Read-only, monospace Details displays the local merge with markers.
- Check/Install/Play/Restore bar, recognized game/profile status, collapsible
  activity log and Copy log. Unknown installed archives are refused, including
  when ViewModel methods are called directly. Busy operations disable writes.
- All game writes use Core's existing verified build/install/restore services.
  Installation builds/reloads/verifies before showing the exact destination and
  both retained backup paths for confirmation. Declining or cancelling discards
  the temporary build. Atomic installation/restore stages cannot be cancelled.
- Cancellation tokens and safe checkpoints for imports, checks, builds and
  verification; archive operations run off the UI thread. An underlying UTML
  load/fingerprint or compiler batch can finish before cancellation takes effect.
- If first-run setup uses the game's own archive as the base, successful GUI
  installation selects the already verified manager backup as the independent
  ancestor for subsequent checks/restores. CLI's explicit base behavior is kept.
- Per-hunk Core resolutions use `code/<entry>#<zero-based ancestor line>`.
  Insertion unions and replacement winners work together in one entry; per-hunk
  choices override whole-entry choices. Whole-entry forms still work, unknown keys
  fail validation, and `keep-both` on a replacement is rejected. CLI persistence
  and GUI both use the same Core validation/build plan.
- README rewritten with GUI and six-step CLI starts, screenshots, safety, FAQ,
  command links, credits, current roadmap and packaging instructions.

## Verification

The Release build completes with **zero warnings and zero errors** in our projects.
The pinned dependency prints its pre-existing package-readme notice; it is not a
compiler warning and the submodule was not edited.

| Run | Passed | Skipped | Failed |
| --- | ---: | ---: | ---: |
| Release suite without `JAMM_ORIGINAL` / `JAMM_MODS_DIR` | 198 | 6 | 0 |
| Release suite with both variables | 204 | 0 | 0 |
| Final GUI suite, including screenshots and real install/restore | 14 | 0 | 0 |

The full real-file run took 7 min 19 s for Core and 2 min 10 s for the GUI,
with those assemblies running concurrently. The final GUI run after the import/status
refinements passed all 14 tests in 1 min 31 s. The final synthetic suite passed
185 Core tests and 13 GUI tests, with the six real-file tests skipped as intended.
Commands used `dotnet test -c Release` / `--no-build` after a Release build.

Headless tests open the main window with empty and configured temporary stores
without a display. A pointer down/move/up test drags an actual rendered load-order
row and checks both the displayed order and its saved profile JSON.
ViewModel tests cover imports (including a failed file in a multi-file selection),
removal safeguards, order, profile switches/rename/delete, saved hunk choices,
busy states, errors, legacy/YYC validation, unknown-file refusal, cancellation at
confirmation, declining installation, verified backups/restore, default base
selection, installed-profile identification and atomic settings updates.
Core/CLI regressions cover mixed insertion/replacement hunk choices and cancellation
between temporary archive serialization and publication.

The real GUI test copies the **complete legacy game folder** to a temporary directory,
imports The Abyss through the ViewModel, checks the profile, invokes the GUI's
Install method with its concrete confirmation, recognizes the installed profile,
then calls Restore. The restored archive and retained original backup both match:

```text
SHA-256 dbb2e5c4b788358d0c6465d3005b2e9d46c4c540e423b064d7a5a3629286d2c1
```

The source original and mod hashes are asserted unchanged by the test. Independent
before/after checks also cover both supplied legacy builds (15 and 16 August) and
all three supplied mod archives. Tests write only temporary copies and manager data.
Existing real-file tests continue to cover each mod, both three-mod build orders,
archive/reference/GML verification, patch caches and management restore safeguards.

Both self-contained publish commands succeed:

```sh
dotnet publish src/Jamm.Gui -c Release -r linux-x64 --self-contained
dotnet publish src/Jamm.Gui -c Release -r win-x64 --self-contained
```

Published outputs remain in ignored `bin/` directories.

## Screenshot inspection

[Dark hero](../images/gui-hero.png) and [light conflict view](../images/gui-conflicts.png)
are generated by the opt-in Avalonia.Headless + Skia test described in
[building.md](../building.md#desktop-gui-builds-and-verification). Both are 1320 × 920.
They show three invented Example Mods, the `weekend-fishing` profile, enablement,
a `keep-both` insertion choice, an automatic field union and an identical-change
finding. All cards and install controls fit; text is legible in both themes.
Only synthetic archives and neutral demo rows are used: no game pixels, extracted
code or personal paths appear in either image.

## What remains for the desktop

- Native file/folder picker behavior, external file drops, clipboard, monitor
  scaling, keyboard navigation and OS theme transitions on Linux and Windows.
  Load-order dragging is verified headlessly but still merits a desktop check.
- Actual Wine/Proton launch and gameplay using the legacy executable. Launch
  construction/prerequisites are tested; no desktop game was started here.
  Play currently uses the shared Wine/Proton settings; native Windows game launch
  is not a separate supported runner mode.
- In-game fish/boat catalogs and texture appearance. `keep-both` keeps code
  statements; it does not renumber shared gameplay slots. Existing dynamic-ID and
  inherited texture warnings still apply.
- Distributable release packaging/signing/installer work remains on the roadmap.
  Self-contained folders publish successfully; no binaries are committed.
