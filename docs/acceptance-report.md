# Pack acceptance report

Every build (`cgfmm build`, `cgfmm apply`, and `ArchiveBuilder.Build`) runs a read-only post-build acceptance analysis (`Jamm.Core.Acceptance.PackAcceptance`) comparing each input mod against the base and the final merged `data.win`. Computing the report never modifies input or output archives.

## Summary and CLI output

- `cgfmm build` and `cgfmm apply` print a compact summary line:

  ```text
  Pack acceptance: 2 mods — Abyss 71/71 present, Verdant 24/25 (lost: fish Reech)
  ```

- Pass `--verbose` to print every tracked item per mod (including `Relocated` and `Modified-by-other` entries), API registration counts, and all runtime risk warnings.
- Pass `--acceptance-json <file>` to `cgfmm build` to write the full `PackAcceptanceReport` JSON (per-mod items, counts, API registration tallies, and runtime risks).

## Status values

| Status | Meaning |
| --- | --- |
| `Present` | The mod's added item or placement arrived in the merged archive at its original slot/identity. Counts toward arrived items in the summary. |
| `Relocated` | The mod's added fish or legacy table entry arrived in the merged archive, but at a different catalog coordinate or table slot index. Counts toward arrived items in the summary. |
| `Modified-by-other` | An asset with the same identity exists in the merged archive, but a different mod's implementation won and replaced this mod's version. |
| `Lost` | The item, placement, or code conflict hunk did not arrive in the merged archive (for example, discarded by a conflict winner choice or deleted without retention). |

## Tracked content kinds and identity rules

| Kind | Source | Identity rule | Arrival check |
| --- | --- | --- | --- |
| `object`, `sprite`, `sound`, `room`, `script` | Assets added by the mod relative to `--base` | Asset name (`Name`) | Present in merged table; `Modified-by-other` if another mod won that name with different content (JAMM runtime placeholder scripts are treated as `Present`). |
| `fish` | Entries added in `gml_Object_CatalogData_Create_0` (`object[a, s]`) or via `jamm_catalog_register` / `jamm_fish_register` | Fish object asset name | Present in merged `CatalogData` (at same `[a, s]` = `Present`, at moved `[a', s']` = `Relocated`) or in active API registrations (`Present`), else `Lost`. |
| `boat` | Entries added in `gml_Object_BoatData_Create_0` or via `jamm_boat_register` | `object[i]` asset name if present, else `name[i]` string | Identity found anywhere in merged `BoatData` (same index = `Present`, moved index = `Relocated`) or active `jamm_boat_register` call (`Present`), else `Lost`. |
| `rod` | Entries added in `gml_Object_RodData_Create_0` or via `jamm_rod_register` | `name[i]` string | Identity found anywhere in merged `RodData` or active `jamm_rod_register` call. |
| `lure` | Entries added in `gml_Object_LureData_Create_0` or via `jamm_lure_register` | `item[i]` / `object[i]` asset name if present, else `name[i]` string | Identity found anywhere in merged `LureData` or active `jamm_lure_register` call. |
| `tool` | Entries added in `gml_Object_ToolData_Create_0` or via `jamm_tool_register` | `item[i]` / `object[i]` asset name if present, else `name[i]` string | Identity found anywhere in merged `ToolData` or active `jamm_tool_register` call. |
| `hat` | Entries added in `gml_Object_GearData_Create_0` or via `jamm_hat_register` | `gear[i]` / `object[i]` asset name if present, else `name[i]` string | Identity found anywhere in merged `GearData` or active `jamm_hat_register` call. |
| `upgrade` | Entries added in `gml_Object_UpgradeData_Create_0` or via `jamm_upgrade_register` | `name[i]` string | Identity found anywhere in merged `UpgradeData` or active `jamm_upgrade_register` call. |
| `placement` | Room `GameObjects` where the mod has more instances of an object than `--base` | `<room>/<object>` | Merged instance count in that room meets the mod's count (or all added `(x, y)` coordinates arrived when another mod removed base instances), else `Lost`. |
| `hunk` | Conflict hunks from this mod discarded by `BuildPlan` resolutions | `<code entry>:<1-based line>` | Always `Lost` with the winning mod noted in `Detail`. |

Active `jamm_*_register` calls in the merged archive (`jamm_catalog_register`, `jamm_fish_register`, `jamm_boat_register`, `jamm_rod_register`, `jamm_lure_register`, `jamm_tool_register`, `jamm_hat_register`, `jamm_upgrade_register`) are also counted per mod (`apiRegistrationCount`) and pack-wide (`apiRegistrations`).

## Pack-level runtime risk checks

Runtime risk checks report non-fatal warnings in `Risks`:

- **`TABLE-LENGTH`**: Compares `maxWrittenIndex + 1` against `nItems` / `n_gear` (or literal loop bounds) in each legacy `*_Data_Create_0` script and external `UpgradeData` loop bounds (`UpgradeMenu_Alarm_0`, `Girl_Other_10`, `Gear_Other_15`) when `UpgradeData` exceeds 18 entries.
- **`CGF-INSTANCE-002`**: Surfaces any `[CGF-INSTANCE-002]` decisions emitted during build diagnosis (code entries reading `<Object>.<member>` when the merged room removed all base instances of `<Object>`).
- **`LIGHT-CULL-RISK`**: Flags objects whose `Create` event (or inherited `Create`) executes `light = instance_create(...)` and that have more than 5 placed instances in any single room, because `FishThinner` culling destroys owned `light` instances when `instance_number(object_index) > 5`.
