# Fix: detect legacy depot in steamrt64 / ubuntu12_32 content folders

## Problem
Current Steam clients (incl. Flatpak) put `download_depot` output at
`<SteamRoot>/steamrt64/steamapps/content/app_343780/depot_343782/` (older: `<SteamRoot>/ubuntu12_32/steamapps/content/...`).
`SteamGameDetection.Candidates` (src/Jamm.Core/Management/SteamGameDetection.cs) only checks
`<root>/steamapps/content/app_343780/depot_343782/data.win`, so a freshly downloaded legacy depot is not found by the selector.
docs/legacy-version.md already documents the steamrt64 path.

## Task
1. In `Candidates`, also check `<root>/steamrt64/steamapps/content/app_343780/depot_343782/data.win` and
   `<root>/ubuntu12_32/steamapps/content/app_343780/depot_343782/data.win` (keep existing paths; keep ordering/dedup behavior).
2. Check whether GameFolderSearch or any other place (GUI, CLI, first-run hints) hardcodes the old content path and fix consistently.
3. Add tests (next to tests/Jamm.Gui.Tests/Phase9bTests.cs line ~90 or in Jamm.Core.Tests) covering steamrt64 and ubuntu12_32 layouts.
4. Run `dotnet test` for the affected test projects; all must pass. Do not commit.
