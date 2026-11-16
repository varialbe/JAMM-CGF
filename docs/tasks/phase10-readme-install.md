# Phase 10 — Player-friendly README + one-step install scripts (implementer: Codex)

Read AGENTS.md first. Work only in this worktree (branch `docs/readme`). Do not commit, push or change
git config. Do not touch `src/Jamm.Gui` views or `tools/` (another Codex is building a "Browse mods" tab
on `ui/browse-mods`, which will be merged later).

## User request (German, verbatim)
"die readme is out of date und viel zu komplex für den average cat goes fishing spieler sie soll aktuelle
sein aber seperat als docs und die main README.md soll nur kurz kompakt sagen: was wird supported, wie holt
man die richtige version, was kann die software, lizenz, installation für windows und linux mit einem simplen
script (später für windows eif ne executable und für ubuntu ne deb"

## Deliverables
1. `README.md` (English, like the app) — short, for players, roughly one screen plus the install commands:
   - What is supported: Cat Goes Fishing **legacy 2023 build** only (GameMaker 1.4 bytecode); mods as `.win`
     or `.zip` with one `data.win`; Windows and Linux (Wine, Proton, Flatpak Steam via `proton-flatpak`).
     The current Steam build cannot be modded (YYC) — say so in one sentence.
   - How to get the right game version: the Steam console `download_depot 343780 343782 9039707068725406600`
     steps, condensed (link to docs/legacy-version.md for details).
   - What the software does: combine several mods into one verified `data.win`, explain conflicts, profiles
     and load order, one-click install/restore/play; example mods (Smooth 60/120 FPS, HD/HD Lite fullscreen,
     Perf) built with tools/Jamm.ModMaker — one line each, link to docs.
   - License: GPL-3.0 (one line, why: links UndertaleModLib).
   - Install: one copy-paste command per OS running the scripts below; note that a Windows .exe installer
     and an Ubuntu .deb package are planned.
   - Links to the detailed docs. No CLI reference, no internals in README.
2. Move and update the detailed content into `docs/` (keep it current with the code on this branch):
   `docs/user-guide.md` (GUI + CLI usage, settings incl. wine/proton/proton-flatpak, profiles, conflicts,
   restore, troubleshooting: screen lock throttles to 1 FPS, Steam must run for Play, VRAM note),
   `docs/example-mods.md` (ModMaker commands perf/smooth/hd/--upscaled-pages/upscale_pages.py, in-game keys
   F7/F8/F11, the orchestrator's measurements: base+HD+Perf+Smooth fullscreen 112-127 FPS at 30 ticks/s,
   all six mods with 2x textures in fullscreen exceed 8 GB VRAM -> use HD Lite: 47-52 FPS, 4.5 GB;
   performance mode measured slower, off by default), keep docs/cli.md, docs/legacy-version.md etc. and fix
   links. Check every statement against the current code/reports (docs/reports/*.md).
3. `scripts/install.sh` (Linux, no root, idempotent): installs the .NET 10 SDK to `~/.dotnet` via Microsoft's
   official `dotnet-install.sh` only if `dotnet` 10 is missing; gets the source (git clone/pull of
   https://github.com/varialbe/JAMM-CGF with `--recurse-submodules`, branch main) into
   `~/.local/share/jamm-cgf/src`; builds Release; puts launchers `cgfmm` and `cgfmm-gui` into `~/.local/bin`
   (they must work without a system-wide dotnet: use the installed dotnet path); adds a `.desktop` entry;
   clear messages; `--uninstall` option. Requires only git + curl (say so if missing).
4. `scripts/install.ps1` (Windows PowerShell 5.1+, no admin): same idea with `dotnet-install.ps1` into
   `%LOCALAPPDATA%\Microsoft\dotnet` (or the official default), source to `%LOCALAPPDATA%\jamm-cgf\src`
   (git if available, else download the main-branch zip via GitHub's archive URL — submodules then need
   their own archives at the pinned commits: resolve them from the repository's submodule SHAs; if that is not
   robustly possible, require git and say so), build, Start-menu shortcut, `-Uninstall`.
5. Validate: `bash -n` / shellcheck if available for install.sh; PowerShell syntax check if pwsh exists.
   The orchestrator runs install.sh end-to-end on the VPS and the desktop.
Report: `docs/reports/phase10.md`. Keep README honest: nothing it promises may be untrue today.
