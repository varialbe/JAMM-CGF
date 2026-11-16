# Patch format, version 2

`cgfmm diff <original.win> <mod.win>` produces a UTF-8 JSON document containing
semantic changes against one exact base. Patches contain copyrighted decompiled
code and string values: keep them local. `*.cgfmm-patch.json` is ignored by Git.

The portable records and source-generated System.Text.Json context live in
`src/Jamm.Core/Patching/PatchModels.cs`. Reading a patch with `PatchJson.Deserialize`
does not open game files or use UndertaleModLib types. Only patch generation needs
the game loader, texture decoder and decompiler. Consumers may compile the portable
model/context source independently of UndertaleModLib.

## Envelope and identity

- `formatVersion`: integer schema version, currently `2`. Readers reject unsupported
  versions; regenerate with `--force`. Breaking changes to identities, field meanings,
  or required data require a new version. Additional optional fields may be introduced
  compatibly; readers should ignore unknown fields.
- `tool`: `cgfmm` and assembly version.
- `createdUtc`: UTC creation time, the only volatile field in forced reruns.
- `base`, `mod`: `fileName` (no directory), lowercase SHA-256 of the complete archive,
  `gen8Timestamp` (Unix seconds), `gmVersion` (stored GEN8 version), `bytecode`.
  The mod also has a display `name`, defaulting to the file stem.
- `warnings`: ordinally sorted, distinct read/fingerprint/decompilation notices.
- `changes`: ordinally sorted family names: `code`, `scripts`, `objects`, `rooms`,
  `sprites`, `texturePages`, `sounds`, `fonts`, `backgrounds`, `paths`, `timelines`,
  `strings`, `variables`, `functions`.

GameMaker stored/detected versions, branches and bytecode versions must match.
YYC archives cannot produce a patch. GEN8 timestamps may differ: Colour Expansion
uses an older base and its differences are handled normally.

## Changes and fields

Every family has `added`, `changed`, and `removed` arrays, sorted by matching key
using ordinal comparison. Identical assets and semantic no-ops are omitted.
Existing texture-page keys sort numerically. New pages sort by their original
source index. Added/changed entries have:

- `name`: case-sensitive asset name, or the special matching key described below.
- `mod`: full semantic field snapshot, with references resolved to resource names.
- `base`: full original snapshot for a changed entry; absent for an addition.
- `fields`: leaf changes `{ "path": "/depth", "base": 0, "mod": 100 }`.
  Paths are RFC 6901 JSON Pointers (`~` becomes `~0`, `/` becomes `~1`).
  Ordered arrays use zero-based indices. Named/semantic maps use escaped keys.
  Missing sides are omitted; explicit JSON `null` means an existing null field.
  An added/removed subtree is one field change. Whole-asset additions expose their
  top-level fields rather than a single empty pointer, including definitions and strings.
- `originalIndex`: original mod archive position for additions. Consumers sort by
  this field when inserting assets; display-name ordering is not insertion order.
- `baseCode`, `modCode`: code text records, only on code entries. `baseCode` is
  absent for added code.

Removed entries are strings naming the matching key. Their original definitions
remain available in the base archive identified by `base.sha256`.
Named assets with duplicate names are rejected rather than silently choosing an
ambiguous merge identity. Definitions and strings use set semantics as below.

## Family semantics

| Family | Semantic data and matching |
| --- | --- |
| Code | Full base/mod LF-normalized GML text for changed entries, mod text for additions. Each text record includes `format` (`gml` or `disassembly`), `localsCount`, `argumentsCount`, `parent`, and sorted `children` names. Entry snapshots also retain child `offset` and `weirdLocalFlag`. Fingerprints select candidates; decompiled text and metadata determine code changes. Only candidate entries are decompiled, with one lazy global context per archive. |
| Scripts | Code entry name and constructor flag. The Phase 0 comparison fingerprint also tracks referenced bytecode changes, but a semantic patch omits script no-ops when only referenced code changes; those changes are recorded in `code`. |
| Objects | Sprite/mask/parent names, depth, visibility, solid/persistent/managed flags, physics properties/vertices. `events` is a map keyed by `type:subtype`; collision subtype is the other object name (also stored as `otherObject`), not its archive index; duplicate event records concatenate ordered actions; values contain ordered action code names. Added/missing keys identify added/removed events. |
| Rooms | Caption, dimensions, speed, flags, background color, creation code, physics/world bounds, views, backgrounds and layers. `instances` is a map keyed by a JSON-encoded `[objectName,x,y]` tuple. Values are sorted multisets of transforms, color, animation and creation/precreate code names. Instance IDs are excluded. Moving an instance changes its identity and produces a removal/addition; transform edits at the same position produce field changes. `tiles` is a map keyed by the JSON tuple `[background,x,y,sourceX,sourceY,width,height,depth]`, containing sorted semantic tile multisets; layers are keyed by layer name with duplicate-name multisets; layer elements include tile/background/instance metadata. |
| Sprites | Dimensions, origin, bounding box, flags, playback metadata, frame count, collision-mask hashes. `frames` has indexed `contentHash` (decoded BGRA pixels) and `regionHash` (pixels plus placement geometry). `/frames/<index>/contentHash` identifies changed pixels. Each frame records source/target rectangles and bounding dimensions as `geometry`; missing frames/textures are null-safe. No images/masks are embedded. |
| Texture pages | Existing-page matching key is decimal base page index; `contentHash` hashes encoded page bytes and `sourceIndex` locates the mod page. New-page keys are `sha256:<content hash>`, with `sourceIndices` retaining all equivalent mod pages. Identical new pages collapse to one addition; `originalIndex` is the first source position. Unsupported pages retain separate `unsupported:<source index>` identities, because an unsupported marker cannot establish content equality. Atlas repacking can change pages without changing sprite pixels. |
| Sounds | Flags/effects, volume, pitch, preload, type/file/audio group name, embedded audio content hash (`audio`). External audio may be absent. |
| Fonts | Display name, metrics/ranges/flags, glyphs keyed by character (sorted multisets for duplicates), ordered kerning, texture region content hash and source/target/bounding geometry. |
| Backgrounds | Flags, texture region content hash and geometry, tileset dimensions/layout/tile IDs. |
| Paths | Smooth/closed flags, precision and ordered points with position/speed. |
| Timelines | Moments keyed by step (sorted multisets for duplicates), each with ordered action code entry names. |
| Strings | Set of actual string values; added entries contain `value`, removed entries are the values. Never changed; duplicate table entries and order are ignored. |
| Variables | Set of JSON-encoded `[name,instanceTypeName]` keys. Added snapshots retain numeric `instanceType`; removed keys preserve both name and type. Type changes are removal/addition. |
| Functions | Set of names, snapshots include classification; classification edits are recorded as changes. Usage chains, IDs and reference counts are excluded. |

Texture formats that the fingerprint layer cannot compare use `unsupported:...`
markers and warnings. Such markers are not proof of pixel equality. A per-entry
GML failure records a warning and disassembly text so the candidate remains visible;
code merging must inspect `format` and avoid treating disassembly as GML.
Child entries retain their own name and metadata. Their text is the parent's complete
decompiled stream (including nested functions), since UTML child entries do not own
an instruction stream; `parent` identifies that source scope. Root text is reused
within one archive. Fallback disassembly uses the same parent scope.

## Binary payloads and Phase 3

Pixels, collision masks, audio bytes and texture pages are **not** included in JSON.
Hashes identify content; the field name, asset key and frame/page index locate the
payload. Phase 3 must find the user's mod archive and verify its **entire-file**
SHA-256 equals `mod.sha256` before reading binary resources from that archive.
It must similarly verify the base before building. A filename or timestamp alone
is insufficient, and a binary hash does not identify a downloadable resource.
Keyed tile snapshots retain the fields needed for reconstruction; binary payloads
still require the verified source archive.

## Cache and determinism

An existing output is reusable only when `formatVersion`, `base.sha256` and
`mod.sha256` match. Cache lookup hashes files read-only without loading UTML or
decompiling. `--force` recomputes. Invalid/unsupported caches are recomputed.
`--name` can relabel a matching cache without reanalysis. Other than `createdUtc`,
forced reruns with the same inputs/name/tool produce identical JSON. Arrays with
semantic order (frames, points, actions, views, backgrounds, kerning) retain that order.
Layers, moments, glyphs, tiles and instances use semantic maps; duplicate identities
retain sorted multisets. Event maps, room instance maps, warnings and named change
lists are stable. All output writes use temporary-file replacement, with input/`.win`
destination safeguards.

`--json-summary` emits one JSON object on stdout (counts, patch path, reuse flag,
elapsed seconds and warnings); progress and optional `--show` text go to stderr.
The normal summary reports added/changed/removed counts for every family.
`--show` formats context-limited unified code hunks and JSON Pointer field changes
for added/changed entries. A removed entry is read from the verified base archive
to display its deletion (including GML for removed code). `LineDiff.Compare` exposes
reusable line edits for Phase 2.

## Portable conflict checks (Phase 2)

Version 2 changes event, layer, moment, glyph, tile and page identities and adds
texture geometry, original addition positions, reference catalogs and definition
field changes. Version 1 caches are rejected and regenerated rather than interpreted
with the new meanings. Sounds, fonts, objects, backgrounds and ordinary sprites use
the same snapshots for candidate fingerprints and patch fields. Scripts with no
semantic snapshot changes are omitted even when their bytecode fingerprint differs.

`baseAssets` maps named families to sorted base names. `references` contains typed
`{family,asset,targetFamily,target}` references from all structured mod assets
(objects, rooms, scripts, timelines), including unchanged assets. Together with GML
in added/changed code, these let `ConflictAnalyzer` check removed dependencies from
patches alone. It does not decompile the entire base to find unchanged GML references.
The portable patch models, field/line diffs and `Merging/` sources can be compiled
without UndertaleModLib. See [conflict rules](conflicts.md) for limitations and the
report's reusable hunk/resolution data. Generated reports contain game content and
must stay local, just like patches.
