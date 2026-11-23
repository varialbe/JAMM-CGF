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
