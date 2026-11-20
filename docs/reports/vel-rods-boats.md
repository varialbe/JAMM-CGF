# Vel's rods and boats

Tested on the August 16, 2023 legacy VM base (Steam manifest `9039707068725406600`).

## Part A · ModKit rods

The `rods` array validates names, prices and stats, single-frame PNGs, origins, and slot counts. Its generated init calls `jamm_rod_register`, so the Items API owns slot allocation, upgrade rows, and save keys. An integer sets the slot count directly. `"vanilla"` passes `-1` to apply the existing rule for adding one slot above the highest current count. The default is eight. Rods use the same `cost` and `unlocked` fields as boats for the shop and starting ownership. The GUI accepts rod packages through its ModKit folder import and rebuild controls.

[Example Rod](../../examples/modkit/example-rod/) builds and merges as a one-sprite mod. The merged build verified at 44,669,990 bytes and injected `jamm_init_example_rod` along with Items API persistence and shop paging.

- `run.sh <merged-win> velrod dock 600` returned **RECORDED** with 828 samples over 100 seconds of capture. The CSV is at `~/jamm-night/runs/velrod-dock/jamm_bench_dock.csv`.
- A second bench run opened the stock rod menu through the rod icon's event and captured Copper Current offered for **$25,000**. That run also returned **RECORDED** with 437 samples. The screenshot is at `~/jamm-night/vel-rods-boats/velrod-shop.png`.

## Part B · Legacy unlock checks

I ran `cgfmm check` against each installed mod's raw patch and the matching legacy base. I compared both warnings and every reachable finding against the local decompiled tables, card events, and reward code.

| Mod | Finding | Manual evidence |
| --- | --- | --- |
| Antiquity | Dark Boat, slot 8, reachable | `BoatData` gives it a $9,000,000 price and a hidden initial state. `BoatData_Step_0` exposes slot 8 after the two required catches. The stock card event uses that price. |
| Abyss | **CGF-UNLOCK-001** for Hyper Boat, slot 8 | The $10,000,000 row starts hidden. The stock purchase progression excludes the last boat row and the catch reward exposes slot 7. I found no additional quest or save writer for slot 8. The generic save loop persists the slot's existing state. |
| Colour | No added boat or rod row | Its rod changes affect existing rows. |
| Verdant | No added boat or rod row | Its legacy item tables add neither kind. |
| Cave | Mystic Boat, slot 8, reachable | The boat starts purchasable for $6,000,000. Its row falls within the unlock array length used by the shop and the stock boat card reads that price. |
| Cave | **CGF-UNLOCK-002** for Mystic Rod, slot 6 | The $680,000 rod row starts purchasable, but `RodData.nItems` remains 6, so the card handler omits it. The legacy upgrade matrix also ends at slot 5. I found no separate grant for the rod. |
| PWB | No added boat or rod row | PWB adds an upgrade definition in `UpgradeData` and retains the existing `RodData` rows. |
| TGE | Echo-9, slot 8, and Proto-Rod, slot 6, reachable | The Louis dialogue exposes both priced shop rows. The Proto-Rod also has a direct ownership grant in the dialogue. I checked the dialogue call site, table bounds, and initial states. |

The seven-mod check also found both warnings. It reported Hyper Boat at relocated slot 9, Mystic Boat at 10, Echo-9 at 11, and Proto-Rod at 7. Mystic Rod remained at slot 6. These findings describe each mod's evidence after relocation. The seven-mod check had 90 unrelated conflicts.

Starting ownership, visible prices, stock purchase progression, and typed writes to unlock state provide evidence. The checker recognizes independent INI flag writers and treats ordinary save round-tripping as persistence. Computed or ambiguous paths can appear as unverified. Verifying whether a player can satisfy a quest's conditions still requires an in-game check.

### Opt-in repair

The per-profile switch is `cgfmm profile unlockfix <profile> <mod> on|off`. The desktop checkbox is **Repair legacy shop unlocks in this profile** on the expanded mod row. The build uses the existing shop table and rod-slot completion. It keeps a usable author price and estimates a missing price from neighbouring slots. A matching legacy save-load path preserves owned rows and makes the repaired slot available in older saves.

Abyss and Cave were built and verified with the setting off and on in a disposable game directory. The enabled builds reported Hyper Boat made buyable at $10,000,000 and Mystic Rod made buyable at $680,000. The Cave build uses the existing rod relocation code to include slot 6 in the shop count and initialize the corresponding upgrade row.

## Verification

- `dotnet build -c Release` passed with zero warnings.
- `dotnet test -c Release --no-build` passed the standard Core and GUI suites. Real-file groups are opt-in.
- The targeted `RealBaseMergesExampleRodWithItemsRuntimeAndShopPaging` test passed against the legacy archive. It checked sprite origin, the generated init, registration runtime, and shop paging after the merge.
- Synthetic fixtures cover slot validation and generated GML, reachable and missing legacy shop rows, quest and save flags, relocation, build repair off and on, and profile persistence.
