# Texture atlas repacking

JAMM repacks the legacy atlas after asset merges, runtime patches and sprite relocations in builds with two or more enabled mods. Use `cgfmm build --no-repack` for a one-off comparison, or `cgfmm profile repack <name> off` to save that choice for a profile. The single-mod build path keeps its source atlas behavior.

## Format and packing

- The pinned UndertaleModLib reads direct TPAG pointers from sprite frame entries (`SPRT`), backgrounds (`BGND`), fonts (`FONT`), and embedded images (`EMBI`). Room tiles resolve through a background or sprite. Spine and SWF carry separate image data.
- The legacy Cat Goes Fishing archive has 34 embedded PNG pages and neither `TGIN` nor `EMBI`. Its neighboring source rectangles have gaps as small as four texels, and sampled opaque edges showed two texels of either extrusion or transparent padding. The repacker reserves two texels per side, keeps transparent-black source padding, and extrudes the region edge where another image or the source page boundary would otherwise supply the padding.
- A deterministic skyline packs within 2048 × 2048, with power-of-two page dimensions for partial layouts. Large regions get dedicated pages. SHA-256 of decoded BGRA8 pixels plus the region dimensions identifies duplicates within the same page flag. Duplicate regions share a source rectangle while each referenced TPAG keeps its own target and bounding fields. The repacker keeps original pages for live empty or out-of-bounds rectangles so the runner sees the original sampling geometry. GameMaker 2, external pages, and archives with texture-group metadata retain their atlases.
- Low VRAM downscaling is deferred. Room tiles and draw routines can sample in source-pixel coordinates. Font glyphs also use pixel coordinates, and a background's size or name cannot establish where gameplay or menus use it. Preserving collision masks alone would leave those behaviors unverified.

## Verification

The synthetic tests compare decoded pixels byte for byte across sprite frames, backgrounds and fonts, and compare collision-mask bytes. They also exercise shared pixels with different target offsets, discarded regions, dedicated pages, malformed rectangles, padding, deterministic output, and the profile switch.

To repeat the opt-in comparison, build the same ordered inputs twice with and without `--no-repack` and point the test at the saved outputs.

```sh
JAMM_UNPACKED_BUILD=/path/to/baseline.win JAMM_REPACKED_BUILD=/path/to/repacked.win \
  dotnet test -c Release --filter FullyQualifiedName~RealTextureBuildPairKeepsEveryReferencedItem
```

## All-seven VPS build

The comparison used TGE, Antiquity, PWB, Abyss, Colour, Verdant and Cave in that order, with the same sanitized patches and legacy base. The baseline was built from the pre-repack checkout of this branch. Both archives passed the builder's reload, reference and GML checks.

| Measure | Baseline | Repacked |
| --- | ---: | ---: |
| Texture pages | 417 | 119 |
| Estimated RGBA8 page memory | 3,400.92 MiB | 2,170.81 MiB |
| TPAG entries | 2,205 | 2,131 |
| Archive size | 207.27 MiB | 282.00 MiB |
| End-to-end build time | 181 s | 419 s |

```text
[CGF-TEX-001] Texture pages: 417 -> 119, 3566 MB -> 2276 MB (30 duplicates merged, 73 unused regions dropped). Preserved 5 source pages with empty or out-of-bounds rectangles.
```

Estimated resident texture memory dropped by 36.2%. The output archive grew by 36.1%. The observed build took about four minutes longer on the shared VPS. The jobs ran with three analysis workers and shared the machine with other work, so these wall times are indicative.

The opt-in comparison passed for all 2,131 live TPAG items. It matched row-wise SHA-256 digests of decoded BGRA8 pixels, every sprite/background/font TPAG reference, source dimensions, target and bounding fields, and collision-mask bytes. For the six pre-existing out-of-bounds rectangles, it compared the entire decoded source page and the original source coordinates.

`dotnet build -c Release` completed with zero warnings and errors. The normal Release suite passed 443 core and 127 GUI tests, with 16 opt-in tests skipped. The all-seven comparison passed in a separate opt-in run.

## VPS gameplay and screenshots

The Wine/Xvfb runs used the shared VPS lock and ran one game at a time. Each used seed 1337, a six-second warmup, a 75-second capture, and a 600-second runner budget. Steam API initialization required relaunches before the successful runs.

```sh
DUR=75 ~/jamm-night/bin/run.sh ~/jamm-night/texture-repack/all7-repacked.win tex-all7 dock 600
DUR=75 ~/jamm-night/bin/run.sh ~/jamm-night/texture-repack/all7-repacked.win tex-all7 deepwater 600
```

| Scene | Baseline result | Repacked result | Differing t60 pixels out of 1,024,000 | In-scene normalized mean absolute error |
| --- | --- | --- | ---: | ---: |
| Dock | RECORDED, 82 samples | RECORDED, 138 samples | 3,038 | 1.96% |
| Deepwater | RECORDED, 100 samples | RECORDED, 150 samples | 0 | 0.65% |

The game window appeared after t60 in all four runs, so those screenshots show the startup desktop. The supplementary in-scene screenshots were taken about 18 seconds after each game window appeared. They show the rendered scenes at different simulation frames. The decoded atlas comparison above checks pixel fidelity independently of scene timing.

The CSVs, screenshots and game logs are under `~/jamm-night/runs/tex-all7-{dock,deepwater}/` and `~/jamm-night/runs/tex-all7-baseline-{dock,deepwater}/`. Build logs and screenshot metrics are under `~/jamm-night/texture-repack/` on the VPS.
