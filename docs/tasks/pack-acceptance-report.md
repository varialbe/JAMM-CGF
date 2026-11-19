# Task: Pack acceptance report — "what arrived from each mod, what was lost"

## Why
Users combine 4–5 mods. A build that succeeds can still silently drop a mod's boat, upgrade, fish or room content
(conflict winner choice, lost hunks, lost room placements). We need a machine-checkable per-mod report after every build,
used by tests, CLI and later GUI.

## Deliverable
1. `Jamm.Core`: a pure analyzer `PackAcceptance` (new folder `src/Jamm.Core/Acceptance/`) that takes the base `UndertaleData`,
   each mod's `UndertaleData` (or its `ModPatch`), the final `BuildPlan` and the merged `UndertaleData`, and returns a result object:
   per mod → list of `ContentItem { Kind, Identity, Status (Present | Lost | Modified-by-other | Relocated), Detail }`.
   Kinds (each with a precise, documented identity rule):
   * `object`, `sprite`, `sound`, `room`, `script` assets the mod ADDED (identity = asset name) → present in merged?
   * `fish`: catalog entries the mod added (identity = fish object name from `CatalogData.object[a, s]`; parse the mod's
     `gml_Object_CatalogData_Create_0` and the merged one, or reuse `CatalogMerge` parsing helpers) → present in merged catalog?
   * `boat`, `rod`, `lure`, `tool`, `hat`(Gear), `upgrade`: legacy table entries the mod added (parse `*_Data_Create_0` literal index
     writes with existing `GmlReferences.IndexWrites`; identity = `object[i]` asset name if present else `name[i]` string)
     → is the same identity present in the merged table (at any index)? Also count JAMM API registrations (`jamm_*_register` calls)
     if present in merged code.
   * `placement`: instances of mod-added or mod-increased objects in rooms (per room, per object count) → merged count ≥ mod's count minus base?
   * `hunk`: conflict hunks of this mod that were discarded by the plan (`plan.Decisions`/resolutions) → listed as Lost with code entry + line.
   Plus pack-level **runtime risk checks** (warnings, not failures):
   * table length consistency: for each legacy table, max written index + 1 vs `nItems`/loop bounds in the merged Create code.
   * existing `CGF-INSTANCE-002` decisions surfaced as risks.
   * objects that own a `light` instance (`light = instance_create(...)` in Create) with more than 5 placed instances in one room
     (FishThinner culling destroys their light; see docs/tasks/fix-light-cull-crash.md if present) — report as risk.
2. Hook into `ArchiveBuilder.Build` result (add the report to the build result object; computing it must not change the output file)
   and print a compact summary in the CLI `build` and `apply` output:
   `Pack acceptance: 5 mods — Abyss 71/71 present, Verdant 24/25 (lost: fish Reech …), …` and with `--verbose` the full list.
   Add `--acceptance-json <file>` to `cgfmm build` writing the full report as JSON.
3. Docs: `docs/acceptance-report.md` explaining each kind and status.

## Tests
xUnit with synthetic archives/patches (see `CatalogArchiveTests`, `BuildingTests`, `UpgradesRuntimeTests` for builders):
added object present; added boat lost when a conflict picks the other mod (status Lost); fish relocated in catalog still Present;
room placement counts; table length risk; light-owner >5 risk. Real-file test opt-in (env `JAMM_ORIGINAL`, `JAMM_MODS_DIR`), skipped otherwise.

## Manual run (read-only inputs)
`dotnet run --project src/Jamm.Cli -c Release -- build --base ~/cgf-files/legacy/2023-08-16_manifest-9039707068725406600/data.win --order ~/cgf-files/mods/pack-inputs/Abyss.win,~/cgf-files/mods/pack-inputs/Verdant.win --out /tmp/acc-test.win`
Never write into ~/cgf-files.

## Rules
Read AGENTS.md. Jamm.Core must not write to console. Zero build warnings; `dotnet test -c Release` green.
Do NOT commit or push. Finish with a summary: files changed, test results, and the CLI summary line for Abyss+Verdant.
