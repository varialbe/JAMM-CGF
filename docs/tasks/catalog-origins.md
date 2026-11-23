# Let the updater see mods that were added from local .win files

Problem: `cgfmm update --check` reports "up to date" for mods added via `cgfmm add <file.win>` because they have no
catalog origin (mod.json `origins: []`, `packageId: null`). The user's own mods (silo, weather-pack, endless-ocean,
submarine, harbor-town) were added that way, so they never get updates.

Implement:
1. Origin matching: when a mod is added (and via a new `cgfmm link --auto [--profile X]` / `cgfmm link <mod> <catalog-id>`),
   match it against the first-party catalog (catalog.json from varialbe.xyz/cgf-mods, already used by the updater:
   see CatalogUpdates.cs / ModUpdateWorkflow.cs) by sha256 of the archive, falling back to name + base sha + version
   heuristics; record packageId/catalog/version in mod.json. Never guess silently: print what was linked and why;
   ambiguous → do not link, explain.
2. GUI: in the library view, an action "Link to catalog" and an indicator when a mod has no origin.
3. `update --check` should list unlinked mods separately ("not linked to a catalog: …, run cgfmm link --auto").
4. Tests (unit, with a fake catalog). Docs in docs/ (user-facing short section).
5. Try it read-only on a COPY of ~/jamm-personal/store (cp -a to /tmp/…; CGFMM_DATA_DIR=<copy>) and report which of the
   user's mods link. NEVER modify ~/jamm-personal/store itself, never touch CGF-Personal or compatdata.
No game runs needed. Build/test with dotnet -m:1. Commit on branch feat/personal-catalog-origins; do not push.

## Round 2 (orchestrator review, merged c45c563 to main; real-store trial found bugs)
Ran `cgfmm link --auto --profile personal` on the real personal store (then restored metadata). Problems:
1. Builtins (builtin-console/perf/smooth/hd-lite) were linked to catalog entries. Builtins are generated locally from the
   user's base by BuiltinMods (cache key GeneratorVersion); they must NEVER be linked or updated from a catalog.
2. After linking, `update --check` listed every linked mod as an update with identical versions, e.g.
   "JAMM Console (builtin-console): 1.0.0 → 1.0.0 · 0.0 MB", "The Silo (jamm-silo): 2.3.0 → 2.3.0", and exit "Updates are available".
   Same version + different sha must not be an update. Decide properly: if linked by name/base heuristics (not sha), record
   the local sha as "local build of version X"; report "local build differs from catalog X" separately (info, no apply).
   Only a strictly newer catalog version (semver) is an update. `--apply` must never replace a mod whose local sha
   differs at the same version without an explicit `--force-replace-local`.
3. Linking by name heuristics should say so in mod.json (match kind) and in output.
Add tests for all three. Re-trial on a metadata-only copy of ~/jamm-personal/store (never modify the real store) and show the
exact `update --check` output in the report. Commit on this branch; do not push.
