# Phase 16 — Catalog API: mods add fish as new catalog entries without colliding (implementer: Codex)

Branch `feat/catalog-api` (from main). No commits/pushes/git config, no submodule edits.

## User request (German, verbatim)
"ich will dass du eine api baust über die mods fische als neue entry in den katalog einfügen sodass zb abyss und
cave eben nicht kollidieren"

## Known facts (orchestrator)
- Catalog lives in `gml_Object_CatalogData_Create_0` as parallel 2D arrays indexed [area, slot]: at least
  desc, desc_caught, desc_e, desc_hidden, desc_n, desc_seen, exotic, map, name, object; plus runtime arrays such as
  count, hide, live, pop (CatalogData_Step_1 sets `hide[a,i] = count[a,i] <= 0` for hard-coded slots, e.g. Abyss
  adds hide[3,21], hide[3,23]). Scripts like get_index_for_object, unhide_entry_for_values(display_size-1,
  catalog_index), fish_respawn (CatalogData.pop[ii,jj]) and FishThinner (live/pop) use slot indices.
- Abyss and Cave both append fish at the same slots (e.g. catalog [2,58] ff., boat 8). Today the merge is
  winner-takes-all: the user's profile resolves CatalogData_Create_0 to Abyss, so Cave's (and Colour's) new fish
  exist but have no catalog entry. Phase 3b already remaps typed object indices; docs/reports/phase3b*.md,
  docs/reports/overnight-2026-11-16.md (F3b.1) describe the slot collision.
- Real files (read-only): base `/run/media/varialbe/Festplatte 2/Games/CatGoesFishing-legacy/2023-08-16_manifest-9039707068725406600/data.win`,
  mods `~/Cat-Goes-Fishing-Mods/{the-abyss-beta,cave-expansion,colour-expansion}.win`.

## Deliverables
1. **Audit** (write it down first in the report): every code path that reads/writes catalog slots, including
   save/load (SaveManager ini keys — are catalog stats saved per slot index? per object name?), quests, sonar,
   catalog UI drawing/paging (does the UI support more entries per area, page sizes, max indices?), achievements.
2. **Runtime API** (injected by JAMM when building a merged archive, documented for mod authors):
   GML script(s) e.g. `jamm_catalog_register(area, object, name, desc, desc_caught, ...)` that appends to the next
   free slot in every parallel array consistently and returns the slot; lookup helpers by object (no hard-coded
   indices). Mod authors can call it from CatalogData's Create; document it in `docs/catalog-api.md`.
3. **Merge adapter for existing mods**: when two or more mods add catalog entries at the same slot (or a mod adds
   entries the winning Create would drop), JAMM keeps the base entries untouched, keeps the first mod's slots,
   and relocates the colliding entries of later mods to new free slots — consistently across all parallel arrays
   and every hard-coded slot reference in those mods' code (Step_1 hide/count lines, scripts, quests). Prefer
   deterministic static relocation at build time (verifiable) over runtime magic; the runtime API is for new mods.
   Apply the same mechanism to boat slots (Abyss vs Cave boat 8) if the audit shows it is the same pattern;
   otherwise document what boats need.
4. **Save compatibility**: if saves store per-slot data, make relocation safe (e.g. stable mapping by object name
   written into the build marker + a migration that keeps existing save progress attached to the right fish);
   never silently corrupt a save. Explain exactly what happens to an existing save when the profile changes.
5. **Verification**: extend the semantic catalog check (name[a,i] must correspond to object[a,i] for every slot;
   no duplicate objects; every mod-added fish present exactly once). Real-file result for Abyss+Cave+Colour in
   both load orders: all base entries unchanged, all new fish of all three mods in the catalog, 0 inconsistent slots.
   Remove the need for the user's manual `profile resolve ... CatalogData_Create_0 Abyss` (merge must now succeed
   without it; keep resolutions working).
6. Tests: synthetic archives with colliding catalog additions, relocation + reference rewrite, save mapping,
   deterministic output; plus the real-file check (opt-in env vars JAMM_ORIGINAL/JAMM_MODS_DIR).
Report: docs/reports/phase16.md. The orchestrator verifies in game (catalog screens) afterwards.
