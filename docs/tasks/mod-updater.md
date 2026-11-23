# Task: "Pull & install updates" — mod auto-updater (CLI + GUI)

## User goal
"ein autoupdater für mods: man drückt 'pull and install updates' und es installiert die (mit Warnung falls save conflicts)."

## Required behavior
1. **Check**: for every mod in the active profile that came from a catalog (first-party varialbe.xyz `catalog.json`, Litterbox/other
   providers already in `src/Jamm.Core/Browsing`), compare installed `Version`/`sha256` with the catalog entry. Produce a list
   `id, installed version, available version, size, changelog/notes if present`.
   CLI: `cgfmm update --check [--profile <n>]` (JSON with `--json`), exit 0 = up to date, 10 = updates available.
2. **Pull & install**: `cgfmm update --apply [--profile <n>] [--yes]` and a GUI button "Pull & install updates" (main window, next to
   the profile/build controls; disabled while a build runs; shows count badge when updates exist).
   Steps: download (existing ModHttp/resolver, verify sha256 from catalog — fail closed on mismatch) → add/replace in ModLibrary keeping
   the old package file as `<file>.prev` for rollback → rebuild the profile (existing build path) → verify → install to the profile's
   game dir atomically (write `data.win.new`, then rename; keep `data.win.bak-<timestamp>` of the previous one).
3. **Save-conflict warning** (BEFORE installing, user must confirm in GUI; CLI needs `--yes` or interactive y/N):
   * Back up the save folder of that game dir/prefix (CGF save `Cat_Goes_Fishing/` under the Wine prefix AppData/Local, path
     resolution already exists for game dirs — reuse; if unknown, warn that no backup could be made) to
     `<store>/save-backups/<profile>/<timestamp>/` always.
   * Flag a **save conflict** when any of: mod major version changes (semver major); mod removed from catalog; the new build drops or
     renames objects/items that the old build had which a save may reference (compare added-asset name sets of old vs new mod patch:
     objects, catalog fish rows, boat/rod/upgrade slots, items-runtime ids); the catalog entry declares `"saveBreaking": true`
     (add optional field support + document it in site/cgf-mods docs and catalog schema).
   * Warning text lists each mod and the concrete reason, plus the backup path. No conflict → still show a short summary & confirm.
4. **Rollback**: `cgfmm update --rollback [--profile <n>]` restores previous packages (.prev), previous data.win (.bak) and offers to
   restore the save backup.
5. Never touch anything outside the selected profile's own game dir. Respect existing protections (profiles/gameDir marked
   protected must refuse unless the user explicitly runs the updater for that profile — the updater IS the allowed way).
6. Tests in `tests/` with a fake catalog + fake HTTP (no network): up-to-date, update available, sha mismatch, major bump → conflict,
   dropped object → conflict, rollback. `dotnet test` must pass. Update README/docs briefly.

## Constraints
Codex only with gpt-6-luna. Don't commit TGE/PWB names or files. No network in tests. Commit on your branch with clear messages; do not push.
