# Phase 7 — Play with Flatpak Steam (implementer: Codex)

Read AGENTS.md first. Work in `src/Jamm.Core` (GameLauncher, ManagerModels/config), `src/Jamm.Cli`,
`src/Jamm.Gui` (settings only if needed) and tests. Do not commit, push or change git config.

## Background (verified by the orchestrator on the user's machine)
- Steam is installed as Flatpak `com.valvesoftware.Steam`; its data dir is
  `~/.var/app/com.valvesoftware.Steam/.local/share/Steam` (Proton in `steamapps/common/Proton - Experimental`).
- Running Proton from the host fails: the game's lsteamclient cannot reach the sandboxed Steam client
  (`Assertion failed ... steamclient_main.c ... !status`).
- Running it in a second Flatpak instance (`flatpak run --command=python3 ...`) fails too: that instance has
  its own PID namespace, Steam drops the client pipe ("fatal stalled cross-thread pipe").
- What works: `flatpak enter <pid of the running Steam instance> env -i <environment of Steam's steam.sh
  process> STEAM_COMPAT_DATA_PATH=... STEAM_COMPAT_CLIENT_INSTALL_PATH=... SteamAppId=343780
  SteamGameId=343780 python3 "<proton>" run "<game>/Cat Goes Fishing.exe"`. The Steam instance pid is the
  lowest `flatpak ps --columns=pid,application` entry for com.valvesoftware.Steam; the environment is read
  from `/proc/<pid>` -> child bwrap -> its first child (steam.sh) `environ`. `flatpak enter` starts with an
  empty environment otherwise (no HOME/DISPLAY -> fontconfig errors, game never shows).
- The compat data path must be inside the sandbox's visible filesystem (e.g. under
  `~/.var/app/com.valvesoftware.Steam/.local/share/...`); `/run/media` is visible, `~/.local/share/cgfmm` is not.
- Steam must already be running; otherwise give a clear message ("Start Steam first, then press Play").
- The current workaround is a shell wrapper at `~/.local/share/cgfmm/proton-flatpak/proton` set as `proton.path`.

## Goal
1. New runner mode `wine.mode = proton-flatpak` (config + CLI `config set` + GUI settings): auto-detect the
   Flatpak Steam data dir and Proton, default compat data path inside the Flatpak home, launch exactly as
   above from C# (Process with ArgumentList, no shell), clear errors for: flatpak missing, Steam not
   running, Proton missing, game folder not visible inside the sandbox.
2. Keep `wine` and `proton` modes unchanged. Unit tests for command construction (no flatpak needed: inject
   process/proc readers).
3. Docs: README section for Flatpak Steam.

## Rules
0 warnings, tests pass (`dotnet test`; if the sandbox blocks the test host, say so — the orchestrator runs
them on the VPS), no game data committed, clear user-facing errors (AGENTS.md rule 3).
Report in `docs/reports/phase7.md`.
