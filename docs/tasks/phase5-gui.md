# Task: Phase 5 — Avalonia GUI (mod manager) and a polished README

Read `AGENTS.md`, `README.md`, `docs/conflicts.md`, `docs/building.md`, `docs/data-layout.md` and the
Phase 2–4 code first. Branch: `experimental`. The CLI and Jamm.Core are finished and verified; the GUI is a
**front end over Jamm.Core**, it must not re-implement merge, build, install or backup logic.

## Goal
A non-developer can open one window, point it at their legacy game folder, drag in mods, order them,
see conflicts in plain language, pick winners, and press **Install** / **Play** / **Restore original**.

## Project
- New project `src/Jamm.Gui/` (Avalonia 11.x, latest stable 11 release, `net10.0`, MVVM with
  `CommunityToolkit.Mvvm`; no ReactiveUI). Executable name `cgfmm-gui`. Add it to the solution.
- Pin exact NuGet versions. Fluent theme, light and dark mode follow the OS.
- If some logic the GUI needs lives only in `Jamm.Cli` (e.g. config/profile/mod-store handling,
  apply orchestration), **move it into Jamm.Core** as a service with result objects and let both CLI and GUI
  use it. CLI behaviour and its tests must stay unchanged (all existing tests green).
- Long operations (patch computation, check, build, verify, install) run off the UI thread with progress text
  and a Cancel button where Jamm.Core supports cancellation (add `CancellationToken` parameters if missing).

## Screens
1. **First run / Settings**: choose legacy game folder (folder picker) and base `data.win` (defaults to the one in
   the game folder). Validate immediately: shows "Legacy bytecode build ✓" or a clear explanation if the file is
   YYC / a different version, with a link to `docs/legacy-version.md` on GitHub. Runner settings for Play
   (Proton path / Wine prefix) — same keys as `cgfmm config`.
2. **Mods** (main window, left): list of added mods with name, size, base compatibility badge, patch cached.
   Add via button and drag & drop (`.win` and `.zip`, same rules as `cgfmm add`). Remove with confirmation.
3. **Profile / load order** (main window, centre): checkboxes to enable mods, drag to reorder (and up/down
   buttons for keyboard users), profile selector (create / rename / delete). Explain "later mod wins" in one line.
4. **Conflicts** (main window, right or tab): result of `check` for the active profile, grouped
   CONFLICT / AUTO / INFO with counts. Each conflict row: asset, kind, the mods involved, a plain-language
   explanation, and a choice control (mod A / mod B / keep both where allowed) that writes the profile's resolve
   choice. "Details" opens a side-by-side or merged view (code with conflict markers, monospace, read-only).
5. **Install bar** (bottom): status of the game folder (original / cgfmm build of profile X / unknown),
   buttons **Check**, **Install**, **Play**, **Restore original**. Install shows a confirmation dialog stating
   exactly which file is replaced and where the backup is. After install, show the verify result.
6. **Log panel**: collapsible, shows the same messages the CLI would print; "Copy log" button.

## Safety (same rules as the CLI, test them)
- The GUI never writes a `data.win` itself; it calls the Phase 3/4 Jamm.Core install and restore code.
- Install and Restore are disabled while an operation runs. Refuse if the game folder's `data.win` is unknown
  (same check as the CLI) and say what to do.
- No game content is shown except code in the Details view for the user's own local files.

## Tests
- ViewModel unit tests (no UI): add/remove/reorder, profile switching, resolve choices persisted, button
  enablement while busy, error messages surfaced.
- `Avalonia.Headless.XUnit` smoke test: main window opens with an empty temp data dir and with a configured one.
- Headless screenshot generation (Avalonia.Headless + Skia) for the README: write PNGs to `docs/images/`
  using **fake/neutral demo data only** (made-up mod names like "Example Mod A", no game assets, no real paths).
  Add the generator as an opt-in test or a small `--screenshots <dir>` dev switch; document how to regenerate.

## README (rewrite, polished, GitHub landing page)
- Title, one-sentence pitch, badges (.NET 10, GPL-3.0, platform Linux/Windows), a hero screenshot of the GUI.
- "What it does" in 4 bullets; "Quick start" for the GUI (download/build → choose legacy game folder → add mods →
  Install → Play) and for the CLI (the 6-step quick start from Phase 4).
- The legacy-version requirement, prominently but short, linking to `docs/legacy-version.md`.
- Feature overview with conflict screenshot; safety section (backups, restore, never touches originals);
  CLI command reference (short table, link to detailed docs); FAQ; building from source; credits
  (UndertaleModTool, mod authors by mod name only), license.
- Roadmap table updated (Phases 0–5 done; next: in-game test feedback, packaging).
- Keep personal paths out (`$HOME/...` placeholders only).

## Packaging
- `dotnet publish src/Jamm.Gui -c Release -r linux-x64 --self-contained` and `-r win-x64` must succeed; document
  the commands in `docs/building.md`. Do not commit published binaries.

## Verify before finishing
- `dotnet build -c Release` zero warnings in our projects, `dotnet test` green with and without env vars.
- Headless smoke test passes on this machine (no display). Screenshots generated and look sane (describe them).
- Run the GUI's Install path once headlessly through the ViewModel against a temp copy of the legacy game folder
  (real files, skipped without env vars) and restore it; report that the original hash is back.
- Final message: what works, what does not, what the user must test on the desktop.
- Do NOT commit. No game content or personal paths in tracked files.

## Core fix needed by the Conflicts screen (from the Phase 4 review)
`profile resolve <p> <code entry> keep-both` is refused when the entry has list hunks *and* an overlapping
replacement hunk (e.g. the fish catalog entry). Support per-hunk resolutions (stable hunk keys such as
`code/<entry>#<ancestor line>`), let keep-both apply to the list hunks while other hunks take a mod choice, and
keep the current whole-entry form working. CLI and GUI both use it; add tests.
