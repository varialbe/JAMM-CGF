# The Silo 2.3.0

The MissileSilo instance draws a static rock and a recessed hatch frame at its native depth. The colocated ModKit controller slides one plate 300 pixels along the hatch's local X axis over 1.55 seconds. Bubbles rise from the exposed opening, and four Geiger lights fade up to their authored brightness. The same instance supplies the transform when the Area API follows Cave Expansion's relocation and rotation.

The closed collision shape comes from frame 0 of `spr_silomask`. At full travel the controller assigns `shaft-mask`, which preserves the v2 passage geometry. The small grey block in v2 came from the rectangular crop at (795, 270), 59 × 95 px, in `open-silo`. The new controller uses that composition exclusively for collision, so the crop no longer draws. The open flag remains under `[JAMM_Areas]` in the native save slot.

The foreground takes a 380 × 106 px crop of `spr_silo` and clips it to the near lip. The crop and its adjusted origin sample the same pixels as the static body. The layer sits at −19.25, between the fish and the native lighting pass at −19.5. The legacy archive has MissileSilo and the CaveRock parent at 0 and the nearby CaveRockBig and CaveRockLeg pieces at 1. The cast lure and its line share the lure's Draw event at −1.55. While hooked, the lure draws at the fish's depth plus 0.002. Normal fish set their depth from size, and the vanilla fish Step can temporarily draw at −19. The narrow lip covers these actors briefly. The rear rock and shaft remain visible behind them.

ModKit's `alphaMask` layer field clips a crop with the alpha from an original PNG or self-contained SVG before flip, scale and rotation. Matching crop dimensions are required. The Silo uses it for the plate, lip, shadow and fish parts. The package ships recipes, original SVG shapes and GML. JAMM samples the vanilla artwork from the player's legacy archive at build time.

## Art review

The side-by-side review is `~/jamm-night/silo-v3/art-review.png`. Earlier sheets are in `review-1/` and `review-2/` under the same directory.

| Pass | Review and changes |
| --- | --- |
| 1 | The rim aligned with the body, but fragments of the vanilla zigzag showed above and below the new frame. The parked door protruded awkwardly. Ferrex's tail read as a flat dark shape, and its two teal marks looked like extra eyes. |
| 2 | Sampled steel covered the broken zigzag. The bay and plate got additional clearance and one recessed handle. Lower luminance cutoffs recovered the Underfin tail's rays. Ferrex gained a teal pectoral and three tapered markings. The lower shaft still read as a single bright rock face. |
| 3 | A muted recess follows the back rock below the lip, giving the lure a darker surface to reappear against. The plate is slightly less bright, and Ferrex's dorsal crop is softer and shorter. The rock retains its original outer outline, and each nontransparent lip pixel matches the underlying body. |

The first in-game review showed an edge around the lip because it drew after the native lighting pass. The final depth puts the body and lip in the same lighting pass.

Ferrex uses a shortened copper Bagu body, a swept Noxius dorsal and a forked Underfin tail. The teal pectoral, tail rays and markings give it a different silhouette and palette at the game's normal scale.

## Archive verification

The release build completed with zero warnings. The default test run passed 537 Core and 129 GUI tests. It skipped 17 opt-in tests. The no-mod build matched the untouched August 16, 2023 base byte for byte, with SHA-256 `dbb2e5c4b788358d0c6465d3005b2e9d46c4c540e423b064d7a5a3629286d2c1`.

The Silo source adds 26 code entries and five objects. The source diff has zero changed or removed code entries, zero changed or removed objects, and zero room edits. The solo and all-seven builds each accepted 28 of 28 Silo assets. All seven followed Cave Expansion's hatch from (33984, 11570) to (35472, 11984). The archive verifier reported six legacy texture page bounds warnings and the pack acceptance report recorded one table-length risk. The personal profile uses Perf, HD Lite, Console, Smooth, The Silo, Weather Pack and Endless Ocean in that order. JAMM reported zero conflicts and verified the final personal archive.

The open mask has the same pixel coverage as v2. Area placement uses the closed frame during initial spawn checks. This put the three Veyl spawns deeper in the solo shaft. The other three species kept their initial solo positions.

The site generator rebuilt the 2.3.0 source ZIP (10,596 bytes) and catalog entry. The ZIP contains the ModKit source and its original SVG parts. The staged detail page describes the current package and dates the older gallery captures. No deployment was made.

