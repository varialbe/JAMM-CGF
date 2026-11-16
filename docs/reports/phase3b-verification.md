# Phase 3b verification

Verified on `experimental` using the locally supplied August 2023 legacy base and
The Abyss, Cave Expansion and Colour Expansion. Inputs were opened read-only;
complete-file SHA-256 values were checked before and after verification. No game
content or personal input paths are included here. No commit or push was made.

## Implementation and container audit

Indexed base objects, sprites, sounds, backgrounds, paths, scripts, fonts,
timelines, rooms and shaders remain at their original positions. Removing one in a
patch cannot remove it from a build or reset the history of its surviving edits.
Definitions also continue to form unions. New assets append in introduction order.

The analysis decompiled all root entries from the 16 August base, the available
15 August public base and all three mods into temporary local files. Integer
assignments were inventoried by container and scope; values were checked against
source asset tables and related data or their uses. This distinguishes asset IDs
from prices, timing, coordinates, flags and gameplay catalog indices. In particular:

| Containers | Evidence for the selected type |
| --- | --- |
| `object` | Catalog names and boat entries identify the corresponding object at the source index. |
| `disp` | Boat and lure shop indices select the corresponding icon sprites. |
| `gear`, `item`, `message` | Equipment, tool/lure and notification arrays select matching objects; definitions are scoped to their data initialization entries. |
| `PATH`, scoped `path` | Fish and path-controller values select existing paths used by movement functions. |
| `dataobject`, `data_object`, `card_type` | Fish initialization and shop menus select their matching data and card objects. |
| `body_sprite`, `face_sprite`, `tube_sprite`, `harness_sprite`, `main_sprite`, `bg_sprite` and its variants | Values select the corresponding composite-fish and parallax sprites used by drawing code. |
| `thund_sound` | Source values select the thunder sound resources subsequently passed to audio playback. |
| Scoped `set`, `target_index`, `escort_target`, `stick_target`, `target`, `potential` | Uses identify object families; scope restrictions avoid typing unrelated flags and instance handles. |
| `fish_target_nearest_fish` argument 0 | The helper passes this argument to object-taking instance functions. |

RodData's literal arrays contain costs and movement statistics rather than asset
IDs. Spawn code primarily uses already typed instance-function arguments and the
movement/data containers above. No additional literal `spr`/`sprite` arrays were
found; composite sprite variables and shop `disp` arrays needed explicit types.
The checked-in JSON contains identifiers, families, scopes and short reasons only.

Definitions are registered through Underanalyzer's game-specific registry, without
editing the submodule. Each archive is decompiled in its own index space before
merging. Unchanged bytecode is also compared by its resolved meaning. Old v2 patch
code is normalized using its verified source tables; normal diff/check cache lookup
refreshes older type-definition revisions. Mixed revisions in portable checks
require regeneration with `diff --force`.

Cross-family name collisions require additional linking: Cave adds a sprite called
`Fourhead`, while Colour adds an object with that name. The GM1.4 compiler otherwise
chooses the wrong family. Temporary family-qualified symbols carry that distinction
through merging, and only those symbols are linked to final numeric indices before
compilation. They do not become archive variables. The semantic verifier detected
this failure before the fix; a synthetic regression now covers it.

## Semantic checks and limits

Source code is decompiled once per input model. The reference stream expected from
each selected hunk is assembled by the ordinary conflict plan, using source-resolved
names; discarded alternatives are accounted for by its printed decisions. Unchanged
base entries use their own base reference stream. All selected streams are checked
against compiled code in memory and again after serialization/reload. A missing
asset or wrong family/name fails before publishing the archive, identifying the
entry, container, expected identity and actual identity. This check is independent
of the compiled archive's raw indices and does not infer identity from numeric
ranges. Standalone `verify` lacks the source plan and cannot repeat provenance
comparison; rebuilding repeats it.

The literal audit examines all mod root entries after known types have been
resolved. It reports 617 remaining candidates: 214 for Abyss, 222 for Cave and 181
for Colour. Review identified ordinary amounts, coordinates, dimensions, timing,
array-slot keys and other non-asset values. The audit deliberately reports possible
families rather than rewriting these values. Computed IDs, aliased containers and
future untyped containers still require analysis and desktop testing.

## Build results

All commands completed successfully:

- `dotnet build -c Release`: zero warnings, zero errors.
- Without the real-file environment variables, `dotnet test --no-build -c Release`:
  155 passed, 5 intentionally skipped.
- With both environment variables, the complete Release suite: 160 passed, none skipped.
- The terminal-only grouping refinement was followed by the same build/unit checks
  and a repeat of the affected real compatibility/cache test.

Both default builds retain **objects/Akuma, objects/Angle, objects/Garder and
objects/Underfin**, removed by the supplied Cave archive. There are four removals
in these inputs. Every base object and sprite remains at its exact original index.
Both builds contain 897 objects, 742 sprites, 169 scripts and 2,133 root code entries;
all 2,133 selected/base reference streams pass in memory and after reload.

| Order / output in `/tmp/cgfmm-build/` | Size (bytes) | Source references requiring relocation | Decisions | Elapsed |
| --- | ---: | --- | ---: | ---: |
| Abyss → Cave → Colour / `forward.win` | 105,067,218 | Abyss 0, Cave 82, Colour 46 | 72 | 173.955 s |
| Colour → Cave → Abyss / `reverse.win` | 109,345,746 | Colour 0, Cave 82, Abyss 82 | 72 | 59.844 s |

Forward timing includes regeneration of all three pre-Phase-3b analysis caches;
reverse reuses those current-revision caches. Tests were running concurrently.
Relocation counts include changed/added source alternatives before conflict
selection; they are not a claim that discarded alternatives were compiled. The
selected reference stream is verified independently.

The 72 printed decisions comprise four indexed-asset retention notices, 18
base-definition retention notices and 50 conflict decisions: 18 code/list hunks,
9 literal-slot overlaps, 8 reference findings, 7 binary choices, 5 differing
same-name additions and 3 field choices. The complete local records are
`forward.log`, `reverse.log`, `phase3b-check.json`, `phase3b-index-audit.json` and
`phase3b-literals.json`. Generated patches, GML and reports containing game text
remain outside the checkout. The final input hash check passed for all five
supplied archives.

## Index comparison and gameplay decisions

The comparison was repeated for every object and sprite index in each input,
independently of decompilation. Counts below compare the asset name at the same raw
index in an input and output; these differences are expected when mod additions
share source indices. Every source object and sprite identity remains present.

| Input | Forward object differences | Forward sprite differences | Reverse object differences | Reverse sprite differences |
| --- | ---: | ---: | ---: | ---: |
| Base | 0 | 0 | 0 | 0 |
| Abyss | 0 | 0 | 104 | 108 |
| Cave | 102 | 98 | 102 | 98 |
| Colour | 53 | 51 | 0 | 0 |

All Abyss/Colour differences concern additions allocated after earlier mods'
additions. Cave's 102 object differences comprise 101 added identities and
`Elder_Mustardfish`, which Cave relocated from base index 183 to its source index
674; the merged archive correctly keeps it at base index 183. Its 98 sprite
differences concern additions. These categories account for every raw mismatch.
There are no missing source object/sprite identities and no unexplained semantic
reference mismatches. In particular, base `Bombat` remains at index 69. Abyss's
`Gawll` prey handle uses source index 661, remains 661 in forward order and correctly
relocates to 814 in reverse order.

Default choices retain Colour's catalog insertion hunks in forward order and
Abyss's in reverse order. The shared boat hunks select Cave forward and Abyss
reverse, since Colour does not participate in those edits. Accordingly, forward
slot `[2,58]` names and references `Brushick`; reverse names and references
`Overbite`. `Bombat` resolves correctly in both. Cave's `Fatneck` is checked in its
single-mod build and in an additional three-mod build explicitly selecting Cave's
catalog hunks. These checks distinguish an intentionally discarded insertion from
a wrong asset reference. A whole insertion-hunk choice can discard several slots,
including some slots that would otherwise be disjoint.

Abyss and Cave also claim identical boat and fish-catalog slots. `keep-both` can
retain both sequences of statements, but execution still overwrites identical
slots. Slot renumbering is not implemented. Six out-of-bounds texture-region
warnings remain in each default three-mod build; original geometry is preserved.
No desktop game launch or visual/gameplay validation was performed here.

## Other fixes and regression coverage

A linked parent of a game directory is supported after resolution. Links at
`data.win`, its permanent backup and its marker remain refused; protected input
aliases remain refused. Managed storage retains its stricter path confinement.
Managed installation signatures include the builder revision, so an earlier
installation cannot bypass Phase 3b rebuilding through `play`.

A single mod's unsupported binary payload is a WARNING; competing unsupported
versions remain CONFLICTs. Repeated code/list hunks are grouped in the terminal
compatibility table, while JSON and `--details` preserve individual hunks.

Synthetic regressions cover removal plus overlapping additions, unchanged bytecode
whose source index changed meaning, typed assignments and helper arguments,
untyped numbers/comments/strings, invalid/dangling IDs, a deliberately wrong
semantic index, a cross-family compiler collision, linked parents, protected input
aliases, old analysis caches and old managed installation signatures. Real tests
cover all three single-mod GML comparisons by resolved names, both default orders,
explicit Cave catalog selection and install/restore round trips.

## Independent review follow-up

All four review findings were confirmed against the implementation and fixed:

1. Audit payloads discovered through patch inputs were missing from the protected
   path list. `check` now resolves every audit payload before generating caches or
   writing a report, and protects those archives alongside the supplied inputs.
   Report destinations also reject original backups, installation markers and
   installed archives, including symbolic-link aliases.
2. Ignoring an indexed removal could retain an object, script or timeline while
   deleting its code. The builder now supplies reference snapshots from the exact
   base archive to the plan. Dependency retention follows the surviving snapshots,
   including retained base assets and surviving earlier edits, and preserves their
   required code even if every mod removed the referring asset.
3. Unresolved numeric built-in arguments were omitted from semantic verification.
   Literal argument checks now use the pinned decompiler's GameMaker signatures,
   including multiple asset arguments, conditional event signatures and the special
   variadic `script_execute` case. Invalid indices fail with the entry, function and
   argument position. Negative sentinels and computed expressions remain intact.
4. Patch generation filtered out bytecode whose asset indices changed meaning.
   When indexed asset tables differ, code is compared using resolved asset names
   even if its bytecode fingerprint is identical. Type-definition revision 2
   invalidates earlier diff caches. A regression compares portable `check`
   conflicts and build decisions in both load orders.

The additional synthetic regressions cover discovered archives with arbitrary
extensions, backups/markers and aliases; retained object/script/timeline code;
surviving code edits; built-in indices across all indexed families; negative
sentinels; nested calls and strings; decimal/hexadecimal/oversized literals; and
failure before publishing an invalid build. No review finding was skipped.

Final review verification:

- `dotnet build -c Release`: zero warnings and zero errors.
- `dotnet test -c Release --no-build` with the configured real-file variables:
  187 passed, none skipped (6 minutes 2 seconds).
- The same suite with both real-file variables unset: 182 passed, five expected
  opt-in skips.
- Real tests repeated all three single-mod builds, both three-mod orders, the
  explicit Cave catalog decision, archive verification and install/restore, and
  confirmed the original source hashes were unchanged.
- `git diff HEAD --check` passed. No submodule changes, staging or commits.
