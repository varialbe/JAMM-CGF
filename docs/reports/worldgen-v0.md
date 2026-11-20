# Endless Ocean v0

Endless Ocean builds onto the August 2023 legacy VM archive or onto an already merged pack. It adds a controller to NewWorld and reserves 488 chunks of 2,048 px. The controller streams the camera's current chunk and two neighbors on each side. A full window owns 20 vanilla CaveRockBig instances and at most 15 fish. Near the start and the far end the window is smaller. Fish on the hook, in the net or aboard stay under vanilla control until that interaction ends. Revisiting an unloaded chunk reconstructs its layout from the seed.

The sampled rock profile uses two frequencies of smooth value noise and places rock origins between y = 10,700 and 12,010. Adjacent chunks sample the same profile. The native CaveRockBig mask is a descendant of CaveRock and Collidable, which the lure and swimmer already query. Fish use three depth bands and six existing species. The generator clears a padded spawn envelope against live Collidable masks, assigns the normal Fishalog index, and then leaves swimming, catching and selling to the existing objects. After spawning, the controller calls the vanilla rod refresh to initialize lure modifiers for the new fish.

The only world-generation data written to disk is the seed. It lives in `jamm_endless_ocean.ini` in the game save directory and is shared by its save slots. The recipe guards the existing primary and backup SaveManager events at their per-instance `[Fishes]` writes so transient fish IDs are excluded. Regular catches, vanilla position and Fishalog data still use the game's save format.

## East edge and the seven inputs

I read NewWorld directly from the local archives. The spec's 140,000 × 26,000 dimensions match Cave Expansion. The August 16 legacy base on the VPS is 100,000 × 13,000.

| Archive | NewWorld width × height | Eastmost placed origin |
| --- | ---: | ---: |
| August 16 legacy vanilla | 100,000 × 13,000 | 81,634 |
| Abyss | 170,000 × 30,000 | 165,600 |
| Antiquity | 150,000 × 35,000 | 82,128 |
| Colour | 170,000 × 17,000 | 169,727 |
| Verdant | 100,000 × 13,000 | 100,096 |
| Cave Expansion | 140,000 × 26,000 | 100,976 |
| PWB | 90,000 × 18,000 | 81,634 |
| TGE | 120,000 × 18,000 | 101,248 |

The build uses 170,000 as a minimum and moves the origin past the input's width and any farther placed sprite or mask bounds. This covers packs whose resolved width is smaller than their content. The public five-pack's eastern artwork moves its start to x = 170,373. Apply the recipe after merging so the width can be set from the actual input. The legacy vanilla build has a stretch of open water between the old boat limit and the first generated rock.

The first eastward stop is the boat's `MAX_X`. Vanilla Boat7 sets it to 65,000. Boat movement clamps at that value and the buoy follows it. The new controller extends only the live Boat7 instance's range after it is created. The room view follows Camera and has room boundaries, so the build also extends NewWorld's width by 999,424 px from the generation origin. Boat7 stops 400 px before the far end. NavBrain builds a route graph for agents from placed nodes. Boat input, camera code, the buoy and the graph use their existing events.

## Validation

The synthetic GML tests run with `node --test tests/worldgen/chunks.test.mjs`. They execute the checked-in layout and streaming scripts, compare visitation orders and seeds, check spawn envelopes across all 488 chunks at three seeds, exercise the live collision veto and verify reload and destruction. The .NET tests cover recipe seed validation and archive integration. The release build had zero warnings. The core suite passed 447 tests and the GUI suite passed 127 on rerun after an intermittent timing failure. Four synthetic GML tests passed.

The vanilla bench started on a disposable save, drove Boat7 across its stock cap and the generation boundary, then teleported between underwater checkpoints. It used a vanilla lamp to illuminate the captured area. The lure's native CaveRock collision query returned a hit at all three checkpoints. The bench exercised both SaveManager events and found zero streamed fish IDs in their `[Fishes]` sections. It returned west with zero generated instances and completed without a CODE ERROR.

| Vanilla checkpoint | Boat x | Camera left x | Chunks | Owned instances | Active room instances | Screenshot |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Past stock Boat7 limit | 65,044 | 64,713 | 0 | 0 | 4,346 | |
| Past generation boundary | 170,119 | 169,747 | 3 | 21 | 4,367 | |
| First terrain stop | 172,890 | 172,490 | 4 | 28 | 4,379 | `vanilla-1.png` |
| Deep fish stop | 410,665 | 410,265 | 5 | 35 | 4,386 | `vanilla-2.png` |
| Final chunk | 1,168,218 | 1,167,818 | 3 | 21 | 4,174 | `vanilla-3.png` |
| Returned west | 65,005 | 64,718 | 0 | 0 | 3,977 | |

The same route completed on the public five-pack (Antiquity, PWB, Abyss, Colour and Verdant) with seed 314159 read from the save directory. It passed the pack's stock Boat7 limit of x = 85,000, crossed the generation boundary, hit terrain at each underwater stop, wrote zero streamed fish IDs into the primary and backup saves, and finished without a CODE ERROR.

| Five-pack checkpoint | Boat x | Chunks | Owned instances | Active room instances | Screenshot |
| --- | ---: | ---: | ---: | ---: | --- |
| First terrain stop | 173,263 | 4 | 28 | 9,691 | `public5-1.png` |
| Deep fish stop | 411,047 | 5 | 35 | 9,700 | `public5-2.png` |
| Final chunk | 1,168,591 | 3 | 21 | 9,379 | `public5-3.png` |
| Returned west | 85,005 | 0 | 0 | 9,075 | |

Screenshots are in `~/jamm-night/worldgen/screens/`. The CSVs are in `~/jamm-night/worldgen/run-vanilla.cv5qkA/save/jamm_bench_worldgen.csv` and `~/jamm-night/worldgen/run-public5.3iMCLq/save/jamm_bench_worldgen.csv`. Total active counts also reflect vanilla zone activation. Generated instance counts come from the controller's owned lists.

## Showcase

The showcase uses a separate bench archive built from Endless Ocean on the August 2023 vanilla base. Run `jamm-modmaker bench endless-ocean.win showcase.win --scene worldgen --showcase` and launch it in a separate game copy with a disposable save and the chosen seed in `jamm_endless_ocean.ini`. The bench places Boat7 east of the generation boundary, moves the camera through fixed frames, and lights the underwater shots with a scaled vanilla lamp. Each underwater frame spans 4,096 world pixels horizontally. The depth sheets stack three frames at the same x and scale, omitting the stretches of water between the three fish spawn bands.

| File in `~/jamm-night/worldgen/showcase/` | Caption |
| --- | --- |
| `vanilla-1337-transition.png` | At x = 170,000 the first generated rock rises on the right. The open water on the left follows the end of the legacy map's placed content. |
| `vanilla-1337-floor-03.png` | At x = 177,168 the floor rises from a low shelf into a long ridge. |
| `vanilla-314159-floor-03.png` | The same framing with seed 314159. The high ground on the left drops into a trough before rising at the far right. |
| `vanilla-1337-floor-117.png` | At x = 410,640 the floor climbs to a broad crest and starts to descend again. |
| `vanilla-1337-floor-486.png` | At x = 1,166,352 the low shelf rises sharply toward the final chunk. |
| `vanilla-1337-depths-03.png` | Upper-water, midwater and near-floor fish at x = 177,168. |
| `vanilla-1337-depths-117.png` | The same three depth bands at x = 410,640. |
| `vanilla-1337-depths-486.png` | The three depth bands near the east end at x = 1,166,352. |
| `vanilla-1337-panorama.png` | Five consecutive deep-ocean screens spanning x = 223,248–233,488, joined west to east. |
| `vanilla-1337-surface.png` | Boat7 at x = 410,640, with open sky and continuous waves above the generated terrain. |

The panorama is 3,000 × 720. FFmpeg crops the middle 600 columns of each 1,200 × 720 frame and joins them with `hstack`. Each crop represents one 2,048 px chunk and the bench keeps the lamp fixed across the five frames. The two `vanilla-*.csv` files record the seed, camera bounds, streamed chunks and the number of generated fish whose centers were inside each frame.

The optional public five-pack run completed all 16 captures with the recipe default seed, 271828. Its generation boundary moves the route 373 px east. The near-floor frame at the first stop recorded zero fish. The other two floor stops recorded fish in all three bands.

| File in `~/jamm-night/worldgen/showcase/public5-271828/` | Caption |
| --- | --- |
| `transition.png` | The first generated rocks east of the five-pack's boundary at x = 170,373. |
| `floor-03.png` and `depths-03.png` | The first floor stop and its depth panels at x = 177,541. |
| `floor-117.png` and `depths-117.png` | The middle stop and all three fish bands at x = 411,013. |
| `floor-486.png` and `depths-486.png` | The far-eastern floor and all three fish bands at x = 1,166,725. |
| `panorama.png` | Five joined views of the floor spanning x = 223,621–233,861. |
| `surface.png` | Boat7 above the middle stop at x = 411,013. |
| `capture.csv` | Camera bounds, seed, streamed chunks and generated fish counts for the five-pack run. |

The release build finished with zero warnings. The core and GUI suites passed 447 and 127 tests, with 15 opt-in tests skipped across both suites. All four synthetic worldgen tests passed. Building the default validation scene from the same recipe produced a byte-for-byte match with the prior bench archive.

## Fixed ranges and v1

| System | What the legacy build does | What v1 needs |
| --- | --- | --- |
| Spawns | Many fish are hand-placed. The vanilla ProceduralSpawner builds a fixed grid near its own origin, and Zoner snapshots placed fish at room start. ModKit fish registration takes an explicit x/y rectangle. Vanilla gear's range fields are fractions of a fixed span ending at x = 18,000. | Use chunk-local spawn rules per biome. Give each spawned entity a stable logical identity if depletion or quests need to survive unloading. |
| Quests | Catch quests use Fishalog species and counts. Cast distance/depth quests use fixed values and rod tiers. Daily resets use the date. | Existing catches can count without new entries. Exploration goals would need logical coordinates, biome IDs and persistent progress. |
| Sonar | Normal and deep sonar follow Girl and Lure using bounded windows, but the normal sonar noise uses absolute x and depth. Deep sonar has equipment and depth gates. | Update the noise model and ensure the loaded window covers both view and lure. Keep sonar overlays in local coordinates through a shift. |
| Navigation | NavBrain creates edges between placed NavNodes at room start using line-of-sight collision. Generated chunks have no NavNodes or edges. | Stream local nodes with chunks, link neighboring chunks, and discard links safely when either endpoint unloads. |
| Day and night | The ordinary cycle depends on AmbianceController and time. Some species have fixed night behavior. Noctis uses `room_width` for a route and the boat's wave motion uses `Girl.x / room_width`. | Separate the legacy world width from the traveled distance, and audit each room-width consumer. |
| Boat travel | Boat7's movement speed and bonus stay vanilla. The legacy fuel counter belongs to the lure rocket. The buoy and swimmer's horizontal range consume Boat.MAX_X. Sea startup instantiates wave tiles across the full room width. | Replace the finite boat cap with travel between local segments, stream waves, and preserve the progression gates for earlier boats. |

A floating origin would keep a logical chunk index and a local x coordinate, shift by whole chunks and update the boat, player, lure, line, attached fish, camera, sonar, physics and path anchors together. The transition back to the vanilla map would need to restore its coordinate frame and preserve any static quest and navigation references. Saves would need a logical travel coordinate and a waypoint for loading an existing slot into the extended world. The v0 room stops at x = 1,169,424 on the vanilla build. Its wave tile allocation is fixed at startup, and the terrain and free-fish instance budget depends on the streamed window.
