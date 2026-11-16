# Catalog API (JAMM phase 16)

JAMM injects these GM1.4 scripts when building an archive with CatalogData changes.
`area` means the game's **size tab**: 0 small, 1 medium, 2 large, 3 huge.

```gml
// At the END of CatalogData Create, after its ordinary initialization:
var slot;
slot = jamm_catalog_register(2, MyFish, "My Fish", "A newly discovered fish.");
```

The signature is `jamm_catalog_register(area, object, name, desc,
desc_caught?, desc_seen?, desc_hidden?, desc_n?, desc_e?, exotic?)`.
It returns the appended slot, or the existing slot when the same object is already
registered in that size. Invalid size/object or registering an existing object in a
different size returns -1. Name and descriptions are strings; exotic defaults to 0.
Omitted descriptions inherit desc, except hidden, which has a generic discovery
message. All parallel fields are initialized: object, name, map, description
families, exotic, weight, life, count, hide, max_pop, pop, live and extinct.
The display permutation appends the new fish, and instance index refresh is reset.

`jamm_catalog_find(object)` returns its slot, `jamm_catalog_area(object)` its size;
both return -1 for an unknown object. Use both values when reading catalog state.
Do not persist the returned slot, assume another mod's final index, or change an
object's asset name between versions if its progress should survive.

Mods can ship placeholder scripts with these exact names to compile standalone;
JAMM replaces their implementations with its API. The `jamm_catalog_` namespace is
reserved. A standalone mod's placeholders must implement its own fallback behavior.
Registration calls in CatalogData Create are retained by the adapter. Keep them
unconditional and self-contained, using literal arguments or global/asset values;
local temporary variables from a mod's replaced Create are not carried over.

## Existing mods and conflict choices

Literal legacy entries are discovered by object identity. Base catalog definitions
are retained. First additions retain their coordinates; later collisions append to
the next free slot of that size. An object shared by mods gets one entry. A mod's
replacement of a base fish becomes an additional entry, keeping the base fish.
The same relocation is applied before merging to catalog reads/writes and literal
`unhide_entry_for_values(size, slot)` calls. Qualified CatalogData references and
CatalogData instance/with scopes are recognized; strings, comments and other
objects' arrays are excluded. Ordinary conflicts elsewhere still follow the
existing resolution rules.

The display map is a permutation, not an object table. Base order is kept and
additions are appended once. Array dimensions drive the existing scroll UI; the
four size tabs do not need another page-size constant.

The catalog Create no longer needs a manual winner resolution. Existing named
winner resolutions remain accepted, and cannot discard another mod's new fish.
Base slot identity/definitions take priority over cosmetic mod edits to those
entries. Unsupported sparse definitions or unidentifiable entries fail with a
message instead of producing phantom fish.

Abyss also overwrites two of its own object/name slots. JAMM recovers the earlier
named fish at fresh slots, with their display name as a conservative description
where description occurrences cannot be paired reliably. Literal references to
an overwritten slot keep the source's final-object meaning.

## Saves and changing profiles

Legacy Catalog keys concatenate size and slot, so load order can change their
meaning. New builds read/write a separate `JAMM_Catalog` INI section using keys
`f:<object asset name>`, `li:<object asset name>` and `pop:<object asset name>`.
No external save file is edited during building or installation.

On first load of a legacy save, unambiguous base entries use their original keys.
The five base positions reused by released Cave archives (even when Cave is not
selected today), positions replaced by another selected mod, and all mod-added
slots are ambiguous:
JAMM leaves their old keys untouched, starts those entries undiscovered and shows
one explicit notice during loading. It cannot infer whether an unmarked save came
from Abyss or Cave. Keep a backup and the old archive to use that old mod progress.
This deliberately avoids assigning progress to an unrelated fish. Saving records
the current state under object names; the legacy section remains intact.

Once the new section exists, missing identities use defaults instead of falling
back to stale legacy slot keys. Reordering a profile retains named fish progress.
Removing a mod leaves its keys in the save; re-adding the same object restores
that progress. Renaming an object constitutes a new identity. The installed build
marker includes the mod/object/source-slot/final-slot mapping for diagnosis; the
runtime does not depend on a host filesystem marker to load a save.

This migration covers **catalog statistics**. Other legacy systems still store
instance IDs, object IDs for fish on the boat/lure, and boat slots. Those systems
are not made profile-independent by the catalog API. Use an empty boat and lure
when switching mod profiles, and retain a save backup.

## Boats

Abyss and Cave both introduce slot 8, but also use the same `Boat8` object and
`spr_boat_icon8` names for different implementations. Moving an array entry would
still select a single winning object/art definition. Boat support requires asset
namespacing and event/resource relinking, boat_id/unlock/equipped-save migration,
and achievement handling. Boats therefore continue to use ordinary explicit or
load-order conflict choices. JAMM does not advertise two independent boat 8s.

## Verification

The builder compares every literal name/object/map/exotic/description cell with
the expected merged source both in memory and after writing/reloading. It also
rejects duplicate object entries and missing/duplicate display-map targets. Normal
asset-reference verification and full decompile/compile verification still run.
Tests using real archives require JAMM_ORIGINAL and JAMM_MODS_DIR; no game data
belongs in the repository. The orchestrator must still check catalog screens and
save behavior inside the game.
