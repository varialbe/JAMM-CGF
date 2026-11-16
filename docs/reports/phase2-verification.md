# Phase 2 verification

Verified on 16 November 2026 on `experimental`. No commit, push, git configuration
change or game input write was performed. Generated patches, reports and code details
were kept outside the repository. Only aggregate results and asset names appear here.

## Implementation and Phase 1 review

All ten review items were checked against the implementation. Shared semantic snapshots
now drive the relevant fingerprints; semantic no-ops are omitted. Function classification
edits, addition fields, null frames, numeric page ordering, named collision events,
semantic collection keys, keyed tiles, texture geometry, named audio groups and original
addition positions are covered by regressions. Line diff uses linear-space Hirschberg
LCS; disassembly is a separate condition and cannot enter GML merging.

Explicit cache relabeling and top-level `--verbose` already worked in the reviewed
code. Regression coverage was added; cache reuse also resets a previous custom label
when a later command requests the default name. Direct diff command parsing accepts
`--verbose`. The duplicated fingerprint/field projections were unified to prevent
future missing-field drift. Patch format 2 rejects format 1 caches for regeneration.

Real archives revealed identical bytes in several added texture pages. Those pages
now have one content-addressed addition with all source indices retained. This avoids
ambiguous duplicate identities without discarding insertion/source information.
Unsupported pages remain separate: equal unsupported markers never establish equality.

## Checks

- `dotnet build -c Release`: zero warnings and errors in the solution.
- Tests without the real-file environment: 80 passed, 3 skipped, 0 failed.
- Tests with both real-file variables: 83 passed, 0 skipped, 0 failed.
- The portable models, line/field diff and merging sources also compile in an isolated
  .NET 10 project with no UndertaleModLib reference, with zero warnings/errors.
- SHA-256 values before/after verification match for the common base, all three mods
  and the 15 August base. The submodule is unchanged.
- Existing patches with the 15 August archive selected as `--base` fail with exit 1,
  explaining the selected and targeted base hashes. Synthetic tests also cover mixed
  patch bases, old schema rejection/regeneration, cache labels and output protection.

## Real-file findings and timings

The table includes all six three-mod orders and both orders of every pair.
Every run completed successfully with exit **3** (conflicts found). Times measure
the CLI-reported analysis time using reusable patches, including input hashing.
Runs shared the machine with real-file tests; these are observations, not isolated benchmarks.
Counts are findings, not distinct assets. A single asset may have multiple code
hunks or per-pair field findings; automatic subchanges remain visible alongside
conflicting subchanges.

| Load order | CONFLICT | AUTO | INFO | WARNING | Seconds |
| --- | ---: | ---: | ---: | ---: | ---: |
| Abyss → Cave → Colour | 52 | 7 | 13 | 6 | 11.567 |
| Abyss → Colour → Cave | 52 | 7 | 13 | 6 | 13.954 |
| Cave → Abyss → Colour | 52 | 7 | 13 | 6 | 12.098 |
| Cave → Colour → Abyss | 52 | 7 | 13 | 6 | 17.906 |
| Colour → Abyss → Cave | 52 | 7 | 13 | 6 | 14.016 |
| Colour → Cave → Abyss | 52 | 7 | 13 | 6 | 15.572 |
| Abyss → Cave | 27 | 4 | 6 | 6 | 6.781 |
| Abyss → Colour | 15 | 4 | 4 | 3 | 4.693 |
| Cave → Abyss | 27 | 4 | 6 | 6 | 7.404 |
| Cave → Colour | 30 | 4 | 8 | 3 | 4.683 |
| Colour → Abyss | 15 | 4 | 4 | 3 | 4.128 |
| Colour → Cave | 30 | 4 | 8 | 3 | 4.375 |

Patch generation was measured separately with `diff --force --json-summary`:

| Mod | Seconds | Code added | Code changed | Code removed |
| --- | ---: | ---: | ---: | ---: |
| the-abyss-beta | 35.455 | 93 | 10 | 0 |
| cave-expansion | 49.262 | 98 | 21 | 0 |
| colour-expansion | 25.052 | 115 | 20 | 1 |

Abyss and Cave each report three out-of-range texture-region warnings; Colour
reports none. These markers do not establish pixel equality. There were no GML
decompilation fallback findings for these three real mods.

Frequently affected code entries are `gml_Object_CatalogData_Create_0`,
`gml_Object_Boat7_Create_0`, `gml_Object_BoatData_Create_0` and
`gml_Object_RodData_Create_0`. Same-name additions include `Boat8`, `FossilG`,
`spr_boat_icon8`, `spr_firebolt` and `spr_sign`. `NewWorld` has conflicting
field edits in every pair. Literal boat and rod slot collisions are reported
separately. Removed-reference findings include `gml_Object_Meteor_Create_0`
and several objects removed by Cave.

For all three mods, the 52 conflicts comprise 10 overlapping code-hunk findings,
8 shared-insertion findings, 9 literal-slot findings, 10 removed-reference findings,
5 same-name additions, 7 binary/content choices and 3 room-field findings.

## Three `--details` spot checks

1. `gml_Object_BoatData_Create_0`: real conflict. Abyss and Cave both extend the
   same boat catalog position and write different definitions into slot 8. The
   preview retains both versions and offers `keepBoth`, but keeping both alone
   would still overwrite that slot; the separate index findings require a decision.
2. `Boat8`: real same-name object conflict. The mods use different sprites and
   creation handlers, so treating the additions as the same definition is unsafe.
3. `NewWorld`: real field conflicts. The mods choose different room dimensions.
   Shared removals/identical placements are separately identified as harmless;
   independent instance edits still receive automatic merge findings.

These are inspection judgments, not gameplay tests. Raw details were kept local
because they contain game code and values.

## What works and what remains

`check` accepts archives, cached patches or a mixture, verifies the selected base,
preserves load order, writes deterministic findings and reusable code-hunk choices,
and supports field/code details. Non-overlapping code and field edits combine;
same-name, binary, removal/use, list and literal-slot collisions are visible.

Phase 2 does not build, install or launch a merged archive. Phase 3 must provide
the decision workflow, apply choices, obtain binary payloads from hash-verified mod
archives and handle any required slot/resource remapping. Lexical references and
container names are conservative: dynamic names, aliases, runtime scope and
unchanged GML outside the patch are not exhaustively analysed. `keepBoth` is a
proposal requiring selection, not a promise of runtime compatibility.

## Independent Phase 2 review fixes

All four findings were verified and fixed; none were false positives.

- Removing the final newline now overlaps adjacent insertions. Regressions cover
  the reported example, trailing comments and both load orders.
- Literal-write ranges include complete multiline values, nested expressions and
  multiline strings. Deleted value lines count as edits; unrelated following
  statements do not become slot collisions.
- Complete SWF and Spine payloads contribute hashes to sprite snapshots. Unknown
  payloads remain explicit unresolved changes even when projected fields match,
  and require a decision even if only one mod changes the asset. Portable patch
  round trips retain these conditions without exporting payload contents.
- Dependencies use merged structured snapshots and omit removed source assets.
  Unresolved field/removal alternatives retain conditional dependency findings.
  Regressions cover both load orders and independently eliminated references in
  snapshots that also have conflicting fields.

Verification after these fixes: `dotnet build -c Release` completed with zero
warnings/errors; `dotnet test -c Release --no-build` passed **102 tests**, with
**0 failures and 0 skips**, using the configured real-file environment. SHA-256
values for the common base and all three mod inputs matched before/after testing.
No commit was made. The earlier findings/timings above describe the original
verification run; their counts were not remeasured as a benchmark after these fixes.
