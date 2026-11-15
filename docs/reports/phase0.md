# Phase 0 — feasibility check

Inputs were opened read-only. This report contains metadata, counts and hashes, with no extracted game code, dialogue, images or audio. A base verdict estimates similarity; it does not establish that a conflict-free merge is possible.

## Conclusion

data.win is YYC (no bytecode), detected GameMaker 2023.6.0.0, built 2024-08-05 UTC. It cannot supply code for merging.

The inspected mods use legacy GameMaker 1.4 bytecode 16. They need a matching legacy data.win and Cat Goes Fishing.exe runner. See [Getting the bytecode version](../legacy-version.md).

Mod code is approximately 93% identical between mods (directional range 92.28–93.91%). **Follow-up (legacy base found):** the 16 Aug 2023 build (depot 343782, manifest `9039707068725406600`) is the base of The Abyss and Cave Expansion (identical GEN8 timestamp) and matches every mod at 98.85–99.45 % identical code. See [phase0-legacy.md](phase0-legacy.md) and [Getting the bytecode version](../legacy-version.md).

## Inspection

### data.win

File: data.win

| Field | Value |
| --- | --- |
| Size (bytes) | 32716940 |
| SHA-256 | 04a5e1830b94ee4858509cbe499f8908bb0d726747ac21a9408e0be66bd24b25 |
| GEN8 game name | Cat_Goes_Fishing |
| Display name | Cat Goes Fishing |
| GameMaker GEN8 | 2.0.0.0 |
| UTML detected version | 2023.6.0.0 (LTS2022_0; format lower bound) |
| Bytecode | 17 |
| Build (GEN8) | 0 |
| Timestamp (Unix seconds) | 1722850316 |
| Compilation | YYC / no bytecode: merging is impossible (no bytecode) |

| Asset type | Count |
| --- | ---: |
| code | 0 |
| scripts | 508 |
| sprites | 509 |
| objects | 669 |
| rooms | 4 |
| sounds | 47 |
| backgrounds | 0 |
| texture pages | 45 |
| texture page items | 1327 |
| strings | 2237 |
| variables | 0 |
| functions | 0 |
| fonts | 10 |
| shaders | 1 |
| paths | 114 |
| timelines | 1 |
| extensions | 1 |

Warnings (UTML and fingerprint notices): 0.


### cave-expansion.win

File: cave-expansion.win

| Field | Value |
| --- | --- |
| Size (bytes) | 58773638 |
| SHA-256 | ef9241b4d3415414b4b3a33e2476d53fbcbea2d1bca79ff3d35f63a680971842 |
| GEN8 game name | Cat_Goes_Fishing |
| Display name | Cat Goes Fishing |
| GameMaker GEN8 | 1.0.0.9999 |
| UTML detected version | 1.0.0.9999 (Pre2022_0; format lower bound) |
| Bytecode | 16 |
| Build (GEN8) | 9999 |
| Timestamp (Unix seconds) | 1692207312 |
| Compilation | VM (bytecode available) |

| Asset type | Count |
| --- | ---: |
| code | 1926 |
| scripts | 168 |
| sprites | 586 |
| objects | 738 |
| rooms | 4 |
| sounds | 44 |
| backgrounds | 2 |
| texture pages | 99 |
| texture page items | 1515 |
| strings | 6896 |
| variables | 3787 |
| functions | 364 |
| fonts | 10 |
| shaders | 0 |
| paths | 113 |
| timelines | 1 |
| extensions | 0 |

Warnings (UTML and fingerprint notices): 3.

- Fingerprint: 'PageItem 1267' is unsupported (out-of-range region); content was not compared.
- Fingerprint: 'PageItem 1278' is unsupported (out-of-range region); content was not compared.
- Fingerprint: 'PageItem 1281' is unsupported (out-of-range region); content was not compared.

### colour-expansion.win

File: colour-expansion.win

| Field | Value |
| --- | --- |
| Size (bytes) | 56770470 |
| SHA-256 | 49c7bc3226c1aaf7f49aed8ddd9ca31b251e2c509250a2d29032bb77dde0b822 |
| GEN8 game name | Cat_Goes_Fishing |
| Display name | Cat Goes Fishing |
| GameMaker GEN8 | 1.0.0.9999 |
| UTML detected version | 1.0.0.9999 (Pre2022_0; format lower bound) |
| Bytecode | 16 |
| Build (GEN8) | 9999 |
| Timestamp (Unix seconds) | 1692057761 |
| Compilation | VM (bytecode available) |

| Asset type | Count |
| --- | ---: |
| code | 1942 |
| scripts | 169 |
| sprites | 539 |
| objects | 694 |
| rooms | 4 |
| sounds | 44 |
| backgrounds | 2 |
| texture pages | 78 |
| texture page items | 1412 |
| strings | 6886 |
| variables | 3847 |
| functions | 367 |
| fonts | 10 |
| shaders | 0 |
| paths | 117 |
| timelines | 1 |
| extensions | 0 |

Warnings (UTML and fingerprint notices): 0.


### the-abyss-beta.win

File: the-abyss-beta.win

| Field | Value |
| --- | --- |
| Size (bytes) | 76402070 |
| SHA-256 | 44f4c296edde8e4629b7e2d1cea1db4392b1408e32f6ebae99e2f401baffd188 |
| GEN8 game name | Cat_Goes_Fishing |
| Display name | Cat Goes Fishing |
| GameMaker GEN8 | 1.0.0.9999 |
| UTML detected version | 1.0.0.9999 (Pre2022_0; format lower bound) |
| Bytecode | 16 |
| Build (GEN8) | 9999 |
| Timestamp (Unix seconds) | 1692207312 |
| Compilation | VM (bytecode available) |

| Asset type | Count |
| --- | ---: |
| code | 1921 |
| scripts | 168 |
| sprites | 596 |
| objects | 745 |
| rooms | 4 |
| sounds | 44 |
| backgrounds | 2 |
| texture pages | 188 |
| texture page items | 1419 |
| strings | 6983 |
| variables | 3781 |
| functions | 364 |
| fonts | 10 |
| shaders | 0 |
| paths | 112 |
| timelines | 1 |
| extensions | 0 |

Warnings (UTML and fingerprint notices): 3.

- Fingerprint: 'PageItem 1315' is unsupported (out-of-range region); content was not compared.
- Fingerprint: 'PageItem 1328' is unsupported (out-of-range region); content was not compared.
- Fingerprint: 'PageItem 1334' is unsupported (out-of-range region); content was not compared.

## Comparison with original

### cave-expansion.win

Same GameMaker version (stored and detected): **NO**; same bytecode: **NO**; same GEN8 game name: **yes**.

| Asset type | Identical | Changed | Added | Removed |
| --- | ---: | ---: | ---: | ---: |
| sprites | 0 | 483 | 103 | 26 |
| objects | 0 | 637 | 101 | 32 |
| rooms | 0 | 4 | 0 | 0 |
| sounds | 0 | 44 | 0 | 3 |
| texture pages | 0 | 45 | 54 | 0 |
| strings | 1650 | 0 | 5246 | 587 |
| fonts | 0 | 10 | 0 | 0 |
| backgrounds | 0 | 0 | 2 | 0 |

**NOT COMPARABLE: data.win is YYC (no bytecode)**

### colour-expansion.win

Same GameMaker version (stored and detected): **NO**; same bytecode: **NO**; same GEN8 game name: **yes**.

| Asset type | Identical | Changed | Added | Removed |
| --- | ---: | ---: | ---: | ---: |
| sprites | 0 | 484 | 55 | 25 |
| objects | 0 | 642 | 52 | 27 |
| rooms | 0 | 4 | 0 | 0 |
| sounds | 0 | 44 | 0 | 3 |
| texture pages | 0 | 45 | 33 | 0 |
| strings | 1656 | 0 | 5230 | 581 |
| fonts | 0 | 10 | 0 | 0 |
| backgrounds | 0 | 0 | 2 | 0 |

**NOT COMPARABLE: data.win is YYC (no bytecode)**

### the-abyss-beta.win

Same GameMaker version (stored and detected): **NO**; same bytecode: **NO**; same GEN8 game name: **yes**.

| Asset type | Identical | Changed | Added | Removed |
| --- | ---: | ---: | ---: | ---: |
| sprites | 0 | 483 | 113 | 26 |
| objects | 0 | 641 | 104 | 28 |
| rooms | 0 | 4 | 0 | 0 |
| sounds | 0 | 44 | 0 | 3 |
| texture pages | 0 | 45 | 143 | 0 |
| strings | 1654 | 0 | 5329 | 583 |
| fonts | 0 | 10 | 0 | 0 |
| backgrounds | 0 | 0 | 2 | 0 |

**NOT COMPARABLE: data.win is YYC (no bytecode)**

## Method and limits

Assets match by ordinal name, strings by value (SHA-256 identifiers in JSON), texture pages by index. Duplicate names/values retain multiplicity; exact fingerprints pair first. Overall unchanged percentage is identical/original over named assets only (code, scripts, sprites, objects, rooms, sounds, fonts and backgrounds); strings and texture pages are reported separately and additions do not enter the denominator. The verdict uses only original code entries: at least 80% identical = LIKELY SAME BASE; 40% to below 80% = POSSIBLY DIFFERENT VERSION; below 40% = DIFFERENT BASE. YYC, empty CODE or unsupported bytecode cannot establish a mergeable base: code, scripts, variables and functions are skipped if either input lacks supported bytecode, and no unchanged percentage or base verdict is emitted.

Fingerprints use SHA-256: code hashes UTML instruction disassembly with relative jumps and entry offset/argument count, without decompilation; sprites hash dimensions, origin, frame count, ordered decoded BGRA frame rectangles, placement geometry, masks and flags; objects hash sprite/parent/mask names, flags/physics and event types/subtypes/code names; rooms hash size, flags, ordered instances/transforms/creation-code names, legacy tiles and GMS2 layer metadata with instance, tile and background elements (plus legacy tiles and sprites in asset layers); sounds hash flags, playback metadata and embedded audio bytes; texture pages hash embedded encoded texture data; scripts hash referenced code name/content and constructor flag; variables hash instance type; functions hash classification; fonts hash glyph metrics/kerning and texture region; backgrounds hash flags, texture region and tileset metadata. Variables/functions omit table IDs and usage chains. Atlas repacking may change texture-page results while leaving sprite regions identical.

This is a feasibility fingerprint, not an exhaustive serialized diff. Event action parameters, sequences, particles, text/effect layers and external audio files/groups are outside Phase 0's content model. External textures, Spine/SWF sprites and out-of-range texture regions produce warnings and deterministic unsupported:&lt;reason&gt; fingerprints without aborting the file. Equal unsupported markers do not establish equal content. Scripts referencing code absent from the CODE table use a stable missing-code marker and a warning. Only one decoded texture page is cached at a time. UTML's detected version is a format lower bound. Version/name mismatches are reported separately from the code similarity verdict.
