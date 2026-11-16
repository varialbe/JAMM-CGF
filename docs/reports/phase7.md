# Phase 7 — Flatpak Steam

Implemented on `mods/7-flatpak`; no commits, pushes or git configuration changes.

## Changes

- Added `wine.mode = proton-flatpak` to persisted configuration validation, CLI
  configuration/help and GUI Settings. Wine and host Proton command construction
  and defaults remain unchanged.
- Added a shell-free Flatpak launcher. It selects the lowest Steam PID from
  `flatpak ps --columns=pid,application`, reads the child bwrap's first child's
  NUL-separated `/proc/.../environ`, and constructs:
  `flatpak enter <pid> env -i <Steam environment plus overrides> python3 <proton> run <game exe>`.
  Every argument uses `ProcessStartInfo.ArgumentList`. The recovered environment
  is omitted from the displayed command so Steam environment values are not logged.
- Defaults to `~/.var/app/com.valvesoftware.Steam/.local/share/Steam`, its
  `Proton - Experimental` installation, and its `steamapps/compatdata/<appId>`.
  Existing Proton path overrides remain supported. The compatibility directory
  is created only when launching, after validation.
- Checks game/Proton/Steam visibility using shell-free `flatpak enter ... test`
  queries before profile installation. Checks the existing compatibility directory
  or nearest existing host parent for sandbox visibility. Provides actionable
  errors for missing Flatpak, stopped Steam, unreadable process metadata,
  missing Proton and inaccessible paths. Discovery queries have a ten-second timeout.
- Added injectable process, proc and directory readers plus unit coverage for
  instance selection, environment parsing/overrides, default and explicit paths,
  argument boundaries, discovery failures and configuration persistence.
- Added README setup, migration from wrapper settings and CLI reference guidance.

## Validation

- Initial `dotnet build -c Release -m:1 --disable-build-servers` and
  `dotnet test -c Release -m:1 --disable-build-servers` were blocked during restore
  by NU1900: this sandbox cannot access the NuGet vulnerability feed.
- `dotnet build -c Release -m:1 --disable-build-servers -p:NuGetAudit=false`
  succeeded with **0 warnings and 0 errors**. Audit was disabled only for this
  command; no package or repository configuration was changed.
- After final edits, repeated the Release build with `--no-restore`, single-worker
  mode and build servers disabled: **0 warnings and 0 errors**.
- `dotnet test -c Release --no-build --no-restore -m:1 --disable-build-servers`
  reached both test assemblies, but VSTest aborted before executing tests:
  `SocketException (13): Permission denied` creating its local communication
  listener. The orchestrator must run the suites outside this sandbox.
- `git diff --check` passed. No game data or external submodule source was changed.

## Desktop verification still required

The actual Flatpak Steam/Proton desktop launch cannot be exercised here. On the
user's desktop, clear obsolete wrapper/host overrides in Settings, choose
`proton-flatpak`, start Steam and press Play with the legacy game in an accessible
folder. Confirm the game opens and can communicate with Steam. Also check the
stopped-Steam error. Automated command tests require neither Steam nor game files.
