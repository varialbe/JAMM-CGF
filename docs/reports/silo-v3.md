# The Silo 2.3.0

The MissileSilo instance draws a static rock and a recessed hatch frame at its native depth. The colocated ModKit controller slides one plate 300 pixels along the hatch's local X axis over 1.55 seconds. Bubbles rise from the exposed opening, and four Geiger lights fade up to their authored brightness. The same instance supplies the transform when the Area API follows Cave Expansion's relocation and rotation.

The closed collision shape comes from frame 0 of `spr_silomask`. At full travel the controller assigns `shaft-mask`, which preserves the v2 passage geometry. The small grey block in v2 came from the rectangular crop at (795, 270), 59 × 95 px, in `open-silo`. The new controller uses that composition exclusively for collision, so the crop no longer draws. The open flag remains under `[JAMM_Areas]` in the native save slot.

The foreground takes a 380 × 106 px crop of `spr_silo` and clips it to the near lip. The crop and its adjusted origin sample the same pixels as the static body. The layer sits at −19.25, between the fish and the native lighting pass at −19.5. The legacy archive has MissileSilo and the CaveRock parent at 0 and the nearby CaveRockBig and CaveRockLeg pieces at 1. The cast lure and its line share the lure's Draw event at −1.55. While hooked, the lure draws at the fish's depth plus 0.002. Normal fish set their depth from size, and the vanilla fish Step can temporarily draw at −19. The narrow lip covers these actors briefly. The rear rock and shaft remain visible behind them.

ModKit's `alphaMask` layer field clips a crop with the alpha from an original PNG or self-contained SVG before flip, scale and rotation. Matching crop dimensions are required. The Silo uses it for the plate, lip, shadow and fish parts. The package ships recipes, original SVG shapes and GML. JAMM samples the vanilla artwork from the player's legacy archive at build time.

## Art review

The side-by-side review is `~/jamm-night/silo-v3/art-review.png`. It includes three in-game shots of the lure crossing the lip. Earlier sheets are in `review-1/`, `review-2/`, and `review-3/` under the same directory.

| Pass | Review and changes |
| --- | --- |
| 1 | The rim aligned with the body, but fragments of the vanilla zigzag showed above and below the new frame. The protruding plate and two bright handles made it look like a separate asset pasted over the vanilla rock. Ferrex's tail read as a flat dark shape, and its two teal marks looked like extra eyes. |
| 2 | Sampled steel covered the broken zigzag. The bay and plate got additional clearance and one recessed handle. Lower luminance cutoffs recovered the Underfin tail's rays. Ferrex gained a teal pectoral and three tapered markings. The lower shaft still read as a single bright rock face. |
| 3 | A muted recess follows the back rock below the lip, giving the lure a darker surface to reappear against. The plate is slightly less bright, and Ferrex's dorsal crop is softer and shorter. The rock retains its original outer outline, and each nontransparent lip pixel matches the underlying body. |

The first in-game review showed an edge around the lip because it drew after the native lighting pass. At the corrected depth, the visible edge disappears and the line and lure briefly drop out at the lip. The lure is visible again against the darker rock below. The opened shot has a continuous rock outline where v2 drew the grey block.

Ferrex uses a shortened copper Bagu body, a swept Noxius dorsal and a forked Underfin tail. The teal pectoral, tail rays and markings give it a different silhouette and palette at the game's normal scale.

## Archive verification

The release build completed with zero warnings. The default test run passed 537 Core and 129 GUI tests. It skipped 17 opt-in tests. The two task-specific opt-ins passed separately: the ten-profile real archive matrix and the staged site package integration test. The no-mod build matched the untouched August 16, 2023 base byte for byte, with SHA-256 `dbb2e5c4b788358d0c6465d3005b2e9d46c4c540e423b064d7a5a3629286d2c1`.

The Silo source adds 26 code entries and five objects. The source diff has zero changed or removed code entries, zero changed or removed objects, and zero room edits. The solo and all-seven builds each accepted 28 of 28 Silo assets. All seven followed Cave Expansion's hatch from (33984, 11570) to (35472, 11984). The archive verifier reported six legacy texture page bounds warnings and the pack acceptance report recorded one table-length risk. The personal profile uses Perf, HD Lite, Console, Smooth, The Silo, Weather Pack and Endless Ocean in that order. JAMM reported zero conflicts and verified the final personal archive.

The open mask has the same pixel coverage as v2. Area placement uses the closed frame during initial spawn checks. This put the three Veyl spawns deeper in the solo shaft. The other three species kept their initial solo positions.

The site generator rebuilt the 2.3.0 source ZIP (10,596 bytes) and catalog entry. The ZIP contains the ModKit source and its original SVG parts. The staged detail page describes the current package and dates the older gallery captures. No deployment was made.

## In-game verification

The ModMaker scene keeps the camera on the hatch during the opening and the first part of the descent. It probes the hatch collision while closed and at five points during the slide, then samples the lure path through the open shaft. Run the scene with `~/jamm-night/bin/run.sh <win> <label> silo 600` under the shared game lock. Use `JAMM_SILO_MODE=save` for a playthrough with a save, then `reload` with that run's save directory as `JAMM_SILO_SAVE_SEED`. The scene drives the lure and triggers the native bite and boat deposit routines. It covers the shaft and save behavior under scripted control.

| Profile | Hatch in NewWorld | Closed and moving | At full travel | Blocked descent samples | Fish caught and deposited | Fresh-process reload |
| --- | --- | --- | --- | ---: | ---: | --- |
| Silo alone | (33984, 11570), 0° | Blocked | Passable | 0 | 4 | Open and lit with all four catches |
| Silo plus all seven | (35472, 11984), −30° | Blocked | Passable | 0 | 4 | Open and lit with all four catches |
| Personal profile | (33984, 11570), 0° | Blocked | Passable | 0 | 4 | — |

The all-seven save run hit the existing `Maize.edges` patrol crash after the four fish were deposited and the bench recorded the completed save. A separate process loaded that save and verified the hatch, Fishalog, and boat inventory. The archive built on this branch still has that legacy pack bug. A separate diagnostic archive built with the existing `CGF-NAV-001` repair from `6d59db8` repeated the opening and descent at Cave's hatch, caught all four fish, and exited cleanly. The repair was used for this local test archive.

An independent solo run loaded slot 8 alongside the opened slot 7. Slot 8's hatch was closed, dark and blocked. Slot 7 retained its open flag. The all-seven evidence is kept locally under `~/jamm-night/silo-v3/` in `internal-all7/` and `internal-all7-maize-fixed/`. The reload checks are in `internal-reloads/`.

The solo media is in `~/jamm-night/silo-v3/opening/`. It includes 12 labeled checkpoints and 18 native draw frames. `opening.mp4` is 1200 × 720 and runs 2.87 seconds. `opening.gif` is 840 × 504 with 34 frames. Both clips use the solo game's native captures, with short holds on the closed and lit states.
