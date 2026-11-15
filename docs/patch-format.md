# Patch format, version 1

`cgfmm diff <original.win> <mod.win>` produces a UTF-8 JSON document containing
semantic changes against one exact base. Patches contain copyrighted decompiled
code and string values: keep them local. `*.cgfmm-patch.json` is ignored by Git.

The portable records and source-generated System.Text.Json context live in
`src/Jamm.Core/Patching/PatchModels.cs`. Reading a patch with `PatchJson.Deserialize`
does not open game files or use UndertaleModLib types. Only patch generation needs
the game loader, texture decoder and decompiler. Consumers may compile the portable
model/context source independently of UndertaleModLib.

## Envelope and identity

- `formatVersion`: integer schema version, currently `1`. Readers reject unsupported
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
using ordinal comparison. Identical assets are omitted. Added/changed entries have:

- `name`: case-sensitive asset name, or the special matching key described below.
- `mod`: full semantic field snapshot, with references resolved to resource names.
- `base`: full original snapshot for a changed entry; absent for an addition.
- `fields`: leaf changes `{ "path": "/depth", "base": 0, "mod": 100 }`.
  Paths are RFC 6901 JSON Pointers (`~` becomes `~0`, `/` becomes `~1`).
  Arrays use zero-based indices. Missing sides are omitted; explicit JSON `null`
  means an existing null field. An added/removed subtree is one field change.
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
| Scripts | Code entry name and constructor flag. The existing comparison fingerprint also tracks referenced bytecode changes, so a script can be recorded with an empty `fields` array when only its code changes. |
| Objects | Sprite/mask/parent names, depth, visibility, solid/persistent/managed flags, physics properties/vertices. `events` is a map keyed by `type:subtype`; values contain ordered action code names. Added/missing keys identify added/removed events. |
| Rooms | Caption, dimensions, speed, flags, background color, creation code, physics/world bounds, views, backgrounds and layers. `instances` is a map keyed by a JSON-encoded `[objectName,x,y]` tuple. Values are sorted multisets of transforms, color, animation and creation/precreate code names. Instance IDs are excluded. Moving an instance changes its identity and produces a removal/addition; transform edits at the same position produce field changes. `tiles` has count and semantic content hash; layer elements include tile/background/instance metadata. |
| Sprites | Dimensions, origin, bounding box, flags, playback metadata, frame count, collision-mask hashes. `frames` has indexed `contentHash` (decoded BGRA pixels) and `regionHash` (pixels plus placement geometry). `/frames/<index>/contentHash` identifies changed pixels. No images/masks are embedded. |
| Texture pages | Matching key is decimal page index; `contentHash` hashes encoded page bytes. Atlas repacking can change pages without changing sprite pixels. |
| Sounds | Flags/effects, volume, pitch, preload, type/file/group, embedded audio content hash (`audio`). External audio may be absent. |
| Fonts | Display name, metrics/ranges/flags, ordered glyph metrics/kerning, texture region hash. |
| Backgrounds | Flags, texture region hash, tileset dimensions/layout/tile IDs. |
| Paths | Smooth/closed flags, precision and ordered points with position/speed. |
| Timelines | Ordered moments with step and action code entry names. |
| Strings | Set of actual string values; added entries contain `value`, removed entries are the values. Never changed; duplicate table entries and order are ignored. |
| Variables | Set of JSON-encoded `[name,instanceTypeName]` keys. Added snapshots retain numeric `instanceType`; removed keys preserve both name and type. Type changes are removal/addition. |
| Functions | Set of names, added snapshots include classification; never changed. Usage chains, IDs and reference counts are excluded. |

Texture formats that the fingerprint layer cannot compare use `unsupported:...`
markers and warnings. Such markers are not proof of pixel equality. A per-entry
GML failure records a warning and disassembly text so the candidate remains visible;
future code merging must inspect `format` and avoid treating disassembly as GML.
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
Tile summaries also require reading the verified source archive to reconstruct tiles.

## Cache and determinism

An existing output is reusable only when `formatVersion`, `base.sha256` and
`mod.sha256` match. Cache lookup hashes files read-only without loading UTML or
decompiling. `--force` recomputes. Invalid/unsupported caches are recomputed.
`--name` can relabel a matching cache without reanalysis. Other than `createdUtc`,
forced reruns with the same inputs/name/tool produce identical JSON. Arrays with
semantic order (frames, glyphs, points, actions, views, backgrounds, layers) retain
that order. Event maps, room instance maps, warnings and named change lists are
stable. All output writes use temporary-file replacement, with input/`.win`
destination safeguards.

`--json-summary` emits one JSON object on stdout (counts, patch path, reuse flag,
elapsed seconds and warnings); progress and optional `--show` text go to stderr.
The normal summary reports added/changed/removed counts for every family.
`--show` formats context-limited unified code hunks and JSON Pointer field changes
for added/changed entries. A removed entry is read from the verified base archive
to display its deletion (including GML for removed code). `LineDiff.Compare` exposes
reusable line edits for Phase 2.
