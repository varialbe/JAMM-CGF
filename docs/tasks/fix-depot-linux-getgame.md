# Part 2: automatic legacy-depot detection + download helper for all Linux Steam installs

Builds on fix-depot-path-steamrt64.md (already implemented in this worktree; keep it).

## Facts (verified on a real machine, 2026-11-17)
- Flatpak Steam put `download_depot 343780 343782 9039707068725406600` output at
  `~/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamrt64/steamapps/content/app_343780/depot_343782/`.
- Reference hashes of that legacy depot (sha256):
  - `data.win`             dbb2e5c4b788358d0c6465d3005b2e9d46c4c540e423b064d7a5a3629286d2c1
  - `Cat Goes Fishing.exe` 06ab8d250bb0f82582da585eb0fe8e30eb4abba858180e7fff5fc5271090018e
- Typing `steam://open/console` into a shell does nothing. The README/GUI advice "open steam://open/console" is wrong/insufficient on Linux.
  What works on Linux: pass the URL to the Steam binary of the matching install
  (`steam steam://open/console`, `flatpak run com.valvesoftware.Steam steam://open/console`, `snap run steam steam://open/console`),
  or (re)start Steam with the `-console` flag, then use the "Console" tab.
- There is no way to inject console commands into Steam from outside; the user must paste the command. Do NOT add DepotDownloader or anything that asks for Steam credentials.

## Task
1. **Steam roots (C#)**: `SteamGameDetection.Roots` must cover apt/Debian (`~/.steam/debian-installation`), native (`~/.local/share/Steam`, `~/.steam/steam`, `~/.steam/root`), Flatpak (user `~/.var/app/com.valvesoftware.Steam/.local/share/Steam`), Snap (`~/snap/steam/common/.local/share/Steam`). For each root, depot candidates under `steamapps`, `steamrt64/steamapps`, `ubuntu12_32/steamapps`. Tests for every layout (fake dirs in temp).
2. **scripts/install.sh**: after the build, add a "legacy game" step (also callable alone via `install.sh --get-game`; update usage text):
   - Detect installed Steam variants: Flatpak (`flatpak info com.valvesoftware.Steam`, user or system), Snap (`snap list steam`), native/apt (`command -v steam`, or the roots above existing). Print what was found.
   - Search all roots × the three content layouts (+ `steamapps/common/Cat Goes Fishing`) for a folder with `data.win` + `Cat Goes Fishing.exe`; verify `data.win` sha256 against the reference. If found: print the path, say JAMM will detect it, done.
   - If not found and stdin is a TTY: explain briefly, copy the command to the clipboard if `wl-copy`, `xclip` or `xsel` exists, open the console with the variant-specific command above (if several variants, ask which; if none, explain that Steam must be installed and owned). Offer (y/N) to restart Steam with `-console` if the console tab did not appear (`<steam cmd> -shutdown`, wait, start with `-console` detached via setsid/nohup).
   - Then poll every 5 s (show a waiting line, Ctrl+C or Enter skips, timeout 30 min) until a verified depot appears; print its path.
   - Non-interactive (no TTY) or `--no-game`: only print the per-variant manual instructions. The step must never make the install fail.
   - Must stay `bash -n` clean and `set -euo pipefail` safe; keep the ERR trap from firing on expected non-zero checks.
3. **README.md + docs/legacy-version.md**: replace the `steam://open/console` advice with: "the Linux installer finds or helps download it (`install.sh --get-game`)", plus short manual instructions per OS/variant (Windows: Win+R `steam://open/console`; Linux: the commands above or `-console`). Mention the steamrt64 download path.
4. **GUI**: `MainViewModel.Setup.cs` SetupMessage — OS-specific text (Linux: run `install.sh --get-game` or the `-console` way). If simple, add an "Open Steam console" action on Linux that runs the variant-specific command (Process.Start, no shell); otherwise skip.
5. `dotnet test` for affected projects must pass; run `bash -n scripts/install.sh`. Do not commit.
