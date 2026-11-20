# Silo v2

The Silo ships as a ModKit source package in `examples/modkit/silo/`. It opens the existing MissileSilo in NewWorld and places a chamber made from vanilla cave rocks and Geiger lights. Veyl, Radik, Ferrex and Garrusk use the normal Fish classes, lure and Fishalog. The source patch adds its own assets and a placement declaration. It changes zero vanilla code entries, objects or rooms. JAMM applies the area in the final merge and installs the usual Fish and Catalog runtimes.

## Art review

The 23-fish contact sheet is at `~/jamm-night/silo-v2/ref/contact-sheet.png`. The revised review is at `~/jamm-night/silo-v2/art-review.png`. The first four rows pair each new fish with three vanilla fish in its size class on sampled deep-water color. The bottom row crops the live fish frame and its vanilla donor from the game's comparison capture at 1× under equal lighting. The separate gameplay screenshots show the fish in the native water and rock lighting. The source renders, vanilla pixels and screenshots stay on the VPS.

Each recipe starts from one intact silhouette and uses `tones` to remap its luminance to flat swatches. Fins keep their native joins. The full silhouette remains the primary marker at game scale. The original SVG details use only the darkest color in the same palette. In all four exported recipes, the intended dominant swatches account for more than 99% of fully opaque pixels.

| Fish | 1. Palette | 2. Joins | 3. Style | 4. Silhouette at game scale | 5. Source and details |
| --- | --- | --- | --- | --- | --- |
| Veyl | Three cool blues sampled from Cuddlefish. | Garder's tail and fins stay attached through their native silhouette. | Flat blue body and a darker soft tail. The old teal patch is gone. | The high fin and rounded tail carry the shape. The smallest sensory pits are deliberately subtle. | One Garder frame and three small flat pit marks. |
| Radik | Three leaf greens sampled from Bagu. | One continuous Sizzer body ends in its own broad tail. | Mid-green fins and three dark scutes. The two sides of the body use the same fill. | The tall profile and large rounded tail remain readable without the scutes. | One Sizzer frame and three flat scutes contained within the flank. |
| Ferrex | Two rust reds and amber sampled from Magmer. | Stretches the complete Magmer silhouette, preserving the lower fins and the tail join. | Flat rust body, darker fin and gill shading, original amber spike shapes. | The narrow profile and exposed amber spikes carry the silhouette. Dark gill marks are secondary. | One Magmer frame and two small gill marks. |
| Garrusk | Maw's sand and stone tones dominate the opaque pixels. | Shark's own full tail is continuous with the stockier body. | The body is a single opaque layer. Native fin boundaries contain the darker shade. | The deeper shoulder and tapered Shark tail define the shape. This is the closest of the four to its donor. | One Shark frame and three small flat gill marks. |

## Hatch opening

An approaching cast lure triggers the colocated ModKit controller within 250 room pixels. It uses the original `spr_silo` and closed `spr_silomask` initially. The two shutters are complementary crops from the closed sprite, and they slide and turn apart over 1.55 simulated seconds with eased motion. A short bubble puff rises through the aperture. Four native Geiger lights then fade in over the hatch and surrounding rocks. Once the shutters clear, the controller switches to the composed open sprite's collision mask. The vanilla open-mask frames leave solid material beneath the lid, so the composed passage supplies the open collision shape.

The controller is placed on the same follow anchor as the hatch. Shutter motion follows the hatch's local rotation, including Cave's relocated and rotated instance. The controller records the completed opening in the active native save under `JAMM_Areas`. Reloading an opened slot starts with the passage clear and the lights on. MissileSilo's Create and Step events are unchanged. The sequence uses bubbles and lighting without a sound cue. Garrusk keeps native swimming motion.

## Compatibility

The archive matrix uses the sanitized library patches for Antiquity, PWB, Abyss, Colour, Verdant, Cave and TGE. The established five-pack is Antiquity, PWB, Abyss, Colour and Verdant. The report tracks whether any conflict names The Silo, independently of existing pack conflicts. All archive inputs were read from local files.

Antiquity moves MissileSilo to (33776, 11440), Verdant moves it to (33984, 11568), and Cave moves and rotates it at (35472, 11984). When a pack contains competing moves of the same original singleton, the follow anchor keeps the latest selected placement. `[CGF-AREA-001]` also reports other room instance edits in the bounds. The area keeps its room-axis orientation when the hatch rotates.

The first five-pack playthrough exposed an Abyss Shados event that accessed an inactive Lumos instance. JAMM's `[CGF-INSTANCE-001]` compatibility rule now resolves that object asset through its exact known event pattern. The Silo's Step hook bounds fish movement on long frames and steers them back toward their spawn water when they stray. Both changes were made after testing the initial pack builds under Wine on the VPS.

## Archive matrix

The following ten builds and three full playthroughs in the next section covered the initial Silo v2 package before the opening addendum. The revised 2.1 package has its own playback captures and verification below.

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

## Opening addendum verification

Version 2.1 builds for solo and all seven retained all 26 Silo assets with zero Silo conflicts. The all-seven profile reported 78 legacy conflicts. The standalone source diff added 26 code entries and changed zero existing code entries, objects or rooms. The release build passed with zero warnings and all 580 default tests passed. The opt-in ten-build matrix and the staged site-package integration test also passed against the original archive. Build logs are at `~/jamm-night/silo-v2/addendum/`.

The opening probe records a blocked passage in the closed and mid-slide frames, then a clear passage once both doors have opened. The glow capture waits for the native lights to reach full brightness. The probe traverses the shaft with the lure and reads the hatch's passability, brightness, catch counts and boat inventory in a fresh process on the same save. The `solo/` and `all7/` directories under `~/jamm-night/silo-v2/screens/opening/` each contain `opening-sequence.png`, the individual captures and the test CSVs.

| Profile | Hatch in NewWorld | Closed and mid-slide | Open and glow | Blocked lure samples after opening | Fresh-process reload |
| --- | --- | --- | --- | ---: | --- |
| Silo 2.1 | (33984, 11570) | Blocked at 0% and 39% | Passable at 100%, lights reached full brightness | 0 | Open and lit. Four Fishalog catches and four fish in the boat. |
| Silo 2.1 + all seven | (35472, 11984) | Blocked at 0% and 39% | Passable at 100%, lights reached full brightness | 0 | Open and lit. Four Fishalog catches and four fish in the boat. |

A separate run loaded slot 8 alongside the opened slot 7. Slot 8's hatch was closed, dark and blocked. Slot 7 still contained the opened flag when the run ended. The slot 8 capture and CSV are in `~/jamm-night/silo-v2/screens/opening/slot-isolation/`.

## Reproducing the checks

Run the regular release build and tests with `dotnet build -c Release` and `dotnet test -c Release`. The synthetic tests cover invalid manifests, placement and transforms, competing anchors, per-instance code, precise collision masks and multi-frame sprite composition. Set `JAMM_ORIGINAL` to the exact legacy base and `JAMM_SILO_INPUTS` to the root of the seven local archives to run `SiloAreaRealTests`.

The ModMaker bench's `--scene silo` moves the boat to the landmark and guides the lure through the opening in NewWorld, sampling native collision as it goes. Its capture camera follows the scripted subject directly to avoid interpolation overshoot on long Wine frames. The catch probe calls the fish's native bite event, then the catch and boat deposit routines. The save run writes a separate test save, and reload starts another process on that save and reads the Fishalog counts and boat inventory. Each test process uses a 600-second limit under the shared game lock. Screens and CSV logs are under `~/jamm-night/silo-v2/screens/`.

## Open points

The scene verifies the passage, catch and save behavior with a scripted lure path. Hand-controlled travel, tackle progression and reeling from the chamber were outside the scene. Fish spawn checks cover the initial free-water positions. The all-seven 2.1 probe followed Radik and Ferrex beyond the shaft for their swimming shots. Save compatibility with the retired diver-room package was outside this test matrix.
