# Fish virtualization inventory (generated)

Generated 2026-11-22 from the legacy vanilla asset names/decompilation and the local patch manifests. The signature scan is a review aid, not proof of safety.

Vanilla fish-related event/code entries: **261**.
Garder hierarchy: **Garder → FishSmallTropic → FishSmall → Fish**; own event entries: gml_Object_Garder_Create_0.
The profiler’s vanilla dock capture (`vanilla-prof-dock`, 270 ticks) attributes **56,109 µs/tick** to `gml_Object_Fish_Step_2` (39% of the profiled loop). The leading event is inherited by ordinary fish descendants, so the pilot uses the Garder leaf object rather than virtualizing the Fish parent.

| Local pack | Fish-related added/changed/removed code entries | Entries with instance/population query signatures | Manifest SHA-256 |
|---|---:|---:|---|
| TGE | 96 | 19 | `933f306bd21f6826…` |
| PWB | 45 | 11 | `f99e9c9810d9c0ba…` |
| Abyss | 64 | 2 | `3cef7153f7226834…` |
| Antiquity | 71 | 6 | `79387064619978e2…` |
| Colour | 66 | 6 | `e54bbaf303e9939b…` |
| Verdant | 8 | 2 | `2c90a91cdfc295d2…` |
| Cave Expansion | 83 | 1 | `15d406b38c69ddc1…` |

## Garder pilot candidate

Vanilla asset inventory: Garder is a leaf under `FishSmallTropic → FishSmall → Fish`; its own event set is Create only. It inherits the shared Fish Step/Draw and parent lifecycle events. Vanilla profiler captures show the inherited Fish Step is the dominant cost, but do not give a per-leaf CPU attribution. The record schema is restricted to primitive pose/lifecycle fields; no instance IDs, light handles, alarms, or RNG state are stored.

Compatibility gate: Garder is a bench-only pilot. Pack code and direct GML instance queries remain a separate compatibility concern; no pack support is claimed.

Reproduce with `python3 tools/bench/fish-virtualization-inventory.py --legacy <legacy-data.win> --tool tools/Jamm.ModMaker/bin/Release/net10.0/jamm-modmaker.dll --patch PACK=manifest.json` repeated once per pack. The local inputs used for this report were the seven manifests at `~/jamm-night/n21/{vgr/patches/TGE,ul/patches/PWB,pub5-main/patches/{Abyss,Antiquity,Colour,Verdant,cave-expansion}}.cgfmm-patch.json`.
