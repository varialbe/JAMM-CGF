# Phase 9c — first run must work for real players (implementer: Codex)

Continue on `ui/browse-mods`. Same rules as 9b (no commits/pushes/git config, no tools/ changes).

## User report (German, verbatim)
"ich kann weder mods mit dem add mods manager hinzufügen noch funktionieren downloads"

## Orchestrator reproduction (user's real desktop, fresh CGFMM_DATA_DIR=/tmp/jamm-gui-firstrun)
- config.json after the user's attempts: `"base": null, "gameDir": null, "activeProfile": null`.
- Automatic detection found nothing usable. Facts on this machine:
  - Steam is Flatpak: `~/.var/app/com.valvesoftware.Steam/.local/share/Steam`, its library only has the
    CURRENT (YYC, unusable) build in `steamapps/common/Cat Goes Fishing`.
  - A second Steam library on an external drive: `/run/media/varialbe/Festplatte 2/Steam/steamapps/common/
    Cat Goes Fishing` (also current build) — it is NOT listed in the Flatpak libraryfolders.vdf.
  - The usable legacy build lives in a user-chosen folder:
    `/run/media/varialbe/Festplatte 2/Games/CatGoesFishing-legacy/2023-08-16_manifest-9039707068725406600/`
    (copied there after `download_depot`). Players commonly move the depot download like this.
- Result: without a base, "Add mods" and "Download" fail and the player is stuck.

## Requirements
1. Never let Add mods / Download fail because setup is missing: if no usable base is configured, open the
   setup flow first (and resume the action afterwards), with plain words.
2. Setup flow for normal players:
   - automatic search (as in 9b) PLUS a bounded scan of mounted drives / common roots for a folder containing
     `Cat Goes Fishing.exe` + `data.win` (Linux: /run/media/$USER/*, /media/$USER/*, /mnt/*, $HOME; Windows:
     all fixed drive roots, Program Files, Steam libraries; macOS if trivial). Depth- and time-limited, runs in
     the background with progress and Cancel, skips system dirs, never follows symlink loops, never writes.
     Classify each hit with the existing inspector (legacy bytecode = usable, YYC = not usable) and show the
     list with the check/cross icons; preselect the best legacy match (manifest 9039707068725406600 if found).
   - "Choose folder…" button as fallback (folder picker, validates the same way, clear error if YYC).
   - If only current builds exist: show the short legacy-depot instruction with a copy button for
     `download_depot 343780 343782 9039707068725406600` and a "Check again" button.
   - One click "Use this": sets base (read-only original) and creates the separate modded copy automatically
     (default next to the original, e.g. `<original>-modded`, editable in Advanced mode), selects the runner
     automatically (Flatpak Steam -> proton-flatpak with detected Proton; native Steam -> proton; Windows ->
     direct) and creates the default profile.
3. Error messages from Add mods / Download must state what failed and the next step (AGENTS.md rule 3);
   check the GUI shows them (status line + dialog), not only the log.
4. Tests: fake directory trees for the scan (incl. external-drive layout above, YYC vs legacy fixtures), the
   "setup first, then resume action" flow for Add mods and Download, runner auto-selection.
5. Live check on this desktop is done by the orchestrator; describe in docs/reports/phase9c.md exactly what the
   player will see on first start.
