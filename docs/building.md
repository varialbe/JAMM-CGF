# Building, installing and verifying a merged game

Use the legacy GM1.4 bytecode archives described in [legacy-version.md](legacy-version.md).
Keep the original and mod files outside the checkout. The builder opens inputs read-only,
starts from a fresh base model and publishes a verified archive to a separate output path.

```sh
cgfmm build --base original.win --order abyss.win,cave.win,colour.win \
  --out build/merged.win
cgfmm build --base original.win --order abyss.win,cave.win \
  --resolve resolutions.json --out build/chosen.win
cgfmm build --base original.win --order abyss.win,cave.win --dry-run
cgfmm verify build/merged.win
```

`--order` accepts one or more comma-separated `.win` or `.cgfmm-patch.json` paths.
Quote the complete value when paths contain spaces. Its default output is `./build/data.win`.
A previous build output can be replaced after the new archive passes verification.
The output cannot alias a base/mod input, patch, resolution file, installed archive or
original backup. This also protects paths reached through symbolic links. A dry run merges, checks references and compiles entirely in memory;
it creates no output, patch cache, directory, backup or marker. It does not perform the
serialization/reload check that requires writing an archive.

Normal `.win` builds create/reuse patches in `patches/` beside the output. Every patch
must name the selected base's entire-file SHA-256. For patch inputs the exact mod archive
must also be available: the builder searches beside the patch, its parent directory, the
working directory and the `.win` files in `JAMM_MODS_DIR`. It accepts a candidate only if
its entire-file SHA-256 equals `mod.sha256`. Renaming a file does not bypass verification.
Patches contain GML and strings but no binary payloads. Keep patches and logs local.

## Conflict choices

Later participating mods win by default. Non-overlapping GML hunks and object/room fields
combine; identical additions are included once. Paths, timeline moments and font fields
also apply their semantic field changes. Sprites, sounds, backgrounds and texture-page
choices select complete binary versions. Every conflict decision is printed.

A resolution file is a JSON object mapping a case-sensitive `family/name` or asset name
to a mod's display name (normally its filename without the extension):

```json
{
  "objects/example_object": "cave-expansion",
  "code/example_code_entry": "keep-both"
}
```

These are illustrative names; use actual names from `cgfmm check`. A selected mod wins
overlapping changes while other disjoint changes remain. Selecting an unchanged mod
for a base asset retains that base asset. Unknown names/choices are errors. `keep-both`
works only when all conflicting code hunks in the entry are insertions at the same
ancestor position. It retains distinct insertions in load order. It cannot resolve an
overlapping replacement, deletion or differing whole-code addition. A qualified key selects exactly one family. Bare names apply only to conflicting
families when names are shared; bare `keep-both` applies only to code. Qualify keys to
resolve families independently. Use unique mod names when preparing patches with `diff --name`.

Deletion/reference conflicts use the final structured snapshot. A later surviving
reference retains an earlier-deleted base asset and its surviving edits, with a printed decision. An explicit
choice can also retain a referenced base asset. A surviving reference to an asset whose
deletion wins is an error: adjust the referring asset or the target's resolution.
Literal container/index collisions are conservative findings from Phase 2. The log
identifies the later participant, but a build cannot control execution order between
unrelated handlers. Check these in the game; successful verification does not prove
that fish catalogs, prices or other runtime lists agree.

Strings, variables and functions form unions. Base definitions are never removed,
even when a patch removes them; the log records those decisions. Duplicate string-table
entries and compiler-local bookkeeping are not semantic assets, so their raw counts
can differ from a source mod.

## Binary resources and code

The builder copies verified source texture pages/items, masks and embedded audio,
translating named resource references into the merged tables. It keeps the source atlas
placement rather than repacking. Multi-mod builds can contain additional texture pages
and page items so that unchanged base sprites and selected mod sprites keep their own
pixels. A single-mod build uses that mod's complete binary tables and matches its page,
page-item and named-asset counts.

New named assets retain their mod's original addition order. Collision event targets
are resolved by object name after building the final object table. Room instance/tile
IDs preserve base identities and allocate fresh IDs for additions. Removing indexed
assets triggers recompilation of surviving base code as well as changed, merged and
added GML, so the compiler can link resource names to the resulting indices. Compiler
errors name the entry and stop before writing an output archive.

A normal build writes a temporary archive, reloads it through UndertaleModLib, checks
the GameMaker/bytecode version and the plan's asset presence, validates resource/string
and texture-page references, decompiles every root code entry, and recompiles all that
GML in memory. Only a passing temporary file becomes the output. `verify` performs the
same reload/reference/decompile/compile checks on a supplied file without rewriting it.
Both commands return 1 on failure and 0 on success. `--verbose` includes diagnostic
exception details; normal errors omit stack traces.

## Install and restore

Use a **separate copy of the whole legacy game folder**, including its old executable.
Installation into the selected base game folder is also supported: its original `data.win`
is backed up and verified before replacement. Mod inputs remain protected. For subsequent
builds, use the verified `data.win.cgfmm-original` as `--base`, or restore first.

```sh
cgfmm build --base original.win --order abyss.win,cave.win \
  --out build/installed.win --install legacy-game-copy
cgfmm restore --game legacy-game-copy
```

Before replacement, installation accepts only the exact selected base or a recognized
previous cgfmm build. The marker `data.win.cgfmm-build.json` records the base, build and
backup SHA-256 values. Modified builds, mismatched backups, unrecognized archives and
symbolic-link installation paths are refused.

The first installation copies `data.win` to `data.win.cgfmm-original` and verifies the
copy by SHA-256 **before** replacing anything. The backup is never overwritten or
deleted, including during subsequent installs and restores. Copies use temporary-file
replacement and their hashes are verified. Restore checks the backup against its marker
when present, restores and verifies `data.win`, removes the obsolete build marker, and
keeps the original backup. A manually retained backup without a marker can also be
restored; the command verifies the copy but cannot establish its historical provenance.
An interrupted marker write may require restore before another install; the original
backup is retained.

## Known limits and desktop testing

The supported reconstruction target is the documented legacy GM1.4 game. GMS2 room
layers and nested child-code scopes are rejected with a clear error instead of partially
reconstructed. YYC archives have no mergeable bytecode. External audio files and the
legacy executable are not packaged or installed by the builder.

Some supplied mods already contain out-of-bounds sprite rectangles and an empty null
texture item. Empty placeholders are valid; out-of-bounds source geometry is preserved
and reported as a warning. Missing non-null texture/resource references fail verification.
These warnings require visual checks in the game.

A verified archive can still have gameplay conflicts: decompiled code is not a full
runtime specification, dynamic resource names and numeric IDs may evade lexical checks,
and preserving multiple catalog insertions can require manual game-specific work.
An in-game test on the desktop with the legacy executable is still required. This
machine cannot launch the game. See [Phase 3 verification](reports/phase3-verification.md)
for build sizes, counts, decisions and timings.
