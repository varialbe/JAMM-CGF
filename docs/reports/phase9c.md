# Phase 9c — first-run setup and resumed mod actions

Implemented on `ui/browse-mods`, 16 November 2026. No questions or user decisions.
No commits, pushes, git configuration changes, tools changes, submodule source
changes, or writes to real game inputs were made.

## What the player sees on first start

With a fresh `CGFMM_DATA_DIR`, JAMM opens **Set up your legacy game** automatically.
It shows search progress while a worker checks standard Steam installations,
registered Steam libraries and depot downloads, then mounted drives and the home
folder. **Cancel setup** cancels the search and dismisses setup; **Choose folder…**
and **Check again** are available once the search is idle. No restart is needed.

Each result shows its full archive path, a check/cross and the inspector's reason:
**✓ Legacy game found** or **✗ Current game found; download the legacy depot to
use mods.** Unreadable/invalid archives show the inspection failure. A usable
match is selected automatically, preferring a path containing manifest
`9039707068725406600` over other usable matches. Current builds cannot enable
**Use this**.

For the reported machine, the mounted-drive scan includes
`/run/media/varialbe/Festplatte 2`, independently of Flatpak's library VDF. The
expected results include the current Steam copy and the moved legacy folder:

```
/run/media/varialbe/Festplatte 2/Games/CatGoesFishing-legacy/2023-08-16_manifest-9039707068725406600/data.win
```

The latter should be selected as usable. This expectation is covered with the
same external-drive directory shape using synthetic archives; it is not a claim
that the real desktop was exercised during this implementation.

The panel displays the separate destination before any copy is made. For that
legacy folder the default is:

```
/run/media/varialbe/Festplatte 2/Games/CatGoesFishing-legacy/2023-08-16_manifest-9039707068725406600-modded
```

**Advanced mode** exposes the editable destination. **Use this** copies the whole
game there, skips filesystem links, retains an independent hash-verified base in
manager `base/<sha256>.win`, configures the destination, and creates/selects the
`default` profile. The original stays a read-only input. Existing destinations,
the original itself, and destinations nested in the original (including symlink
aliases) are refused with a recovery instruction.

Runner setup chooses `proton-flatpak` for detected Flatpak Steam, `proton` for
native Steam, and `direct` on Windows. Installed Proton scripts are found in
Steam common folders, VDF libraries and compatibility-tool folders, preferring
Experimental. If Proton is absent, setup still permits importing mods; the status
explains how to install Proton through Steam and choose its folder in Settings
before Play. Setup does not download a runner or change Flatpak permissions.

If no usable legacy build exists, the panel explains how to open Steam's console
(`steam://open/console`) and displays the command with a copy button:

```
download_depot 343780 343782 9039707068725406600
```

After downloading, the player clicks **Check again** or **Choose folder…**.
The folder picker requires the complete folder with `Cat Goes Fishing.exe` and
validates `data.win` with the same inspector. Selecting YYC shows a clear error
and leaves **Use this** unavailable.

Clicking **Add mods** without a usable base opens setup before the file picker.
After **Use this**, the picker opens automatically. Dragged/already-selected files
are retained until setup succeeds. **Download** (including the advanced install
action) also opens setup and retains the exact requested catalog mod/file; after
**Use this**, it imports and enables that file in the default profile. Cancelling
setup discards the deferred action. Missing, deleted or YYC configured bases also
route through setup instead of reaching the import precondition error.

Failures from importing or downloading now reach the main status line, activity
log and an owned error dialog. Messages identify the local path or online mod and
source, explain the failure, and suggest retrying, choosing a supported file, or
using a local `.win`/`.zip`. Failed/ambiguous card downloads also give an explicit
next step instead of silently returning.

## Implementation and bounds

- `GameFolderSearch` adds breadth-first scanning: maximum depth 8, 30,000 queued/
  visited folders and a 25-second cooperative deadline for the recursive scan.
  Windows scans fixed roots, Program Files and home; Linux scans home and children
  of `/run/media/$USER`, `/media/$USER` and `/mnt`; `/Volumes` is included on macOS.
  Steam's existing standard/VDF discovery is retained before this bounded scan.
- Hidden/cache/system/build trees are skipped. Directory links are skipped,
  paths deduplicated, permission failures tolerated, and cancellation checked
  during traversal and archive reads. The scanner never writes. Like ordinary
  file I/O, an in-flight OS call is not forcibly terminated by the deadline.
- `GameFileLoader` accepts an optional cancellation token and uses a read-only
  stream that checks it during hashing/parsing. Existing callers keep their API
  behavior. Inspector cancellation is preserved rather than classified as an
  invalid game.
- `MainViewModel.Setup.cs` owns setup state and deferred actions. Setup and imports
  share the existing operation guard. Search is injectable so ordinary GUI tests
  use fake roots/results and do not inspect the real desktop.
- Core setup retains copy/input guards and creates the default profile. Windows
  `direct` is a persisted runner option and launches the executable without Wine.
- README/data-layout documentation describe the new destination and recovery flow.

## Validation

Commands (NuGet auditing remains enabled):

```sh
NUGET_HTTP_CACHE_PATH=/tmp/jamm-nuget-http dotnet build -c Release
JAMM_FIRSTRUN_SCREENSHOT=/tmp/jamm9c-firstrun.png NUGET_HTTP_CACHE_PATH=/tmp/jamm-nuget-http dotnet test
```

Release build: **0 warnings, 0 errors**.
Core: **236 passed, 5 skipped**. GUI: **57 passed, 1 skipped**.
Overall: **293 passed, 6 skipped, 0 failures**. Real-file tests remain opt-in;
the pre-existing live-network test's body also remains opt-in.
`git diff --check` is clean.

New coverage exercises moved-depot versus current-build fixtures, hidden folders,
a symlink loop, depth/time/directory bounds, cancellation, recommended-manifest
selection, unusable configured bases, manual YYC rejection, Add and Download
setup/resume, cancellation discarding an action, default profile/enablement,
source hash preservation, destination aliases/existing-copy guards, native/
Flatpak/Windows runner selection, and Proton in a secondary VDF library.
The former missing-base error expectation now asserts setup recovery instead.

An Avalonia Headless render of the setup panel was inspected at
`/tmp/jamm9c-firstrun.png` (temporary synthetic content, not a repository asset).
The window test also checks that a failed local import produces the same message
in the status line and owned dialog. The failed-download regression checks the
same error notification plus the source URL and retry guidance.

## Orchestrator desktop check

Physical folder-picking, clipboard access, mounted-drive search timing, Flatpak
visibility, runner launch and real gameplay remain the orchestrator's live check,
as requested. Start with a fresh data directory, confirm the moved August 16
folder is selected, inspect the sibling destination, press **Use this**, then
exercise Add mods and Download. To check deferred actions specifically, dismiss
initial setup first and initiate each action from an unconfigured fresh instance.
Verify that cancelling setup leaves it unperformed. The real original should
retain its hash throughout. No real downloads, installs or game launches were
performed for phase 9c here.
