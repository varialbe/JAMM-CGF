# JAMM-CGF

Combine Cat Goes Fishing mods into one verified `data.win`, review conflicts,
save profiles and load order, then install, restore or play from the desktop app
(Linux uses Proton/Wine, Windows launches directly).

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
modded copy with one click. Choose optional enhancements in the **Built-in (JAMM)**
group (Perf, Smooth, HD Lite, Console), add mods or download them in **Browse mods**,
switch them on or off, and press **Play**.
Packaged builds need no system .NET: install the Ubuntu amd64 `.deb` with
`sudo apt install ./jamm-cgf_<version>_amd64.deb`, or extract the Windows x64
portable zip and run `cgfmm-gui.exe`. Windows CI also builds an `.exe` installer.
For the headless CLI/scanner, build with `docker build -t jamm-cgf:local .`.
[Package builds, installation and Docker examples](docs/packaging.md).

## Hotkeys & console

Built-in enhancements provide keyboard shortcuts and an in-game console. Single source of truth: `BuiltinMods.Hotkeys` (also printed via `cgfmm builtin list`):

- **Hotkeys:**
  - **F8**: Perf overlay (switches modes)
  - **F7**: Smooth (cycles 60/120/off)
  - **F11**: HD Lite fullscreen (toggles fullscreen)
  - **F12** or **`** (backtick): Console (opens in-game console)

- **Console commands:**
  - `help`: list commands
  - `unlockall`: unlock gear, max level, and full fish catalog
  - `unlockgear`: unlock equipment, upgrades, hats, HUD, nightfall
  - `maxlevel`: set level to max
  - `level <n>`: set level to n (0..max)
  - `catalog all`: mark all fish caught (alias: `unlockfish`)
  - `money <n>`: set dollars (0..999999999); `money +<n>` adds
  - `clear`: clear console log
  - `close` or `exit`: close console

> [!NOTE]
> The next autosave (or opening the Esc save menu) keeps console changes in your save file.

## Browse mods

Download mods from the [Litterbox](https://litterbox.fyi) catalog inside the app. Each
card shows what the mod adds (counted from the file, not copied from its description)
and a compatibility icon: green check, yellow question mark or red cross, with the
reason one click away. [More about browsing and the shared compatibility results](docs/user-guide.md#browse-mods).

The CLI can check catalog-backed mods in the selected profile with `cgfmm update --check`
(`--json` emits machine-readable results; exit code 10 means updates are available). The catalog
update contract, including package SHA-256 and the optional `saveBreaking` flag, is documented in
[`docs/catalog-api.md`](docs/catalog-api.md).

## Built-in mods

After base setup (or in the first-run panel), toggle **JAMM Perf**, **JAMM Smooth**, **JAMM HD Lite** or
**JAMM Console** in the **Built-in (JAMM)** group. JAMM generates them locally,
caches them for your base and enables them in your profile. Then Check/Install or
Play. No terminal or SDK is needed.

Hotkeys and all console commands are documented in [Hotkeys & console](#hotkeys--console), the app's **Hotkeys** help button, the [User guide](docs/user-guide.md#built-in-mods), and [Example mods](docs/example-mods.md).
CLI: `cgfmm builtin list` (prints the mods, hotkeys, and console commands) / `cgfmm builtin add console` (also `perf`, `smooth`, `hd-lite`).

## Make your own mods

Add custom fish, boats, hats, and rod upgrades without UndertaleModTool or editing shared game files—just a folder with `jamm-mod.json` and a PNG sprite:

```json
{
  "id": "my-reef-fish", "name": "My Reef Fish",
  "fish": [{
    "key": "sunfin", "name": "Sunfin", "size": "small", "sprite": "art/sunfin.png",
    "spawn": { "x": [1100, 2400], "y": [1400, 1950], "count": 4 }
  }]
}
```

JAMM merges API and ModKit mods side-by-side with zero slot conflicts and saves progress by stable key. Start with **[Your First Fish in 10 Minutes](docs/modding/getting-started.md)**, explore the **[Modding Hub](docs/modding/README.md)**, or check the **[GML API Reference](docs/modding/api-reference.md)** for rods, lures, tools, and custom GML.

[User guide](docs/user-guide.md) · [CLI reference](docs/cli.md) ·
[Modding guide](docs/modding/README.md) · [Conflicts](docs/conflicts.md) ·
[Build from source](docs/user-guide.md#build-from-source)

Licensed under [GPL-3.0](LICENSE) because JAMM links
[UndertaleModLib](https://github.com/UnderminersTeam/UndertaleModTool).
Game and mod files are not included.
