# Silo v2

The Silo ships as a ModKit source package in `examples/modkit/silo/`. It opens the existing MissileSilo in NewWorld and places a chamber made from vanilla cave rocks and Geiger lights. Veyl, Radik, Ferrex and Garrusk use the normal Fish classes, lure and Fishalog. The source patch adds its own assets and a placement declaration. It changes zero vanilla code entries, objects or rooms. JAMM applies the area in the final merge and installs the usual Fish and Catalog runtimes.

## Art review

The 23-fish contact sheet is at `~/jamm-night/silo-v2/ref/contact-sheet.png`. The local SVG mockups are in `~/jamm-night/silo-v2/mockups/`. The composed sprite comparison is at `~/jamm-night/silo-v2/art-review.png`. Each row pairs a new species with three vanilla fish in its size class on a deep-water color sampled from the game. The archive, extracted parts and review images stayed on the VPS.

- Keep the head on the left and place the sprite origin near the front third so it turns and hooks like the native fish.
- Join broad, softly shaded body shapes under overlapping fins or across a natural color transition. Use silhouette before detail to distinguish species at game scale.
- Match the donor's softness and use a limited palette. Veyl has understated sensory pits, Radik has green scutes, Ferrex has a long rust-colored tail, and Garrusk has a broad jaw and muted fins.
- The reference sprite variants for damage and remains differ from the full-body frame. The new full-body sprites use native motion and rotation while swimming. The public recipe format also accepts separate compositions for any additional frames an object draws.
- The silo opening uses crops from the original closed sprite, spaced around a passable channel. The instance uses the resulting alpha mask. The unused vanilla opening-mask frames open the top of the lid but leave a solid section beneath it.

## Compatibility

The archive matrix uses the sanitized library patches for Antiquity, PWB, Abyss, Colour, Verdant, Cave and TGE. The established five-pack is Antiquity, PWB, Abyss, Colour and Verdant. The report tracks whether any conflict names The Silo, independently of existing pack conflicts. All archive inputs were read from local files.

Antiquity moves MissileSilo to (33776, 11440), Verdant moves it to (33984, 11568), and Cave moves and rotates it at (35472, 11984). When a pack contains competing moves of the same original singleton, the follow anchor keeps the latest selected placement. `[CGF-AREA-001]` also reports other room instance edits in the bounds. The area keeps its room-axis orientation when the hatch rotates.

The first five-pack playthrough exposed an Abyss Shados event that accessed an inactive Lumos instance. JAMM's `[CGF-INSTANCE-001]` compatibility rule now resolves that object asset through its exact known event pattern. The Silo's Step hook bounds fish movement on long frames and steers them back toward their spawn water when they stray. Both changes were made after testing the initial pack builds under Wine on the VPS.

## Archive matrix

| Profile | Legacy conflicts | Silo conflicts | Verified build | Silo content present |
| --- | ---: | ---: | --- | ---: |
| Silo | 0 | 0 | Yes | 24/24 |
| Silo + TGE | 0 | 0 | Yes | 24/24 |
| Silo + Antiquity | 0 | 0 | Yes | 24/24 |
| Silo + PWB | 0 | 0 | Yes | 24/24 |
| Silo + Abyss | 0 | 0 | Yes | 24/24 |
| Silo + Colour | 0 | 0 | Yes | 24/24 |
| Silo + Verdant | 0 | 0 | Yes | 24/24 |
| Silo + Cave | 0 | 0 | Yes | 24/24 |
| Silo + five-pack | 25 | 0 | Yes | 24/24 |
| Silo + all seven | 78 | 0 | Yes | 24/24 |

The two packs used standard load-order resolution for legacy conflicts. The source diff reported 23 added code entries and zero modified or removed code entries. Its vanilla object and room diffs were empty. Patch asset typing moved to revision 4 so fish registration calls relink by object identity. The managed library can refresh earlier cached patches from their local archives. The release build passed with zero warnings and the default suites passed 577 tests. The opt-in Silo matrix and staging-site package tests passed against the real base. Archive logs and JSON reports are at `~/jamm-night/silo-v2/matrix-shaft/`.

## In-game runs

| Profile | Chamber point in NewWorld | Blocked lure samples | Species shown and caught | Reloaded Fishalog entries | Reloaded boat inventory |
| --- | --- | ---: | ---: | ---: | ---: |
| Silo | (34069, 12210) | 0 | 4 | 4 | 4 |
| Silo + five-pack | (34069, 12208) | 0 | 4 | 4 | 4 |
| Silo + all seven | (35557, 12624) | 0 | 4 | 4 | 4 |

Each profile has eight captures under `~/jamm-night/silo-v2/screens/` covering the opened hatch, lit chamber, four swimming species, boat deposit and reload. The five-pack captures were repeated with the updated camera. Its original save and fresh-process reload are retained alongside the recapture log.

## Reproducing the checks

Run the regular release build and tests with `dotnet build -c Release` and `dotnet test -c Release`. The synthetic tests cover invalid manifests, placement and transforms, competing anchors, per-instance code, precise collision masks and multi-frame sprite composition. Set `JAMM_ORIGINAL` to the exact legacy base and `JAMM_SILO_INPUTS` to the root of the seven local archives to run `SiloAreaRealTests`.

The ModMaker bench's `--scene silo` moves the boat to the landmark and guides the lure through the opening in NewWorld, sampling native collision as it goes. Its capture camera follows the scripted subject directly to avoid interpolation overshoot on long Wine frames. The catch probe calls the fish's native bite event, then the catch and boat deposit routines. The save run writes a separate test save, and reload starts another process on that save and reads the Fishalog counts and boat inventory. Each test process uses a 600-second limit under the shared game lock. Screens and CSV logs are under `~/jamm-night/silo-v2/screens/`.

## Open points

The scene verifies the passage, catch and save behavior with a scripted lure path. Hand-controlled travel, tackle progression and reeling from the chamber were outside the scene. Fish spawn checks cover the initial free-water positions. The probe followed some fish beyond the shaft during pack play, including Radik in the all-seven run. Save compatibility with the retired diver-room package was outside this test matrix.
