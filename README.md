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

Own the game on Steam, open `steam://open/console`, then enter:

```text
download_depot 343780 343782 9039707068725406600
```

Wait for “Depot download complete”. Keep the downloaded folder as your untouched
original and copy the **complete legacy game folder** for modded play, including
its executable and required shared files. [Download details](docs/legacy-version.md).

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

Open **JAMM-CGF** from the applications/Start menu, configure the clean base and
modded game folder in Settings, add mods, then create a profile and Check/Install.
A Windows `.exe` installer and an Ubuntu `.deb` package are planned.

## Example mods

Built locally from your own game with `tools/Jamm.ModMaker`:

- **Smooth** — targets 60/120 FPS rendering while keeping simulation at 30 ticks/s.
- **HD / HD Lite** — fullscreen improvements, with optional 2x textures (Lite keeps original textures).
- **Perf** — targeted optimizations and an optional performance mode, off by default.

[Build examples, keys and performance results](docs/example-mods.md).

[User guide](docs/user-guide.md) · [CLI reference](docs/cli.md) ·
[Conflicts](docs/conflicts.md) · [Build from source](docs/user-guide.md#build-from-source)

Licensed under [GPL-3.0](LICENSE) because JAMM links
[UndertaleModLib](https://github.com/UnderminersTeam/UndertaleModTool).
Game and mod files are not included.
