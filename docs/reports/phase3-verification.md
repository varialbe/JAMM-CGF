# Phase 3 verification

Verified on 16 November 2026 on branch `experimental`, using .NET 10 and the pinned,
unmodified UndertaleModLib dependency. No game was launched, no commit/push was made,
and no git configuration was changed.

The artifact measurements below precede the independent review fixes. See
[review follow-up](phase3-review.md) for current validation and all finding dispositions.

## Build and tests

- `dotnet build -c Release`: zero warnings, zero errors.
- Without `JAMM_ORIGINAL` / `JAMM_MODS_DIR`: 108 passed, 4 real-file tests skipped.
- With both variables: 112 passed, zero skipped (3 min 53 s).
- Opt-in build coverage generates patches from the exact common 16 August base,
  builds all three mods separately, compares every decompiled code entry, builds
  both three-mod orders, runs verification, and installs/restores in a temporary
  copy of the entire legacy game folder.
- Separate CLI coverage built Colour from its `.win` input, generated its patch,
  installed into a copied legacy folder, verified the installed/build/backup hashes,
  and restored through `cgfmm restore`. The permanent backup remained; the obsolete
  marker was removed. CLI round trip: 43.308 s, including patch generation.
- Synthetic regressions cover code choices and `keep-both`, field unions, addition
  order, collision targets, retained definitions, deletion/reference choices,
  independent sprite pixels and payload references, compile failures, dry runs,
  existing-output/input/link safeguards and installation/backup/restore failures.
- SHA-256 checks of both supplied legacy archives and all three mods before/after
  verification: all five matched; inputs were unchanged.

## Single-mod equivalence

Every code entry in each output decompiled to exactly the same LF-normalized GML as
its source mod: zero differing entries for all three mods. Named asset counts and
texture-page/page-item counts matched. String/variable/function tables use unions,
retain base definitions and can differ in duplicate/local compiler bookkeeping.

| Mod | Output bytes | Build time | Code | Objects | Sprites | Texture pages |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| The Abyss | 76,399,254 | 20.440 s | 1921 | 745 | 596 | 188 |
| Cave Expansion | 58,768,006 | 20.456 s | 1926 | 738 | 586 | 99 |
| Colour Expansion | 56,762,022 | 15.710 s | 1942 | 694 | 539 | 78 |

All three single-mod builds passed the builder's reload/reference/decompile/compile
verification. Cave retains 18 base strings that its mod archive removed; the decisions
are informational. Abyss and Cave each preserve three source texture-geometry warnings.

## Required combined outputs

Both outputs and complete decision logs are local in `/tmp/cgfmm-build/`. The first
order is Abyss, Cave, Colour; the second is Colour, Cave, Abyss. No explicit resolution
file was used. Wall times include loading, compilation, writing and verification;
runs shared the machine with other verification, so these are not benchmarks.

| Output | Bytes | Build time | Printed decisions |
| --- | ---: | ---: | ---: |
| `merged-abyss-cave-colour.win` | 112,990,722 | 35.256 s | 69 |
| `merged-colour-cave-abyss.win` | 117,110,402 | 35.452 s | 69 |

| Asset family | Abyss, Cave, Colour | Colour, Cave, Abyss |
| --- | ---: | ---: |
| Code | 2133 | 2133 |
| Scripts | 169 | 169 |
| Sprites | 742 | 742 |
| Objects | 894 | 894 |
| Rooms | 4 | 4 |
| Sounds | 44 | 44 |
| Fonts | 10 | 10 |
| Backgrounds | 2 | 2 |
| TexturePages | 307 | 312 |
| TexturePageItems | 1662 | 1681 |
| Strings | 7804 | 7804 |
| Variables | 3833 | 3833 |
| Functions | 367 | 367 |
| Paths | 120 | 120 |
| Timelines | 1 | 1 |

Independent `cgfmm verify` runs:

- `merged-abyss-cave-colour.win`: 9.983 s; 2,133 root entries decompiled and compiled; references passed.
- `merged-colour-cave-abyss.win`: 10.286 s; 2,133 root entries decompiled and compiled; references passed.

Each order printed 69 decisions: 50 Phase 2 conflict findings, 18 retained base-string
definitions and one effective reference-retention decision. The conflict findings
comprised 13 binary, 10 overlapping-code, 8 shared-insertion, 9 literal-index,
5 same-name-addition, 3 field and 2 reference findings. Later participating mods
won overlaps; disjoint code/field changes combined. A later surviving room reference
retained a base object that Cave removed. The final decision logs identify the
winning mod for each finding. The `check` command remains a preview and still
reports conflicts before build choices are applied.

## What remains unverified or unsupported

The game cannot be launched on this machine. **An in-game test on the desktop with
the legacy executable is still required.** In particular, inspect fish catalogs and
literal-index overlaps: successful GML compilation cannot prove runtime execution
order or gameplay compatibility. `keep-both` is an explicit user choice and was
covered synthetically; the combined artifacts above use the default later-mod choices.

Both combined archives preserve six out-of-bounds texture rectangles inherited from
the selected mod assets. They are warnings, not missing resource references. Null,
empty page items remain valid placeholders. Check the affected sprites visually.
The larger combined texture tables are intentional: source atlases are copied rather
than repacked, preserving each selected sprite's pixels.

The supported reconstruction target is the documented GM1.4 legacy game. GMS2 room
layers and child-code scopes are rejected; external audio files and the executable
are not installed by the builder. Dynamic resource lookup and arbitrary numeric
resource IDs remain outside complete static validation. See [building.md](../building.md)
for commands, resolution rules, copy/backup guarantees and limitations.
