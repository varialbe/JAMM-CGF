# Silo v2

The Silo ships as a ModKit source package in `examples/modkit/silo/`. It opens the existing MissileSilo in NewWorld and places a chamber made from vanilla cave rocks and Geiger lights. Veyl, Radik, Ferrex and Garrusk use the normal Fish classes, lure and Fishalog. The source patch adds its own assets and a placement declaration. It changes zero vanilla code entries, objects or rooms. JAMM applies the area in the final merge and installs the usual Fish and Catalog runtimes.

## Art review

The 23-fish contact sheet is at `~/jamm-night/silo-v2/ref/contact-sheet.png`. The 2.2 review is at `~/jamm-night/silo-v2/art-review.png`. The first four rows pair each species with three vanilla fish in its size class. The bottom row uses individual 1× in-game comparison captures with a consistent background and labels below each fish. The source renders, vanilla pixels and screenshots stay on the VPS.

The recipes use `tones` to keep the fills in a compact palette. Radik's palette and composition carry over from 2.1. Its dorsal edge uses a 0.64-pixel alpha filter, which increased the exported partially transparent edge pixels from 1,479 to 2,693. The other three use new palettes and retain the native shading boundaries of their main silhouettes.

| Fish | 1. Palette | 2. Joins | 3. Style | 4. Silhouette at game scale | 5. Source and details |
| --- | --- | --- | --- | --- | --- |
| Veyl | Dusty lilac, plum fins and a darker plum for the pits. | The extended dorsal crop overlaps behind the intact Garder body. The stock tail joins naturally. | The sail uses a single darker fill. | The tall swept sail and lower body ratio distinguish it from the rounder small blue fish. The pits are secondary at game scale. | Garder pixels and three small flat pits. |
| Radik | The same three leaf greens from Bagu. | The continuous Sizzer silhouette and tail retain their original joins. | The dorsal edge has a soft alpha transition. The mid-green fins and flank scutes keep their previous shades. | The tall profile and broad tail remain the main cues. | One Sizzer frame and three small flat scutes. |
| Ferrex | Three copper and ochre shades with a dark eye and gill tone. | The full Bagu silhouette keeps its native proportions, including the long tail. | The body and fins use separate flat copper fills. | The two-lobed nose and long tail read cleanly. Its silhouette is still recognizably Bagu-derived. | One Bagu frame and two small gill marks. |
| Garrusk | Muted petrol-green body, bands and fins. | Shark's full tail remains continuous with the body. The bands sit inside the flank. | Three tapered flat bands use the darker body shade. | The broad shoulder still reads as sharklike. The three bands are the distinguishing feature at 1×. | One Shark frame and a small flat SVG marking layer. |

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

The opening probe records a blocked passage in the closed and mid-slide frames, then a clear passage once both doors have opened. The glow capture waits for the native lights to reach full brightness. The probe traverses the shaft with the lure and reads the hatch's passability, brightness, catch counts and boat inventory in a fresh process on the same save. The earlier 2.1 sequences are archived under `~/jamm-night/silo-v2/round3/prior-captures/`.

| Profile | Hatch in NewWorld | Closed and mid-slide | Open and glow | Blocked lure samples after opening | Fresh-process reload |
| --- | --- | --- | --- | ---: | --- |
| Silo 2.1 | (33984, 11570) | Blocked at 0% and 39% | Passable at 100%, lights reached full brightness | 0 | Open and lit. Four Fishalog catches and four fish in the boat. |
| Silo 2.1 + all seven | (35472, 11984) | Blocked at 0% and 39% | Passable at 100%, lights reached full brightness | 0 | Open and lit. Four Fishalog catches and four fish in the boat. |

A separate run loaded slot 8 alongside the opened slot 7. Slot 8's hatch was closed, dark and blocked. Slot 7 still contained the opened flag when the run ended. The slot 8 capture and CSV are in `~/jamm-night/silo-v2/screens/opening/slot-isolation/`.

## 2.2 capture verification

The capture bench lets the title clear, dismisses the casting hint and filters other messages from the shots. It fixes the primary view on the hatch and lights the scene with a temporary vanilla lamp, using the same light-surface setup as the worldgen showcase. The chamber shot briefly arranges four live fish in frame. The 1× comparison row uses separate in-game render surfaces so each complete fish frame has its own caption and gutter.

The release build passed with zero warnings. All 581 default tests, the ten-build opt-in archive matrix and the opt-in site-package integration passed. The 2.2 archives verified with all 26 Silo assets present and zero Silo conflicts for solo and all seven. The 78 all-seven legacy conflicts carry over from the earlier build.

The solo and all-seven sequences each contain nine frames. The five moving shots recorded 13%, 32%, 45%, 65% and 84% progress. The closed hatch and all five moving shots blocked the probe, and the clear shaft allowed the complete lure path with zero blocked samples. The all-seven sequence followed Cave's rotated hatch at (35472, 11984). Mean frame luminance was 29–33% in solo and 29–37% with all seven. I checked each shot for visible geometry and title or casting text, and confirmed the four live fish in each chamber shot against their body-color regions.

Each native comparison uses a separate surface with a 20-pixel margin around the sprite canvas. I compared the silhouettes in all eight new and vanilla captures against their corresponding source frames. The full silhouettes are present, with no horizontal band across them. The labels sit in separate space below the captures.

The nine full-size stills and `opening-sequence.png` are in each of `~/jamm-night/silo-v2/screens/opening/solo/` and `~/jamm-night/silo-v2/screens/opening/all7/`. The live X11 recording of the all-seven opening was retimed to 3.00 seconds. Its `opening.mp4` and 1,180,215-byte `opening.gif` are in `~/jamm-night/silo-v2/screens/opening/`. I checked the decoded frames for desktop occlusion and sampled twelve video frames for title and casting text. The rejected solo desktop take remains outside the published sequence.

## Reproducing the checks

Run the regular release build and tests with `dotnet build -c Release` and `dotnet test -c Release`. The synthetic tests cover invalid manifests, placement and transforms, competing anchors, per-instance code, precise collision masks and multi-frame sprite composition. Set `JAMM_ORIGINAL` to the exact legacy base and `JAMM_SILO_INPUTS` to the root of the seven local archives to run `SiloAreaRealTests`.

The ModMaker bench's `--scene silo` moves the boat to the landmark and guides the lure through the opening in NewWorld, sampling native collision as it goes. Its capture camera follows the scripted subject directly to avoid interpolation overshoot on long Wine frames. The catch probe calls the fish's native bite event, then the catch and boat deposit routines. The save run writes a separate test save, and reload starts another process on that save and reads the Fishalog counts and boat inventory. Each test process uses a 600-second limit under the shared game lock. Screens and CSV logs are under `~/jamm-night/silo-v2/screens/`.

With `--silo-video`, the bench writes `jamm_silo_opening.ready` in the test save directory and holds the closed view for the desktop recorder. The recorder writes `jamm_silo_opening.go` after it starts. The bench signals the full-glow frame with `jamm_silo_opening.done`.

## Open points

The scene verifies the passage, catch and save behavior with a scripted lure path. Hand-controlled travel, tackle progression and reeling from the chamber were outside the scene. Fish spawn checks cover the initial free-water positions. The all-seven 2.1 probe followed Radik and Ferrex beyond the shaft for their swimming shots. Save compatibility with the retired diver-room package was outside this test matrix.
