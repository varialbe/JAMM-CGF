# Phase 10 — Player README and per-user installers

Implemented on `docs/readme`. No commits, pushes, Git configuration changes,
GUI-view edits, tool edits or game-file operations were performed.

## Delivered

- Replaced the long README with supported-game requirements, condensed Steam
  console download steps, a brief feature list, one install command per OS,
  example-mod descriptions, documentation links and GPL-3.0 attribution.
- Moved detailed GUI/CLI workflows, profiles, conflict review, recovery, build
  instructions and runner configuration to `docs/user-guide.md`. Added installer
  locations, update/removal behavior and launch/performance troubleshooting.
- Added `docs/example-mods.md`: actual ModMaker commands, HD Lite versus 2x
  textures, export/upscale workflow, extra Python/Vulkan requirements, F7/F8/F11,
  supplied orchestrator measurements and experimental mode's measured slowdown.
- Repaired CLI/legacy-guide links formerly pointing to removed README sections,
  and corrected the runner-mode table in `docs/data-layout.md`.
- Added `scripts/install.sh` and `scripts/install.ps1`. Both detect a .NET 10 SDK,
  otherwise install it per user with Microsoft's official installer; clone `main`
  with recursive pinned submodules; build Release; and create launchers/menu
  entries. Existing checkouts must have the expected origin, be clean and on
  `main`; updates use fast-forward-only pull and recursive submodule update.
  Failures stop before new launchers are generated. No root/admin is requested.
- Windows explicitly requires Git, the task's allowed fallback, rather than
  pretending a main-branch ZIP contains pinned recursive submodule sources.
- Uninstall removes the two launchers and desktop/Start-menu entry. It deliberately
  retains source/builds, shared .NET, managed state, game folders and backups;
  the guide explains manual source cleanup and restoring before removal.

## Accuracy review

Checked the retained workflows against the CLI, management/launch code, GUI
project output names, ModMaker entry point/controllers/upscale helper and prior
Phase 6 reports. The current launch service has only Wine, Proton and
`proton-flatpak` modes. Native Windows Play is not implemented, so the README
and guide explicitly direct Windows players to launch the installed legacy game
manually. Windows desktop behavior still needs platform validation.

F7 cycles Smooth's 60/120/off targets, F8 persists experimental Perf mode (fresh
configuration defaults off), and F11 toggles HD fullscreen. HD Lite is the existing
`hd` command without `--upscaled-pages`, not a nonexistent separate command.
The 112–127 FPS, >8 GB and HD Lite 47–52 FPS/~4.5 GB results are attributed to the
orchestrator measurements in the Phase 10 task, not claimed as new local tests.
The Mode 1 slowdown is independently documented in Phase 6c's measured follow-up.
Verification is described as static validation, not a gameplay guarantee.

## Validation

- `bash -n scripts/install.sh`: passed.
- `git diff --check`: passed.
- Checked all relative file links and Markdown heading anchors in README,
  user-guide, example-mods, CLI, legacy-version and data-layout: passed.
- Temporary Linux command doubles (outside the repository) exercised initial
  clone/build/launcher generation; clean repeat update; both launchers with
  spaces in home paths and arguments; refusal of a dirty checkout; invalid
  arguments; repeat uninstall; and retained source. All passed.
- A separate command-double run exercised the missing-SDK download/install path
  and launch through `~/.dotnet/dotnet` when dotnet was absent from PATH: passed.
  These are orchestration checks, not real network/SDK/desktop installation tests.
- ShellCheck and `pwsh` are unavailable here. PowerShell received manual review;
  no PowerShell parser check or Windows execution is claimed.
- `dotnet build -c Release`: blocked by NU1900 because NuGet's configured HTTP
  cache under the home directory is read-only in this environment. A retry with
  `NUGET_HTTP_CACHE_PATH` still encountered that configured cache location.
- `dotnet build -c Release -p:NuGetAudit=false`: passed, **0 warnings, 0 errors**,
  using the same environment workaround recorded in earlier phase reports.
  The committed installers retain normal audit-enabled Release builds.

## Remaining external validation

The orchestrator's real Linux VPS/desktop install runs and a Windows PowerShell
5.1/desktop run remain necessary. No installer was run against the user's real
home or game directories. Public copy/paste URLs target `main`; these new scripts
become available there when the orchestrator merges the branch. `.exe` and `.deb`
packaging remain planned, as stated in the README.
