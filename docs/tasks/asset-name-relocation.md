# Task: two mods ADD an asset with the same name but different content → keep both (rename + remap)

## User goal
"If two mods occupy the same ID, one gets a new ID and all references move along, so both contents survive. It must be smart:
a mod may intentionally modify something (→ merge), or it adds a completely new thing (→ own ID)."
For GameMaker assets the name IS the identity. Today, when two mods each ADD an asset with the same name, one definition wins
and the other mod silently runs with the wrong asset. Relocation of colliding boat table slots already exists
(`src/Jamm.Core/Building/LegacySlotRelocation.cs`, CGF-RELOCATE-001) and fish catalog slots (`CatalogMerge`). This task adds
the asset level, which runs BEFORE those (so they see distinct names).

## Real cases (verified 2026-11-19; TGE/PWB are private — never put their names/content into tests)
1. Object `Boat8` added by TGE (parent Boat, sprite `spr_boat_8`, events Create/Destroy/EndStep/Other15/Draw; used by its BoatData
   slot "Echo-9") and by Antiquity (parent Boat, sprite `spr_boat_8`, only Create; NOT referenced by Antiquity's BoatData, whose
   "Dark Boat" uses another object). Pack `TGE,Antiquity,PWB`: merged `Boat8` = Antiquity's → Echo-9 runs with the wrong boat.
   Also sprite `spr_boat_8`, `spr_boat_icon8` collide. Acceptance report shows "TGE … modified: object Boat8, sprite spr_boat_8".
2. Object `Dollfin` added by Antiquity (small fish, catalog row 0, 22 room instances) and by `cave-expansion.win` (medium fish,
   catalog row 1, 3 instances). Build ABORTS: `Catalog fish 'Dollfin' changes size category across mods` (CatalogMerge.cs:48).
   Public files: `~/cgf-files/mods/pack-inputs/Antiquity.win`, `~/cgf-files/mods/cave-expansion.win` — use these for a real check.

## Required behavior
1. **Detect** assets (objects, sprites, sounds, scripts, backgrounds, paths, fonts, timelines; code entries follow their owner
   object/script) that are ADDED (absent from base) by ≥2 mods under the same name.
2. **Classify** each colliding pair:
   * identical content (same patch payload / same code text / same sprite bytes) → shared lineage, no action (dedupe as today).
   * "same thing, modified": the later mod's definition is clearly a modification of the earlier one (same parent and same sprite
     frames/size for objects, ≥ ~60% identical code lines across events, or for sprites identical dimensions with frames added) → keep
     today's merge behavior, add an INFO decision.
   * otherwise "different things" → **relocate**: rename the later claimant's asset to a unique deterministic name
     (`<name>_jamm_<modslug>`; must be a valid GML identifier and not collide), including its owned code entries
     (`gml_Object_<name>_*`), and rewrite ALL references inside that mod's patch: structured references (parent, sprite of objects,
     room instances' object, sprite/sound fields, texture/audio entries), GML identifier tokens equal to the name in that mod's
     added/changed code (token-based, not substring; skip strings), and numeric asset-index literals that JAMM's SemanticCode
     resolves for that mod (make sure the mod-index→name→merged-index remap uses the NEW name for that mod).
     Assets of the relocated mod that reference the renamed one must follow too (e.g. its catalog `object[a,s]`, BoatData `object[i]`).
   * Fail closed: if a reference cannot be rewritten safely (e.g. `asset_get_index("Dollfin")` string lookups, `object_get_name`
     comparisons, save keys using the name), either rewrite string literals that exactly equal the name in those specific calls, or
     skip relocation with a WARNING decision naming the file/line — never build a half-renamed archive.
3. Decision line: `[CGF-RELOCATE-002] Object "Dollfin" (cave-expansion) renamed to "Dollfin_jamm_cave_expansion": different asset than
   Antiquity's (parent/size/code differ); rewrote N references (M code entries, K room instances).`
4. After relocation the Dollfin pack (`Antiquity, cave-expansion`) must build, both fish must be in the catalog (2 distinct objects), and
   the TGE+Antiquity+PWB pack must keep TGE's Boat8 for Echo-9 (TGE's events) and Antiquity's as a renamed object.
5. Pack acceptance report (`src/Jamm.Core/Acceptance/PackAcceptance.cs`) should report renamed assets as `Relocated` (not Modified).

## Tests
Synthetic archives (see `PackAcceptanceTests`, `CatalogArchiveTests`, `LegacySlotRelocationTests`): two mods add object `Foo` with
different parent/code → both present after build, references (room instance, GML `instance_create(…, Foo)`, catalog row) follow;
identical additions → no rename; modified-copy → no rename + INFO; string lookup → warning, no rename; a sprite collision.
Opt-in real test (env `JAMM_ORIGINAL`, `JAMM_MODS_DIR`) for Antiquity + cave-expansion: build succeeds, both Dollfin variants present.

## Manual verification (read-only inputs; outputs only under /tmp)
`dotnet run --project src/Jamm.Cli -c Release -- build --base ~/cgf-files/legacy/2023-08-16_manifest-9039707068725406600/data.win --order ~/cgf-files/mods/pack-inputs/Antiquity.win,~/cgf-files/mods/cave-expansion.win --out /tmp/dollfin.win`
Private heavy pack (allowed to run locally, never commit names/content): `--order ~/cgf-files/private-vel/TGE.win,~/cgf-files/mods/pack-inputs/Antiquity.win,~/cgf-files/private-vel/PWB.win`.
Builds take several minutes (decompiling ~2000 code entries per mod) — run them once at the end, in the background if needed.

## Rules
AGENTS.md applies. Zero warnings; `dotnet test -c Release` green. Do NOT commit or push. Work only in this worktree (+ /tmp).
Finish with a summary: files changed, tests, decision lines from both real packs.
