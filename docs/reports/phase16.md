# Phase 16 — catalog API

## Audit (before implementation)

Read-only audit of all root code entries in the legacy 2023 base and Abyss, Cave,
and Colour archives, decompiled only into /tmp. No game source or data is included here.

* Catalog coordinates are **fish size category**, not geographic area: 0 small,
  1 medium, 2 large, 3 huge. Base has 199 object entries. `map` is a display-order
  permutation, so its **values** as well as its indices require relocation.
* Create defines object/name/exotic/map and description families, then initializes
  weight/life/count/hide/desc_seen/desc_hidden/desc_caught/max_pop in dynamic loops.
  Runtime also uses pop/live/extinct. Step 0/1/2 update population, special fish
  descriptions, hidden entries and instance catalog_index. Object lookup and
  fish_appraise resolve indices dynamically; Fish Step 2 reveals by size/index.
* SaveManager user events 13 (load), 14 (save), 15 (secondary save) persist Catalog
  keys f/ li/ pop + concatenated size and index, **not object names**. Relocation
  without migration would attach progress to the wrong fish. Unmarked legacy
  mod saves cannot identify which mod owned a colliding slot. Base progress is
  unambiguous; unknown mod progress must never be guessed. OnBoat/LF also save
  numeric object IDs and Fishes saves instance IDs: these are pre-existing,
  separate limitations of changing archive asset order.
* Fishalog Create iterates array dimensions and `map`; CatalogItem stores size,
  index and display order. Fishalog uses a scroll surface and dynamic scroll
  height, not a fixed last slot/page limit. Four size tabs remain fixed.
* Quests use fish names/object lookups and CatalogData counts. Completion,
  reveal-all, extinction and achievements inspect dynamic arrays or base special
  slots. Sonar uses fish instances/object identity, not a separate catalog table.
* Boats have parallel one-dimensional arrays, but both mods define the **same
  named Boat8 object and spr_boat_icon8** with different behavior/art. Slot
  relocation alone cannot retain both definitions. A boat adapter also needs
  namespacing object, sprite and associated events, plus boat_id, unlock/save,
  equipped-boat and achievement migration. This phase must not pretend a second
  slot pointing at the same winning Boat8 is two independent boats.

Audited direct consumers and catalog UI entry points (union across four archives):

* `gml_Object_BoatData_Step_0`
* `gml_Object_CatalogData_Alarm_5`
* `gml_Object_CatalogData_Create_0`
* `gml_Object_CatalogData_Draw_0`
* `gml_Object_CatalogData_Other_10`
* `gml_Object_CatalogData_Other_11`
* `gml_Object_CatalogData_Step_0`
* `gml_Object_CatalogData_Step_1`
* `gml_Object_CatalogData_Step_2`
* `gml_Object_CatalogDown_Create_0`
* `gml_Object_CatalogDown_Mouse_61`
* `gml_Object_CatalogDown_Other_10`
* `gml_Object_CatalogHandle_Create_0`
* `gml_Object_CatalogHandle_Mouse_4`
* `gml_Object_CatalogHandle_Mouse_50`
* `gml_Object_CatalogHandle_Mouse_56`
* `gml_Object_CatalogHandle_Step_2`
* `gml_Object_CatalogItem_Other_10`
* `gml_Object_CatalogSurface_Create_0`
* `gml_Object_CatalogSurface_Mouse_4`
* `gml_Object_CatalogSurface_Mouse_50`
* `gml_Object_CatalogSurface_Mouse_56`
* `gml_Object_CatalogTab_Create_0`
* `gml_Object_CatalogTab_Draw_0`
* `gml_Object_CatalogTab_Other_10`
* `gml_Object_CatalogUp_Mouse_60`
* `gml_Object_CatalogUp_Other_10`
* `gml_Object_Catalog_Create_0`
* `gml_Object_Catalog_Destroy_0`
* `gml_Object_CatchQuestNewNew_Other_11`
* `gml_Object_CatchQuestNew_Other_11`
* `gml_Object_ControlObjectSpawner_Create_0`
* `gml_Object_Cuddlecanth_Alarm_8`
* `gml_Object_Cusper_Other_16`
* `gml_Object_FishThinner_Step_0`
* `gml_Object_Fish_Create_0`
* `gml_Object_Fish_Step_2`
* `gml_Object_Fishalog_Create_0`
* `gml_Object_Fishalog_Draw_0`
* `gml_Object_Fishalog_Step_0`
* `gml_Object_Girl_Step_0`
* `gml_Object_Magnalav_Step_0`
* `gml_Object_Message_RevealAll_Create_0`
* `gml_Object_SaveManager_Other_13`
* `gml_Object_SaveManager_Other_14`
* `gml_Object_SaveManager_Other_15`
* `gml_Object_icon_NewQuest_Other_11`
* `gml_Object_icon_Reset_Other_10`
* `gml_Script_cast_complete`
* `gml_Script_fish_appraise`
* `gml_Script_fish_depop`
* `gml_Script_fish_respawn`
* `gml_Script_fish_was_caught`
* `gml_Script_get_exotic_for_name`
* `gml_Script_get_index_for_name`
* `gml_Script_get_index_for_object`
* `gml_Script_get_life_for_name`
* `gml_Script_increment_count_for_name`
* `gml_Script_line_snap`
* `gml_Script_new_quest`
* `gml_Script_quest_complete`
* `gml_Script_set_best_life_for_name`
* `gml_Script_start_quest_fish_new`
* `gml_Script_start_quest_fish_new_new`
* `gml_Script_unhide_entry_for_name`
* `gml_Script_unhide_entry_for_values`

## Additional audit findings

Cave replaces five base identities at [0,23], [0,25], [1,39], [1,43],
[2,46]. These are retained as base entries; Cave's replacement fish receive new
entries. The legacy import blacklist includes these positions even without Cave
in today's profile, because the save may have been written by Cave previously.

Abyss writes two different object/name pairs to each of [2,92] and [2,93]. Its
own final arrays therefore omit Gremlin and Bascer. The adapter recovers both
identifiable pairs at fresh slots. Description assignments cannot be paired
reliably after the source typos: recovered entries use their display label as a
conservative description rather than another fish's prose. Literal references to
an overwritten source slot continue to mean its last assigned object.

## Implementation

* `CatalogMerge` runs before conflict analysis and build planning. It reads lexical
  spans, preserves the complete base Create prefix, deduplicates object identities,
  keeps first additions and relocates later additions deterministically per size.
  Base replacements become additions. Every description/runtime parallel array is
  initialized, and new display-map entries form a permutation. Unsupported gaps
  and missing labels fail clearly instead of creating phantom entries.
* Qualified and scoped catalog array reads/writes, map targets, literal reveal
  calls, and object-scoped catalog_index assignments follow the source mapping.
  Strings/comments and other object scopes are excluded. Catalog insertion hunks
  containing only assignments to distinct cells are combined automatically.
* `CatalogRuntime` injects registration, object/size lookup, and save-read scripts.
  Optional author placeholders are replaced by the official implementation.
  Existing CatalogData games receive the API even when selected patches leave
  Create unchanged. Author registration calls are retained after initialization;
  the documented form is unconditional, self-contained calls at the end of Create.
* SaveManager load/save/secondary-save calls use object-name keys in a new INI
  section. First import reads only unambiguous base legacy positions. Ambiguous
  legacy mod keys are never reassigned, changed or deleted: those entries start
  undiscovered and loading displays an explicit notice. Once named state exists,
  absent identities use defaults, never stale slot data. Removed mod keys remain
  available when the same object is re-added. This is conservative migration, not
  a claim to recover ownership that an unmarked legacy save never recorded.
* CLI/profile installation markers carry mod/object/source/final-slot mappings.
  Runtime save identity is self-contained in the save and does not depend on the
  installation marker. Profile build signatures were bumped so previously built
  profiles are rebuilt with the API.
* In-memory and reloaded archive checks compare every expected literal
  name/object/map/exotic/description cell, reject duplicate/extra/missing objects,
  and check map permutations. The existing typed-reference and full bytecode
  decompile/compile verification remains enabled, including injected scripts.
* Conflict preview now reports an AUTO catalog finding. Named Create resolutions
  remain accepted without dropping another mod's fish. No manual profile resolve
  is needed for the catalog. The user guide/conflict guide and
  `docs/catalog-api.md` describe the new behavior and save policy.

## Real-file results

Abyss + Cave + Colour, forward and reverse orders, without catalog resolutions:

| Check | Forward | Reverse |
| --- | ---: | ---: |
| Base fish retained at their original slots | 199 | 199 |
| Unique additional fish (including source overwrite recovery) | 174 | 174 |
| Total unique catalog fish | 373 | 373 |
| Inconsistent slots | 0 | 0 |
| Selected/base/injected code entries passing semantic checks | 2,137 | 2,137 |

Both outputs contain 78 small, 109 medium, 142 large and 44 huge fish. There are
175 source mappings because one object is shared across mod sources. All base
literal assignments remain in the original Create prefix, including map order.

The opt-in real build test passed: single-mod archives, both three-mod orders,
all source object identities exactly once, unchanged base prefix, explicit legacy
Cave Create resolution, standalone verification, copied-folder install/reinstall/
restore, and input SHA-256 invariance. The source archives and real installation
were never modified. Temporary real archives/decompilation remained outside the
repository under /tmp.

## Validation

* Release solution build: `dotnet build -c Release --no-restore -m:1`, zero warnings
  and zero errors. Initial restore used `-p:NuGetAudit=false` because the sandbox
  cannot reach NuGet's vulnerability service. No project/config setting changed.
* Standard `dotnet test` was attempted; VSTest cannot bind its local control socket
  in this sandbox (SocketException, permission denied). A temporary in-process
  reflection runner executed the same xUnit Fact/Theory methods and data rows.
  Core regression result: 271 passed, zero failed, six opt-in cases skipped.
* The real-file test was separately enabled with JAMM_ORIGINAL/JAMM_MODS_DIR:
  one passed, zero failed. Its explicit assertions cover both orders and all
  original/mod input hashes.
* Synthetic coverage includes collisions, base preservation, duplicate object
  identities, deterministic byte-for-byte archive hashes, scoped reference and
  map-target rewriting, instance index/reveal rewriting, combined hide rules,
  self-overwrite recovery, deliberately corrupted semantic cells, compiled API
  injection/save-call transformation and install-marker mapping/restore.
* `git diff --check` passed; submodule working tree remains clean. No commits,
  pushes, releases, git configuration changes or game data added to the worktree.

## Remaining game verification and boundaries

The orchestrator should inspect all four catalog tabs, scroll to the appended
entries, catch/reveal fish from each mod, and exercise save/reload plus load-order
changes in game. The engine itself was not launched here. Legacy mod catalog
progress is retained as old keys, not magically attributed to a fish. A separate
explicit source-save mapping would be needed to import ambiguous historical
progress. Custom legacy mods that replaced different base slots need an explicit
migration adapter before claiming safe import of those slots.

The adapter supports literal parallel-array definitions and the audited index
consumers, not arbitrary computed coordinates, alias-based writes or arbitrary
local Create logic. API authors should use object lookups rather than numeric
slots. Boat8 still needs asset namespacing, event/art relinking, boat_id/unlock/
equipped-save migration and achievement handling; assigning another slot to the
same winning object would not create two boats. Other legacy object/instance ID
save systems (fish on the boat/lure) remain outside catalog-stat migration.
