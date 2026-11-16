# Checking whether mods work together

`cgfmm check` compares each mod with the exact same original game archive. It reads
archives and patches, writes a report if requested, and leaves every game file alone.
Use the legacy August 2023 bytecode build described in [legacy-version.md](legacy-version.md).
A successful check is a compatibility preview, not a guarantee that the combined game
will run correctly. The Phase 3 builder applies choices; playing the combination still requires desktop testing.

```sh
cgfmm check abyss.win cave.win colour.win --base original.win --json out/check.json
cgfmm check patches/abyss.cgfmm-patch.json patches/cave.cgfmm-patch.json \
  --base original.win --details '<asset name from the table>'
```

The argument order is the load order. Later mods are the default choice for binary
conflicts when building. `check` never silently resolves a reported conflict.
Mod inputs create or reuse patches in `./patches/`, or `patches/` beside the JSON
report. `--patches-dir <directory>` overrides this location. Existing patches need
only the base file for SHA-256 verification; their mod archives are not opened.
Every patch must target that exact base hash. Different timestamps alone do not
prevent analysis: Colour Expansion targets an older build and can still be diffed
against the common 16 August base.

## Reading the result

| Status | Meaning |
| --- | --- |
| CONFLICT | A decision is required: two changes disagree, a dependency is removed, or code could not be safely decompiled. |
| AUTO | Both edits can be kept automatically, for example different object fields. |
| INFO | The overlap is harmless, for example identical edits or duplicate additions. |
| WARNING | Analysis has a limitation or an input patch contains a notice. Read the explanation. |

The exit code is **0** when no conflicts were found, **3** when analysis succeeded
and found conflicts, and **1** for an input, output or usage error. Findings are
grouped by severity and sorted consistently. Counts refer to findings, not unique
assets: one code entry can contain multiple conflicting hunks (grouped into one
terminal row at its highest reported severity, with a total hunk count; JSON and
`--details` retain each hunk), and a three-mod
field overlap can produce a finding for each pair. The per-mod totals count patch
additions, changes and removals; they are independent of finding counts.

`--details <asset>` shows merged GML with markers around unresolved regions. For
fields it prints the base value and the participating mods' values beside each
other. `<absent>` differs from the existing JSON value `null`. Reference and index
findings list their source assets/code entries and line numbers. Treat reports as
local game content: details and JSON can contain decompiled code and game strings.
Do not commit or redistribute them.

## What can be kept together

Code changes use the original GML as their common ancestor. Edits at different
positions combine automatically; identical edits are kept once. Overlapping edits
with different text require a choice. All versions use ancestor coordinates in
load order, so a third mod never compares its code against conflict markers from
the first two. A same-name code addition has no ancestor: identical definitions are
harmless; differing definitions need a two-way choice. Metadata such as parent code
and argument/local counts is checked separately. Disassembly fallback is reported
as its own conflict and is never treated as GML.
Removing a final newline conflicts with an adjacent insertion, so automatic merges
cannot join separate lines or let a trailing comment consume inserted code.

Objects and rooms combine edits to different fields. Removing a field or instance
that another mod edits is a conflict. Events use event type and subtype; collision
events name the other object rather than its unstable archive index. Identical
handler references can be kept once; the actual handlers are merged through their
code entries. Different handler assignments need a decision. Room instances use
object name and position, so different additions form a union and identical
placements are included once. Different transforms at the same placement require
a decision. Tiles have matching keys including background, position, source
rectangle and depth. Layers, timeline moments and font glyphs have named/semantic
keys, avoiding unrelated additions colliding at the end of an array.

Sprites, texture pages, sounds, fonts, backgrounds, paths and timelines are treated
as whole-content choices when changed by multiple mods. Identical semantic content
is harmless; different content is a conflict. Unsupported texture markers are not
proof of equality. New texture pages use content hashes as identities, so unrelated
pages added at the same numeric archive position do not collide. Existing pages
retain numeric base identities. Atlas repacking can produce many existing-page
conflicts even when some sprite pixels are unchanged; inspect the sprite findings
too. Texture placement geometry is included in comparisons.
SWF and Spine payloads are hashed when available. Unknown or incomplete payloads
remain explicit findings even when exposed metadata is identical. A single mod
changing an unsupported payload produces a WARNING; multiple competing versions
produce a CONFLICT because equality cannot be established.

Same-name additions of named assets are prominent conflicts when their definitions
differ. Strings, variables and function additions form a union. Changed function
classification is retained and compared as fields. Named resource additions retain
their original positions for future insertion by the builder.

## Problems that are less obvious

A removed asset can still be used by another mod. Checks include object parent,
sprite, mask and collision references, event handlers, room instances and creation
code, scripts and timeline handlers. Patches store structured references, including
unchanged structured assets. Added/changed GML is tokenised to find identifiers
matching known sprite, object, sound, script and room names. Strings and comments
are excluded. An identifier can also be a local variable, so GML matches are
conservative and need human review. This is not a full GML type checker or runtime
analysis; dynamically constructed names and arbitrary indirect handles cannot be
proved safe.
Dependency checks use merged structured snapshots and omit removed source assets.
References that can survive an unresolved field or removal choice remain conditional
conflicts; choosing another alternative may eliminate them.

Two mods inserting different lines at the same ancestor position often extend a
shared fish catalog. This is a **CONFLICT (list)** with a suggested **keepBoth**
choice: keep distinct insertions in load order. It remains a conflict until someone
chooses that option; the inserted code might have order dependencies.

The Abyss and Cave Expansion both use boat slot 8 and fish catalog slots starting
at `[2,58]`. `keep-both` keeps inserted statements, but two writes to the same slot
still overwrite each other when executed. The later mod wins overlapping slots in
the merged handler unless you select another mod. Keeping both fish in distinct
catalog slots requires manual edits today; automatic slot renumbering is future work.
A default choice selects an entire overlapping insertion hunk, which may contain
several slots; inspect `--details` before choosing which insertion to keep.
This slot conflict is separate from asset IDs: Phase 3b preserves base indices and
links typed mod IDs to the intended named assets, but does not allocate new gameplay
slots or coordinate execution order across different handlers.

Literal writes such as `catalog[42] = ...`, `ds_list_set(catalog, 42, ...)` and
`ds_map_add(catalog, "key", ...)` are checked across code entries. Two mods writing
the same named container and literal index/key produce a **CONFLICT (index)**,
even in different handlers. Only writes touching added/changed lines count, including
edits or deletions anywhere in a multiline value expression.
Container names do not establish runtime identity or scope, so these are also
conservative findings. Expressions used as indices and aliased/dynamic container
names are outside this lexical check.

## Phase 3 handoff

The JSON report stores load order, complete findings, merged code and individual
hunks. Each unresolved code hunk retains base text, all participating versions,
available mod choices and, for shared insertions, the `keepBoth` text. These are
reusable decisions for Phase 3. Field previews apply changes in load order and
explicitly mark assets requiring a decision; they must not be used as approved
build results. Field findings preserve alternatives, including absent/null values.

Phase 3 provides the selection workflow and builds a new `data.win` from verified
base/mod hashes. It locates binary payloads in those exact local mod archives,
applies selected code and field resolutions, and preserves addition order. See
[building and installation](building.md) for commands, decisions and safety checks.
`check` itself remains read-only; launching belongs to Phase 4.

## Auditing numeric asset references

`cgfmm check cave.win --base original.win --asset-literals --json local-audit.json`
reports untyped integer candidates across all mod root code entries after known
CGF containers and built-in argument types have been resolved. Each candidate lists
its source entry, container, line, value and possible added/shifted asset families.
It returns 0 when the audit completes; these are review hints, not conflicts or
proof that ordinary numbers are asset IDs. Patch inputs in this mode also require
the exact local mod archive. Keep audit JSON local.

## Saving individual hunk choices

The GUI presents each overlapping code hunk separately. Its persisted key is
`code/<entry>#<start>`, where `start` is the report's zero-based ancestor line.
For example, use `profile resolve combined 'code/example_catalog#12' keep-both`
for an insertion and choose a participating mod for a replacement hunk in the same
entry. Qualified and bare whole-entry choices still work. Per-hunk choices override
whole-entry choices; unmatched keys are refused rather than silently ignored.
See [building choices](building.md#conflict-choices).
