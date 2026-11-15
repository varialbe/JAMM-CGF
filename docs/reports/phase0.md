# Phase 0 — feasibility check

Inputs were opened read-only. This report contains metadata, counts and hashes, with no extracted game code, dialogue, images or audio. A base verdict estimates similarity; it does not establish that a conflict-free merge is possible.

## Inspection

### data.win

Path: /home/varialbe/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Cat Goes Fishing/data.win

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

Path: /home/varialbe/Cat-Goes-Fishing-Mods/cave-expansion.win

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

- Fingerprint: texture item 'PageItem 1267' has source rectangle 440,455 600x280 outside its 1024x2048 texture page 'Texture 34'; hashed available pixels with transparent padding and an invalid-region marker.
- Fingerprint: texture item 'PageItem 1278' has source rectangle 10,2600 4100x1500 outside its 4096x8192 texture page 'Texture 36'; hashed available pixels with transparent padding and an invalid-region marker.
- Fingerprint: texture item 'PageItem 1281' has source rectangle 3666,351 475x475 outside its 4096x8192 texture page 'Texture 36'; hashed available pixels with transparent padding and an invalid-region marker.

### colour-expansion.win

Path: /home/varialbe/Cat-Goes-Fishing-Mods/colour-expansion.win

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

Path: /home/varialbe/Cat-Goes-Fishing-Mods/the-abyss-beta.win

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

- Fingerprint: texture item 'PageItem 1315' has source rectangle 0,0 1331x409 outside its 1330x409 texture page 'Texture 84'; hashed available pixels with transparent padding and an invalid-region marker.
- Fingerprint: texture item 'PageItem 1328' has source rectangle 0,0 7159x3131 outside its 7059x3131 texture page 'Texture 97'; hashed available pixels with transparent padding and an invalid-region marker.
- Fingerprint: texture item 'PageItem 1334' has source rectangle 0,0 306x216 outside its 306x213 texture page 'Texture 103'; hashed available pixels with transparent padding and an invalid-region marker.

## Comparison with original

### cave-expansion.win

Same GameMaker version (stored and detected): **NO**; same bytecode: **NO**; same GEN8 game name: **yes**.

| Asset type | Identical | Changed | Added | Removed |
| --- | ---: | ---: | ---: | ---: |
| code | 0 | 0 | 1926 | 0 |
| scripts | 0 | 167 | 1 | 341 |
| sprites | 0 | 483 | 103 | 26 |
| objects | 0 | 637 | 101 | 32 |
| rooms | 0 | 4 | 0 | 0 |
| sounds | 0 | 44 | 0 | 3 |
| texture pages | 0 | 45 | 54 | 0 |
| strings | 1650 | 0 | 5246 | 587 |
| variables | 0 | 0 | 3787 | 0 |
| functions | 0 | 0 | 364 | 0 |
| fonts | 0 | 10 | 0 | 0 |
| backgrounds | 0 | 0 | 2 | 0 |

**MERGING IMPOSSIBLE (no bytecode in YYC file): code N/A (original has 0 code entries); original assets unchanged 1650/4029 (40.95%).**

### colour-expansion.win

Same GameMaker version (stored and detected): **NO**; same bytecode: **NO**; same GEN8 game name: **yes**.

| Asset type | Identical | Changed | Added | Removed |
| --- | ---: | ---: | ---: | ---: |
| code | 0 | 0 | 1942 | 0 |
| scripts | 0 | 167 | 2 | 341 |
| sprites | 0 | 484 | 55 | 25 |
| objects | 0 | 642 | 52 | 27 |
| rooms | 0 | 4 | 0 | 0 |
| sounds | 0 | 44 | 0 | 3 |
| texture pages | 0 | 45 | 33 | 0 |
| strings | 1656 | 0 | 5230 | 581 |
| variables | 0 | 0 | 3847 | 0 |
| functions | 0 | 0 | 367 | 0 |
| fonts | 0 | 10 | 0 | 0 |
| backgrounds | 0 | 0 | 2 | 0 |

**MERGING IMPOSSIBLE (no bytecode in YYC file): code N/A (original has 0 code entries); original assets unchanged 1656/4029 (41.10%).**

### the-abyss-beta.win

Same GameMaker version (stored and detected): **NO**; same bytecode: **NO**; same GEN8 game name: **yes**.

| Asset type | Identical | Changed | Added | Removed |
| --- | ---: | ---: | ---: | ---: |
| code | 0 | 0 | 1921 | 0 |
| scripts | 0 | 167 | 1 | 341 |
| sprites | 0 | 483 | 113 | 26 |
| objects | 0 | 641 | 104 | 28 |
| rooms | 0 | 4 | 0 | 0 |
| sounds | 0 | 44 | 0 | 3 |
| texture pages | 0 | 45 | 143 | 0 |
| strings | 1654 | 0 | 5329 | 583 |
| variables | 0 | 0 | 3781 | 0 |
| functions | 0 | 0 | 364 | 0 |
| fonts | 0 | 10 | 0 | 0 |
| backgrounds | 0 | 0 | 2 | 0 |

**MERGING IMPOSSIBLE (no bytecode in YYC file): code N/A (original has 0 code entries); original assets unchanged 1654/4029 (41.05%).**

## Method and limits

Assets match by ordinal name, strings by value (SHA-256 identifiers in JSON), texture pages by index. Duplicate names/values retain multiplicity; exact fingerprints pair first. Overall unchanged percentage is identical/original across the twelve compared families, including strings; additions do not enter the denominator. The verdict uses only original code entries: at least 80% identical = LIKELY SAME BASE; 40% to below 80% = POSSIBLY DIFFERENT VERSION; below 40% = DIFFERENT BASE. YYC, empty CODE or unsupported bytecode cannot establish a mergeable base.

Fingerprints use SHA-256: code hashes UTML instruction disassembly with relative jumps and entry offset/argument count, without decompilation; sprites hash dimensions, origin, frame count, ordered decoded BGRA frame rectangles, placement geometry, masks and flags; objects hash sprite/parent/mask names, flags/physics and event types/subtypes/code names; rooms hash size, flags and ordered instance object names, positions/transforms and creation-code names; sounds hash flags, playback metadata and embedded audio bytes; texture pages hash embedded encoded texture data; scripts hash referenced code name/content and constructor flag; variables hash instance type; functions hash classification; fonts hash glyph metrics/kerning and texture region; backgrounds hash flags, texture region and tileset metadata. Variables/functions omit table IDs and usage chains. Atlas repacking may change texture-page results while leaving sprite regions identical.

This is a feasibility fingerprint, not an exhaustive serialized diff. Room layers/tiles, event action parameters, external audio files/groups and non-bitmap sprite formats are outside Phase 0's content model. External textures and non-bitmap sprite fingerprints fail explicitly. Out-of-bounds texture regions produce warnings and hash available pixels with transparent padding plus an invalid-region marker; those sprite matches have limited confidence. UTML's detected version is a format lower bound. Version/name mismatches are reported separately from the code similarity verdict.
