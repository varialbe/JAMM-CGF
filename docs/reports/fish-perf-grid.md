# Fish neighbour grid

The grid rewrites bounded `FishLarge` avoidance and preserves the original traversal order for every fish inside the cutoff. The heavy pack missed the performance target. The fixed-clock heavy dock pair spent 171.64 ms/tick in Fish End Step with the grid off and 179.27 ms/tick with it on. All-7 ran faster with the grid on in the measured pairs, but the shared-VPS captures could not isolate the grid's contribution. Every compared fish-state hash and explicit RNG count matched. Heavy dock and both all-7 scenes hit the 600-second cap before completing a full comparison.

## Tooling and inputs

The benchmark and GML profiler landed together in merge commit `9916593`. The existing silo bench entry point remains available. The audit used the 2023-08-16 legacy archive, the local TGE + Antiquity + PWB build (`heavy-main.win`), and the local seven-mod build (`all7.win`, adding Abyss, Colour, Verdant and Cave). All source archives, generated archives, decompilations and raw runtime captures stayed on the VPS.

I resolved object indices against the archive's object table and inspected the decompiled selectors, call sites and state writes. A profiler call is a GML entry invocation. It counts each parent event or helper call once even if it performs several engine scans or takes a branch that performs no scan. Inclusive shares can overlap.

The revised all-7 OFF/ON runs use `all7-repack-verify.win` to reduce atlas load time within the 600-second budget. Its object index and room summaries matched the unrepacked archive. The avoidance, Fish End Step, FishThinner Step and procedural-spawner Create decompilations also matched. The script audit rates use the unrepacked archive.

## Neighbour audit

| Script or event | Radius, ordering and effects | Grid decision |
|---|---|---|
| `fish_avoid_fish` | Strict squared-distance cutoff `abs(argument1)`. Accumulates both velocity components in selector traversal order. Coincident distinct instances still reach the original arithmetic. The last non-self visit supplies `sdist`, including a fish outside the cutoff. | Uses the grid for `FishLarge` and radii through 1,024. Bagu, dynamic `object_index` selectors and larger radii keep the original loop. Audited callers use 40, 50, 300, 500 and 1,000 for the large-fish selector. |
| `fish_flock` | Unbounded same-species nearest query on a random refresh or missing partner. Temporarily deactivates self, then rejects a partner by state and applies spring-like velocity changes. | Native nearest and reactivation order retained. |
| `fish_shove_schoolmates` | Precise shape membership from `instance_place` at the current position. Uses one returned instance. The 1–500 distance clamp affects force after the lookup. | Native collision masks and hit selection retained. |
| `fish_fleelarge`, `fish_fleehuge` | Unbounded native nearest, then a size-dependent test of 100 × (display size − 2), with 10 extra pixels in state 9. Includes state and random gates. | Native nearest and tie handling retained. |
| `fish_target_nearest_fish`, `fish_target_nearest_fish_late` | Unbounded nearest against a supplied asset. Rejects and deactivates candidates before querying again. The ordinary version breaks when rejecting a candidate beyond 5,000. The late version tests the candidate's target. Reactivates the selector. | Each scan observes the current active set and engine ties. |
| `fish_target_furthest_fish` | Unbounded repeated furthest lookup, with state rejection and temporary deactivation. | Native furthest selection retained. |
| `fish_target_random_fish`, `fish_target_random_living_fish`, `random_fish` | Random index into the active instance order. The living variant rejects by state and temporarily deactivates before redrawing. | Keep the same indexed population and RNG consumption. |
| `fish_swarm_at_fish_with_percent`, `fish_target_random_fish_with_percent` | Nearest or random target selection followed by unbounded same-species traversal and a random test per visit. Can overwrite targets on other fish. | Full traversal, RNG and side-effect order retained. |
| `fish_lunge`, `check_lunge`, `check_lunge_catswim` | Lookup against the single lure or swimming cat and possible hooked fish. Uses a gear- and fish-dependent radius and an obstruction test against scenery. | No fish-neighbour scan to replace. |
| `FishSmallSwarm_Other_10` | Global nearest of medium, early large and huge fish, plus a same-species nearest from an offset leader position. | Multiple unbounded native selections. |
| `Chaw_Other_10`, `Cuddlefish_Other_10`, `Clownfish_Other_10`, `Conpas_Other_10`, `Dire_Trillow_Other_10`, `Red_Skitter_Other_10`, `Snobfish_Other_10`, `Tetra_Other_10`, `Trillow_Other_10` | Global nearest of two or more fish classes, followed by state- and display-size-dependent fleeing, pairing or follow logic. Snobfish also chooses small fish and Nightfish. | Unbounded choices and engine ties. |
| `Akuma_Other_13`, `Blooper_Other_10`, `Bulbfish_Other_10`, `Bullfish_Other_10`, `Gallina_Other_13`, `Smarty_Other_13`, `Tappy_Other_10` | Global nearest against large, huge, medium or species selectors. Some distance checks use the lure or a second target as the origin after selection. | Preserve the selected instance even when it is outside a local circle. |
| `Ceephalio_Other_10` | Unbounded engine nearest of Cephalopoid. | Native target selection retained. |
| `Centy_Create_0`, `Chrono_Create_0`, `Thermo_Create_0`, `Ghost_Create_0`, `Goldfish_Create_0`, `Torby_Create_0` | Whole-species initialization traversal or first indexed instance. Runs at creation. | Initialization order and partner references retained. |
| `Bagu_Other_13`, `Curby_Other_13` | Global same-species nearest with self handling and post-selection possession tests. Bagu uses 300 and 500, Curby 100. | Native nearest and activation behavior retained. |
| `Cave_Shark_Other_13`, `Chaw_Step_0`, `Cuddlebig_Other_10`, `Cuddlered_Other_13`, `Fennec_Other_10`, `Flechette_Create_0`, `Flechette_Other_10`, `Fumer_Other_18`, `Kingfish_Other_10`, `Lil_Cheesefin_Other_10`, `Magmer_Step_0`, `Maw_Create_0`, `Maw_Other_10`, `Nightfish_Other_18`, `Rogachev_Other_18`, `Sibilfin_Other_13`, `Spallow_Other_13`, `Thermo_Other_10` | Unbounded engine target/partner lookups. Species, offset origins and post-search checks differ. | Engine tie, mask and fallback semantics retained. |
| `Bluecheek_Frog_Other_10`, `Bluecheek_Frog_Step_0` | Global nearest followed by a 250-unit test in the user event. The Step selects the first nearest fish within 250 after self, state and size checks. Present in both packs. | Different selection and Step phase, left in place. |
| `Skitter_Other_10`, `Stalker_Other_10` | Skitter compares same-species pair distances and also queries `FishNotSmall` nearest. Stalker walks `FishHugeEarly` and marks the first unstalked candidate and breaks. | Unbounded or order-sensitive selection. |
| `FishYinYang_Other_10/17`, `EvilCouple_Other_10/17`, `Red_Skitter_Other_16` | Whole-species loops change or pick partners, including cross-instance state writes. EvilCouple appears in both packs. | No safe spatial cutoff. |
| `Puff_Other_13`, `Boom_Puff_Other_13`, `Felish_Other_25`, `Spotter_Other_25`, `Nightfish_Sire_Other_10`, `Tangfin_Other_13` | Native collision or nearest-then-bounding-box queries. Puff uses radius 28 and can break on the first hit, Felish and Spotter use radius 500, Nightfish Sire uses radius 45 with an offset origin, and Tangfin tests bounding-box distance <60 after nearest. | Keep mask geometry, engine tie/first-hit order and state effects. |
| `fish_check_unique`, `fish_check_exotic` | Unbounded scan over caught fish. Filters by state, species and boat ownership, with writes to uniqueness/rarity. | All candidates are required. |

Several other fish traversals are driven by a player action, a controller or a single instance reference. These include `Bomb_Other_10` and `BombNoPal_Other_10` (scaled explosion radius and mask checks), `CatSwim_Other_10` (line/circle hit checks), `CatAnchor_Step_1` and `Swordfish_Other_10` (specific instance hit checks), `FishThinner` and `Zoner` (streaming/activation), `SonarPanel` (drawing and visibility), `SaveManager` (save/load), and the attachment and quest handlers `Book_Step_2`, `Egg_Step_2`, `SmileyEgg_Step_2` and `Girl_Step_0`. `CatalogData`, `Boat_Step_0`, `Lure_Step_0`, `Rod_Other_10/16`, `levelup` and `sell_fish` also enumerate fish for gameplay bookkeeping. The audit found no fish-neighbour traversal over a DS list in these inputs. Lists used for UI, catalog bookkeeping or saved collections did not feed a spatial fish query.

## Runtime design

`[Perf] FishGrid=1` in `jamm_perf.ini` enables the grid independently of F8. Missing settings default to on when Perf is present. JAMM applies the rewrite after merging and verifying the selected GML. An altered avoidance pattern logs a `[CGF-PERF-GRID]` decision and keeps the merged script. The check accepted all three benchmark inputs and also accepts the existing Perf version with its two arithmetic invariants hoisted.

The controller resets validity in Begin Step. It builds lazily during the first eligible Fish End Step query, after engine movement. One `with (FishLarge)` walk captures each active instance's traversal ordinal. The query unions the relevant cells, sorts those ordinals, and executes the original impulse math against current positions. It also preserves the final `sdist` write.

Cells are 1,024 pixels wide. Queries include padding for up to 32 pixels of movement per axis from the last recorded position. Larger moves update the recorded position and relink the fish if it changes cells, without changing its traversal ordinal. A position outside the indexed bounds or a write with a potentially different instance as its destination invalidates the snapshot. Creation, destruction, activation and object changes invalidate before a tracked candidate can enter, leave or change order. This catches same-count replacement. The query also checks the room and the active selector count. Once invalidated, that tick uses the original loop.

## Validation and measurements
<!-- Recorded measurements -->

The Release solution build completed with zero warnings. The .NET suite passed 460 core and 127 GUI tests, with 15 real-file tests skipped by default. The original-only managed Perf build also passed semantic reference validation and archive read-back. The GML fixture checks 500 ordered queries against a brute-force loop, negative and large coordinates, movement between cells, same-count replacement and deactivate/reactivate. Both the original and revised fixtures passed in the legacy Wine runner.

The benchmark runs used Wine, the same 1200×720 viewport for performance captures, default scene coordinates and seed 1337. F8 scheduling was off. Each pair uses the same benchmark archive with a different `jamm_perf.ini` setting. The trace freezes simulation-facing time, date, frame delta and the FPS input to FishThinner. It suppresses raster submission while retaining Draw events. Hashes cover the active Fish traversal, binary positions, velocities, scale, path state and selected fish state variables. The RNG column counts explicit calls reachable from GML.

### Script and event call rates

Each cell reports entry calls per tick / percentage of the profiled loop. Zero means the capture recorded no invocation. A dash means the capture was unavailable.

| Entry | Vanilla dock | Vanilla deepwater | Heavy dock | Heavy deepwater | All-7 dock | All-7 deepwater |
|---|---:|---:|---:|---:|---:|---:|
| `fish_avoid_fish` | 110.13 / 4.21% | 112.75 / 4.72% | 160.59 / 4.63% | 162.73 / 4.20% | 165.86 / 4.94% | 165.05 / 4.87% |
| `fish_flock` | 49.79 / 0.11% | 46.99 / 0.13% | 67.98 / 0.10% | 78.50 / 0.07% | 76.14 / 0.05% | 80.75 / 0.05% |
| `fish_shove_schoolmates` | 54.80 / 0.28% | 49.76 / 0.27% | 63.10 / 0.13% | 65.27 / 0.10% | 77.00 / 0.06% | 77.00 / 0.06% |
| `fish_fleelarge` | 281.85 / 0.61% | 253.61 / 0.70% | 415.02 / 0.68% | 435.37 / 0.57% | 803.90 / 1.03% | 819.15 / 1.11% |
| `fish_fleehuge` | 227.28 / 0.28% | 217.27 / 0.30% | 305.07 / 0.24% | 306.67 / 0.28% | 540.19 / 0.27% | 541.10 / 0.35% |
| `fish_target_nearest_fish` | 1.04 / 0.09% | 1.01 / 0.11% | 1.02 / 0.11% | 1.00 / 0.08% | 1.05 / 0.08% | 1.05 / 0.08% |
| `fish_target_nearest_fish_late` | 0.07 / 0.00% | 0.10 / 0.01% | 0.07 / 0.01% | 0.07 / 0.00% | 0.14 / 0.01% | 0.00 / 0.00% |
| `fish_target_furthest_fish` | 0.00 / 0.00% | 0.02 / 0.00% | 0.02 / 0.00% | 0.07 / 0.01% | 0.05 / 0.00% | 0.10 / 0.01% |
| `fish_target_random_fish` | 0.06 / 0.00% | 0.05 / 0.00% | 0.05 / 0.00% | 0.03 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% |
| `fish_target_random_fish_with_percent` | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% |
| `fish_target_random_living_fish` | 0.11 / 0.01% | 0.09 / 0.01% | 0.20 / 0.02% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% |
| `fish_swarm_at_fish_with_percent` | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% |
| `fish_check_unique` | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% |
| `fish_check_exotic` | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% |
| `random_fish` | 8.79 / 0.05% | 8.98 / 0.06% | 9.22 / 0.05% | 11.60 / 0.05% | 0.00 / 0.00% | 0.00 / 0.00% |
| `fish_lunge` | 1392.20 / 11.91% | 1307.43 / 12.83% | 2182.24 / 9.81% | 2240.57 / 9.24% | 3630.19 / 8.02% | 3628.40 / 8.06% |
| `check_lunge` | 1379.64 / 0.98% | 1298.04 / 1.08% | 2169.29 / 0.81% | 2229.30 / 0.66% | 3600.62 / 0.70% | 3600.45 / 0.74% |
| `check_lunge_catswim` | 1379.64 / 0.53% | 1298.04 / 0.58% | 2169.29 / 0.42% | 2229.30 / 0.40% | 3600.62 / 0.37% | 3600.45 / 0.35% |
| `Akuma_Other_13` | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% |
| `Bagu_Other_13` | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% |
| `Blooper_Other_10` | 2.80 / 0.24% | 4.03 / 0.40% | 4.68 / 0.26% | 4.83 / 0.23% | 5.00 / 0.22% | 5.00 / 0.23% |
| `Bluecheek_Frog_Other_10` | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% |
| `Bluecheek_Frog_Step_0` | 0.00 / 0.00% | 0.00 / 0.00% | 2.00 / 0.91% | 2.00 / 0.82% | 2.00 / 0.74% | 2.00 / 0.69% |
| `Boom_Puff_Other_13` | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% |
| `Bulbfish_Other_10` | 77.00 / 1.30% | 70.53 / 1.30% | 98.32 / 0.95% | 98.27 / 0.88% | 121.57 / 0.60% | 123.45 / 0.66% |
| `Bullfish_Other_10` | 7.31 / 0.11% | 5.92 / 0.10% | 7.88 / 0.07% | 9.00 / 0.07% | 9.00 / 0.03% | 9.00 / 0.04% |
| `Cave_Shark_Other_13` | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% |
| `Ceephalio_Other_10` | 0.00 / 0.00% | 0.00 / 0.00% | 10.88 / 0.09% | 11.40 / 0.07% | 12.00 / 0.05% | 12.00 / 0.05% |
| `Centy_Create_0` | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% |
| `Chaw_Other_10` | 101.99 / 1.67% | 95.70 / 1.76% | 122.02 / 1.05% | 125.53 / 1.02% | 131.00 / 0.53% | 130.00 / 0.58% |
| `Chaw_Step_0` | 103.54 / 0.12% | 96.26 / 0.12% | 122.41 / 0.07% | 126.20 / 0.12% | 131.00 / 0.15% | 131.00 / 0.14% |
| `Chrono_Create_0` | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% |
| `Clownfish_Other_10` | 5.00 / 0.09% | 5.00 / 0.12% | 5.00 / 0.06% | 5.00 / 0.05% | 5.00 / 0.03% | 5.00 / 0.05% |
| `Conpas_Other_10` | 10.05 / 0.16% | 12.22 / 0.23% | 12.00 / 0.10% | 13.80 / 0.10% | 15.00 / 0.07% | 15.00 / 0.07% |
| `Cuddlebig_Other_10` | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% |
| `Cuddlefish_Other_10` | 167.66 / 3.30% | 155.55 / 2.55% | 213.83 / 2.17% | 211.83 / 1.17% | 221.33 / 1.18% | 224.05 / 0.74% |
| `Cuddlered_Other_13` | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% |
| `Curby_Other_13` | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% |
| `Dire_Trillow_Other_10` | 6.74 / 0.10% | 7.84 / 0.16% | 8.44 / 0.06% | 8.70 / 0.06% | 9.00 / 0.03% | 9.00 / 0.05% |
| `EvilCouple_Other_10` | 0.00 / 0.00% | 0.00 / 0.00% | 2.00 / 0.05% | 2.00 / 0.04% | 2.00 / 0.05% | 2.00 / 0.04% |
| `EvilCouple_Other_17` | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% |
| `Felish_Other_25` | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% |
| `Fennec_Other_10` | 1.00 / 0.01% | 1.00 / 0.01% | 1.00 / 0.01% | 1.00 / 0.01% | 1.00 / 0.01% | 1.00 / 0.01% |
| `FishSmallSwarm_Other_10` | 57.16 / 1.38% | 49.15 / 1.40% | 63.95 / 0.90% | 64.50 / 0.88% | 69.00 / 0.51% | 69.00 / 0.53% |
| `FishYinYang_Other_10` | 6.00 / 0.14% | 6.00 / 0.15% | 6.00 / 0.15% | 6.00 / 0.11% | 6.00 / 0.12% | 6.00 / 0.13% |
| `FishYinYang_Other_17` | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% |
| `Flechette_Create_0` | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% |
| `Flechette_Other_10` | 5.00 / 0.10% | 5.00 / 0.11% | 5.00 / 0.05% | 5.00 / 0.04% | 5.00 / 0.02% | 5.00 / 0.02% |
| `Fumer_Other_18` | 16.59 / 0.20% | 11.77 / 0.17% | 17.76 / 0.12% | 19.10 / 0.11% | 20.00 / 0.06% | 20.00 / 0.06% |
| `Gallina_Other_13` | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% |
| `Ghost_Create_0` | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% |
| `Goldfish_Create_0` | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% |
| `Kingfish_Other_10` | 68.36 / 1.71% | 66.02 / 1.31% | 82.63 / 1.14% | 80.33 / 0.54% | 85.38 / 0.64% | 89.75 / 0.36% |
| `Lil_Cheesefin_Other_10` | 7.33 / 0.12% | 5.92 / 0.12% | 9.00 / 0.08% | 7.80 / 0.08% | 0.00 / 0.00% | 0.00 / 0.00% |
| `Magmer_Step_0` | 2.00 / 0.01% | 2.00 / 0.01% | 2.00 / 0.01% | 2.00 / 0.01% | 9.00 / 0.02% | 9.00 / 0.02% |
| `Maw_Create_0` | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% |
| `Maw_Other_10` | 1.00 / 0.01% | 1.00 / 0.02% | 1.00 / 0.01% | 1.00 / 0.01% | 1.00 / 0.00% | 1.00 / 0.00% |
| `Nightfish_Other_18` | 7.87 / 0.19% | 8.04 / 0.23% | 7.88 / 0.18% | 8.40 / 0.16% | 9.00 / 0.17% | 9.00 / 0.17% |
| `Nightfish_Sire_Other_10` | 3.00 / 0.10% | 3.00 / 0.13% | 5.46 / 0.15% | 5.70 / 0.13% | 6.00 / 0.14% | 6.00 / 0.13% |
| `Puff_Other_13` | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% |
| `Red_Skitter_Other_10` | 4.00 / 0.06% | 4.00 / 0.06% | 4.00 / 0.03% | 4.00 / 0.02% | 4.00 / 0.02% | 4.00 / 0.01% |
| `Red_Skitter_Other_16` | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% |
| `Rogachev_Other_18` | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% |
| `Sibilfin_Other_13` | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% |
| `Skitter_Other_10` | 31.89 / 0.55% | 27.32 / 0.44% | 38.32 / 0.39% | 38.80 / 0.22% | 40.00 / 0.18% | 40.00 / 0.13% |
| `Smarty_Other_13` | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% |
| `Snobfish_Other_10` | 72.69 / 1.13% | 68.96 / 1.17% | 83.44 / 0.72% | 88.73 / 0.64% | 93.24 / 0.39% | 94.00 / 0.39% |
| `Spallow_Other_13` | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% |
| `Spotter_Other_25` | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% |
| `Stalker_Other_10` | 1.95 / 0.01% | 1.98 / 0.02% | 2.00 / 0.01% | 1.97 / 0.01% | 1.95 / 0.00% | 2.00 / 0.01% |
| `Tangfin_Other_13` | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% |
| `Tappy_Other_10` | 9.60 / 0.14% | 8.63 / 0.15% | 10.44 / 0.08% | 10.70 / 0.07% | 11.00 / 0.04% | 11.00 / 0.04% |
| `Tetra_Other_10` | 15.78 / 0.22% | 12.98 / 0.22% | 19.44 / 0.15% | 19.40 / 0.10% | 20.00 / 0.07% | 20.00 / 0.07% |
| `Thermo_Create_0` | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% |
| `Thermo_Other_10` | 4.00 / 0.08% | 4.00 / 0.09% | 4.00 / 0.04% | 4.00 / 0.03% | 17.00 / 0.09% | 17.00 / 0.09% |
| `Torby_Create_0` | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% | 0.00 / 0.00% |
| `Trillow_Other_10` | 7.15 / 0.10% | 5.96 / 0.10% | 8.88 / 0.07% | 9.70 / 0.08% | 10.00 / 0.04% | 10.00 / 0.03% |

### Before and after

The initial 512-pixel grid did not improve the heavy dock timing. It measured 181.92 ms/tick OFF and 191.29 ms/tick ON, with 0.750 ms/tick spent building the grid and 8.33 grid queries served per tick. An instrumented follow-up attributed 24 of 31 observed first invalidations to tracked fish moving more than 32 pixels. Active-set changes accounted for the other seven. The revised grid keeps large coordinate changes in the spatial index by relinking the existing ordinal.

These are event-profile captures with the same instrumentation on both sides. Fish time is inclusive elapsed time inside `Fish_Step_2`, and µs/call divides it by executed Fish End Step events. Whole-loop time includes update and draw work. Grid-build time averages across every captured tick, including ticks without a build.

| Pack | Scene | Grid | Ticks | Fish End Step calls/tick | Fish End Step ms/tick | µs/call | Whole loop ms/tick | Grid build ms/tick | Grid collects/tick |
|---|---|---|---:|---:|---:|---:|---:|---:|---:|
| vanilla | dock | 0 | 148 | 1271.59 | 70.38 | 55.35 | 163.93 | 0.000 | 0.00 |
| vanilla | dock | 1 | 103 | 1454.59 | 98.47 | 67.70 | 237.22 | 0.812 | 47.60 |
| vanilla | deepwater | 0 | 181 | 1180.59 | 61.40 | 52.00 | 133.88 | 0.000 | 0.00 |
| vanilla | deepwater | 1 | 169 | 1195.79 | 64.28 | 53.76 | 143.25 | 0.545 | 16.09 |
| heavy | dock | 0 | 54 | 2263.04 | 192.98 | 85.27 | 455.10 | 0.000 | 0.00 |
| heavy | dock | 1 | 30 | 2408.57 | 252.69 | 104.91 | 830.12 | 1.043 | 19.07 |
| heavy | deepwater | 0 | 58 | 2237.55 | 180.28 | 80.57 | 429.60 | 0.000 | 0.00 |
| heavy | deepwater | 1 | 51 | 2287.27 | 200.65 | 87.72 | 481.80 | 0.977 | 17.69 |
| all7 | dock | 0 | 13 | 3855.00 | 814.41 | 211.26 | 1955.81 | 0.000 | 0.00 |
| all7 | dock | 1 | 26 | 3781.85 | 465.25 | 123.02 | 951.16 | 1.877 | 17.35 |
| all7 | deepwater | 0 | 23 | 3821.52 | 509.01 | 133.20 | 1087.04 | 0.000 | 0.00 |
| all7 | deepwater | 1 | 25 | 3793.40 | 451.30 | 118.97 | 986.72 | 1.857 | 80.44 |

A second heavy dock pair used a fixed 120-tick window with the trace clock and raster suppression on both sides. The traces were byte-identical, and both sides executed 250,546 Fish End Step events. The measured Fish End Step time was 171.64 ms/tick OFF and 179.27 ms/tick ON. Whole-loop time rose from 394.00 to 407.97 ms/tick. The grid built at 0.860 ms/tick and served 26.74 queries/tick. This controlled pair showed no CPU reduction for heavy dock. The first ordinary all-7 dock pair had substantially different whole-loop times and only 13 OFF ticks, so the apparent speedup in that row cannot isolate the grid.

The fixed 120-tick all-7 dock pair also produced byte-identical traces, with 390,412 Fish End Step events on each side. The observed Fish End Step time fell from 452.08 to 400.31 ms/tick, while whole-loop time fell from 946.60 to 843.00 ms/tick. Grid build cost was 1.780 ms/tick with 29.54 grid queries served per tick. Both Fish End Step and the rest of the loop moved in the same direction. With one paired capture on the shared VPS, these timings do not establish how much of the difference came from the grid.

### Trace comparison

The initial 512-pixel implementation produced matching 30-tick and 1,800-tick vanilla dock pairs. The table below covers the revised grid. Every completed pair was byte-identical. Heavy dock ON and both states in each all-7 scene reached the 600-second cap. A heavy dock retry reduced buffer writes with per-fish presence masks and reached 1,695 OFF ticks. The matching prefixes establish agreement only over the ticks shown.

| Pack | Scene | OFF ticks | ON ticks | Comparison |
|---|---|---:|---:|---|
| vanilla | dock | 1800 | 1800 | Identical |
| vanilla | deepwater | 1800 | 1800 | Identical |
| heavy | dock | 1800 | 1603 | First 1,603 ticks match |
| heavy | deepwater | 1800 | 1800 | Identical |
| all7 | dock | 742 | 609 | First 609 ticks match |
| all7 | deepwater | 675 | 842 | First 675 ticks match |

The byte-identical trace files have these SHA-256 digests.

| Pack and scene | SHA-256 |
|---|---|
| Vanilla dock | `7bb7349e716ae77dd626ce9a3dd379ec708417027f4522e02dc1c5935120d54f` |
| Vanilla deepwater | `97d86526e14b6f25442e7f1ba630672f2444b80ad20a6e806b4f8f7299cf8cd5` |
| Heavy deepwater | `5481f67fe2af77355a998fea9431355f93f4ede91f34053a9bc8e82e672da426` |

The raw captures are on the VPS under `~/jamm-night/runs/` in directories named `fish-grid-baseline-*`, `fish-grid-profile-relink-*`, `fish-grid-controlled-*` and `fish-grid-trace-relink-*`.
