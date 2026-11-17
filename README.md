# JAMM-CGF

Combine Cat Goes Fishing mods into one verified `data.win`, review conflicts,
save profiles and load order, then install, restore or play from the desktop app.
Play currently uses Wine/Proton; on Windows, launch the legacy game manually after Install.

## Supported game

**Legacy 2023 Cat Goes Fishing only** (GameMaker 1.4 bytecode), with its matching
executable. Import `.win` mods or `.zip` files containing exactly one `data.win`.
The manager targets Windows and Linux; Linux supports Wine, Proton and Flatpak
Steam through `proton-flatpak`. The current Steam build uses YYC and cannot be
modded with JAMM.

You must own the game on Steam. On Linux, the installer finds the legacy build or
helps download it (`install.sh --get-game`). To open the Steam console manually:

- **Windows:** Press `Win+R` and run `steam://open/console` (or start Steam with `-console`).
- **Linux (native / apt):** `steam steam://open/console`
- **Linux (Flatpak):** `flatpak run com.valvesoftware.Steam steam://open/console`
- **Linux (Snap):** `snap run steam steam://open/console`
- Or restart Steam with the `-console` flag and open the **Console** tab.

Then enter in the Steam Console tab:

```text
download_depot 343780 343782 9039707068725406600
```

Wait for “Depot download complete” (on Linux, current Steam clients save this under
`<Steam>/steamrt64/steamapps/content/app_343780/depot_343782/`). Keep the downloaded
folder as your untouched original and copy the **complete legacy game folder** for
modded play, including its executable and required shared files. [Download details](docs/legacy-version.md).

## Install

These scripts download source and build it for your user account. Git is required
on both systems; Linux also needs curl. They install the .NET 10 SDK if missing.
Re-run to update. [Setup, removal and troubleshooting](docs/user-guide.md).

**Linux** — paste into a terminal:

```sh
curl -fsSL https://raw.githubusercontent.com/varialbe/JAMM-CGF/main/scripts/install.sh -o /tmp/jamm-install.sh && bash /tmp/jamm-install.sh
```

**Windows** — paste into PowerShell (5.1 or newer):

```powershell
Invoke-WebRequest https://raw.githubusercontent.com/varialbe/JAMM-CGF/main/scripts/install.ps1 -UseBasicParsing -OutFile "$env:TEMP\jamm-install.ps1"; if ($?) { powershell -NoProfile -ExecutionPolicy Bypass -File "$env:TEMP\jamm-install.ps1" }
```

Open **JAMM-CGF** from the applications/Start menu. First-run setup finds your legacy
game (Steam libraries, moved depot downloads, other drives) and creates a separate
modded copy with one click. Then add mods or download them in **Browse mods**, switch
them on or off, and press **Play**.
Packaged builds need no system .NET: install the Ubuntu amd64 `.deb` with
`sudo apt install ./jamm-cgf_<version>_amd64.deb`, or extract the Windows x64
portable zip and run `cgfmm-gui.exe`. Windows CI also builds an `.exe` installer.
For the headless CLI/scanner, build with `docker build -t jamm-cgf:local .`.
[Package builds, installation and Docker examples](docs/packaging.md).

## Browse mods

Download mods from the [Litterbox](https://litterbox.fyi) catalog inside the app. Each
card shows what the mod adds (counted from the file, not copied from its description)
and a compatibility icon: green check, yellow question mark or red cross, with the
reason one click away. [More about browsing and the shared compatibility results](docs/user-guide.md#browse-mods).

## Built-in mods

After base setup, toggle **JAMM Perf**, **JAMM Smooth**, **JAMM HD Lite** or
**JAMM Console** in the **Built-in (JAMM)** group. JAMM generates them locally,
caches them for your base and enables them in your profile. Then Check/Install or
Play. No terminal or SDK is needed.

Hotkeys: **F8** Perf, **F7** Smooth, **F11** HD Lite, **F12/backtick** Console.
Console commands: `help`, `unlockall`, `unlockgear`, `maxlevel`, `level <n>`, `catalog all`/`unlockfish`, `money <n>`/`+<n>`, `clear`, `close`/`exit` (the next save keeps changes).
CLI: `cgfmm builtin list` / `cgfmm builtin add console` (also `perf`, `smooth`, `hd-lite`).
[Controls and developer ModMaker variants](docs/example-mods.md).

[User guide](docs/user-guide.md) · [CLI reference](docs/cli.md) ·
[Conflicts](docs/conflicts.md) · [Build from source](docs/user-guide.md#build-from-source)

Licensed under [GPL-3.0](LICENSE) because JAMM links
[UndertaleModLib](https://github.com/UnderminersTeam/UndertaleModTool).
Game and mod files are not included.
