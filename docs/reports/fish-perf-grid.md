# Fish neighbour grid

## Tooling and inputs

The benchmark and GML profiler landed together in merge commit `9916593`. The existing silo bench entry point remains available. The audit used the 2023-08-16 legacy archive, the local TGE + Antiquity + PWB build (`heavy-main.win`), and the local seven-mod build (`all7.win`, adding Abyss, Colour, Verdant and Cave). All source archives, generated archives, decompilations and raw runtime captures stayed on the VPS.

I resolved object indices against the archive's object table and inspected the decompiled selectors, call sites and state writes. A profiler call is a GML entry invocation. It counts each parent event or helper call once even if it performs several engine scans or takes a branch that performs no scan. Inclusive shares can overlap.

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

Several other fish traversals are driven by a player action, a controller or a single instance reference. These include `Bomb_Other_10` and `BombNoPal_Other_10` (scaled explosion radius and mask checks), `CatSwim_Other_10` (line/circle hit checks), `CatAnchor_Step_1` and `Swordfish_Other_10` (specific instance hit checks), `FishThinner` and `Zoner` (streaming/activation), `SonarPanel` (drawing and visibility), `SaveManager` (save/load), `CatalogData`, `Boat_Step_0`, `Lure_Step_0`, `Rod_Other_10/16`, `levelup` and `sell_fish`. The audit found no fish-neighbour traversal over a DS list in these inputs. Lists used for UI, catalog bookkeeping or saved collections did not feed a spatial fish query.

## Runtime design

`[Perf] FishGrid=1` in `jamm_perf.ini` enables the grid independently of F8. Missing settings default to on when Perf is present. JAMM applies the rewrite after merging and verifying the selected GML. An altered avoidance pattern logs a `[CGF-PERF-GRID]` decision and keeps the merged script. The same check accepts the existing Perf version with its two arithmetic invariants hoisted.

The controller resets validity in Begin Step. It builds lazily during the first eligible Fish End Step query, after engine movement. One `with (FishLarge)` walk captures each active instance's traversal ordinal. The query unions the relevant cells, sorts those ordinals, and executes the original impulse math against current positions. It also preserves the final `sdist` write.

Cells are 1,024 pixels wide. Queries include padding for up to 32 pixels of movement per axis from the last recorded position. Larger moves update the recorded position and relink the fish if it changes cells, without changing its traversal ordinal. A position outside the indexed bounds or a write with a potentially different instance as its destination invalidates the snapshot. Creation, destruction, activation and object changes invalidate before a tracked candidate can enter, leave or change order. This catches same-count replacement. The query also checks the room and the active selector count. Once invalidated, that tick uses the original loop.

## Validation and measurements
<!-- Recorded measurements -->

The Release solution build completed with zero warnings. The .NET suite passed 460 core and 127 GUI tests, with 15 real-file tests skipped by default. The original-only managed Perf build also passed semantic reference validation and archive read-back. The GML fixture checks 500 ordered queries against a brute-force loop, negative and large coordinates, movement between cells, same-count replacement and deactivate/reactivate. The first fixture passed in the legacy Wine runner.

The benchmark runs used Wine, the same 1200×720 viewport for performance captures, default scene coordinates and seed 1337. F8 scheduling was off. Each pair uses the same benchmark archive with a different `jamm_perf.ini` setting. The trace freezes simulation-facing time, date, frame delta and the FPS input to FishThinner. It suppresses raster submission while retaining Draw events. Hashes cover the active Fish traversal, binary positions, velocities, scale, path state and selected fish state variables. The RNG column counts explicit calls reachable from GML.

### Script and event call rates

Each cell reports entry calls per tick / percentage of the profiled loop. A dash means the capture was unavailable.

| Entry | Vanilla dock | Heavy dock | All-7 dock |
|---|---:|---:|---:|
| `fish_avoid_fish` | 110.13 / 4.21% | 160.59 / 4.63% | — |
| `fish_flock` | 49.79 / 0.11% | 67.98 / 0.10% | — |
| `fish_shove_schoolmates` | 54.80 / 0.28% | 63.10 / 0.13% | — |
| `fish_fleelarge` | 281.85 / 0.61% | 415.02 / 0.68% | — |
| `fish_fleehuge` | 227.28 / 0.28% | 305.07 / 0.24% | — |
| `fish_target_nearest_fish` | 1.04 / 0.09% | 1.02 / 0.11% | — |
| `fish_target_nearest_fish_late` | 0.07 / 0.00% | 0.07 / 0.01% | — |
| `fish_target_furthest_fish` | 0.00 / 0.00% | 0.02 / 0.00% | — |
| `fish_target_random_fish` | 0.06 / 0.00% | 0.05 / 0.00% | — |
| `fish_target_random_fish_with_percent` | 0.00 / 0.00% | 0.00 / 0.00% | — |
| `fish_target_random_living_fish` | 0.11 / 0.01% | 0.20 / 0.02% | — |
| `fish_swarm_at_fish_with_percent` | 0.00 / 0.00% | 0.00 / 0.00% | — |
| `fish_check_unique` | 0.00 / 0.00% | 0.00 / 0.00% | — |
| `fish_check_exotic` | 0.00 / 0.00% | 0.00 / 0.00% | — |
| `random_fish` | 8.79 / 0.05% | 9.22 / 0.05% | — |
| `fish_lunge` | 1392.20 / 11.91% | 2182.24 / 9.81% | — |
| `check_lunge` | 1379.64 / 0.98% | 2169.29 / 0.81% | — |
| `check_lunge_catswim` | 1379.64 / 0.53% | 2169.29 / 0.42% | — |
| `Akuma_Other_13` | 0.00 / 0.00% | 0.00 / 0.00% | — |
| `Bagu_Other_13` | 0.00 / 0.00% | 0.00 / 0.00% | — |
| `Blooper_Other_10` | 2.80 / 0.24% | 4.68 / 0.26% | — |
| `Bluecheek_Frog_Other_10` | 0.00 / 0.00% | 0.00 / 0.00% | — |
| `Bluecheek_Frog_Step_0` | 0.00 / 0.00% | 2.00 / 0.91% | — |
| `Boom_Puff_Other_13` | 0.00 / 0.00% | 0.00 / 0.00% | — |
| `Bulbfish_Other_10` | 77.00 / 1.30% | 98.32 / 0.95% | — |
| `Bullfish_Other_10` | 7.31 / 0.11% | 7.88 / 0.07% | — |
| `Cave_Shark_Other_13` | 0.00 / 0.00% | 0.00 / 0.00% | — |
| `Ceephalio_Other_10` | 0.00 / 0.00% | 10.88 / 0.09% | — |
| `Centy_Create_0` | 0.00 / 0.00% | 0.00 / 0.00% | — |
| `Chaw_Other_10` | 101.99 / 1.67% | 122.02 / 1.05% | — |
| `Chaw_Step_0` | 103.54 / 0.12% | 122.41 / 0.07% | — |
| `Chrono_Create_0` | 0.00 / 0.00% | 0.00 / 0.00% | — |
| `Clownfish_Other_10` | 5.00 / 0.09% | 5.00 / 0.06% | — |
| `Conpas_Other_10` | 10.05 / 0.16% | 12.00 / 0.10% | — |
| `Cuddlebig_Other_10` | 0.00 / 0.00% | 0.00 / 0.00% | — |
| `Cuddlefish_Other_10` | 167.66 / 3.30% | 213.83 / 2.17% | — |
| `Curby_Other_13` | 0.00 / 0.00% | 0.00 / 0.00% | — |
| `Dire_Trillow_Other_10` | 6.74 / 0.10% | 8.44 / 0.06% | — |
| `Felish_Other_25` | 0.00 / 0.00% | 0.00 / 0.00% | — |
| `FishSmallSwarm_Other_10` | 57.16 / 1.38% | 63.95 / 0.90% | — |
| `FishYinYang_Other_10` | 6.00 / 0.14% | 6.00 / 0.15% | — |
| `FishYinYang_Other_17` | 0.00 / 0.00% | 0.00 / 0.00% | — |
| `Flechette_Create_0` | 0.00 / 0.00% | 0.00 / 0.00% | — |
| `Flechette_Other_10` | 5.00 / 0.10% | 5.00 / 0.05% | — |
| `Fumer_Other_18` | 16.59 / 0.20% | 17.76 / 0.12% | — |
| `Gallina_Other_13` | 0.00 / 0.00% | 0.00 / 0.00% | — |
| `Ghost_Create_0` | 0.00 / 0.00% | 0.00 / 0.00% | — |
| `Goldfish_Create_0` | 0.00 / 0.00% | 0.00 / 0.00% | — |
| `Kingfish_Other_10` | 68.36 / 1.71% | 82.63 / 1.14% | — |
| `Lil_Cheesefin_Other_10` | 7.33 / 0.12% | 9.00 / 0.08% | — |
| `Magmer_Step_0` | 2.00 / 0.01% | 2.00 / 0.01% | — |
| `Maw_Create_0` | 0.00 / 0.00% | 0.00 / 0.00% | — |
| `Maw_Other_10` | 1.00 / 0.01% | 1.00 / 0.01% | — |
| `Nightfish_Other_18` | 7.87 / 0.19% | 7.88 / 0.18% | — |
| `Nightfish_Sire_Other_10` | 3.00 / 0.10% | 5.46 / 0.15% | — |
| `Puff_Other_13` | 0.00 / 0.00% | 0.00 / 0.00% | — |
| `Red_Skitter_Other_10` | 4.00 / 0.06% | 4.00 / 0.03% | — |
| `Red_Skitter_Other_16` | 0.00 / 0.00% | 0.00 / 0.00% | — |
| `Rogachev_Other_18` | 0.00 / 0.00% | 0.00 / 0.00% | — |
| `Sibilfin_Other_13` | 0.00 / 0.00% | 0.00 / 0.00% | — |
| `Skitter_Other_10` | 31.89 / 0.55% | 38.32 / 0.39% | — |
| `Smarty_Other_13` | 0.00 / 0.00% | 0.00 / 0.00% | — |
| `Snobfish_Other_10` | 72.69 / 1.13% | 83.44 / 0.72% | — |
| `Spallow_Other_13` | 0.00 / 0.00% | 0.00 / 0.00% | — |
| `Spotter_Other_25` | 0.00 / 0.00% | 0.00 / 0.00% | — |
| `Stalker_Other_10` | 1.95 / 0.01% | 2.00 / 0.01% | — |
| `Tangfin_Other_13` | 0.00 / 0.00% | 0.00 / 0.00% | — |
| `Tappy_Other_10` | 9.60 / 0.14% | 10.44 / 0.08% | — |
| `Tetra_Other_10` | 15.78 / 0.22% | 19.44 / 0.15% | — |
| `Thermo_Create_0` | 0.00 / 0.00% | 0.00 / 0.00% | — |
| `Thermo_Other_10` | 4.00 / 0.08% | 4.00 / 0.04% | — |
| `Torby_Create_0` | 0.00 / 0.00% | 0.00 / 0.00% | — |
| `Trillow_Other_10` | 7.15 / 0.10% | 8.88 / 0.07% | — |

### Before and after

The initial 512-pixel grid did not improve the heavy dock timing. It measured 181.92 ms/tick OFF and 191.29 ms/tick ON, with 0.750 ms/tick spent building the grid and 8.33 grid queries served per tick. An instrumented follow-up attributed 24 of 31 observed first invalidations to tracked fish moving more than 32 pixels. Active-set changes accounted for the other seven. The revised grid keeps large coordinate changes in the spatial index by relinking the existing ordinal.

These are event-profile captures with the same instrumentation on both sides. Fish time is inclusive elapsed time inside `Fish_Step_2`, and µs/call divides it by executed Fish End Step events. Whole-loop time includes update and draw work. Grid-build time averages across every captured tick, including ticks without a build.

| Pack | Scene | Grid | Ticks | Fish End Step calls/tick | Fish End Step ms/tick | µs/call | Whole loop ms/tick | Grid build ms/tick | Grid collects/tick |
|---|---|---|---:|---:|---:|---:|---:|---:|---:|
| vanilla | dock | 0 | — | — | — | — | — | — | — |
| vanilla | dock | 1 | — | — | — | — | — | — | — |
| vanilla | deepwater | 0 | — | — | — | — | — | — | — |
| vanilla | deepwater | 1 | — | — | — | — | — | — | — |
| heavy | dock | 0 | — | — | — | — | — | — | — |
| heavy | dock | 1 | — | — | — | — | — | — | — |
| heavy | deepwater | 0 | — | — | — | — | — | — | — |
| heavy | deepwater | 1 | — | — | — | — | — | — | — |
| all7 | dock | 0 | — | — | — | — | — | — | — |
| all7 | dock | 1 | — | — | — | — | — | — | — |
| all7 | deepwater | 0 | — | — | — | — | — | — | — |
| all7 | deepwater | 1 | — | — | — | — | — | — | — |

### Trace comparison

The initial 512-pixel implementation produced matching 30-tick and 1,800-tick vanilla dock pairs. The full pair's SHA-256 was `6c835cd0cd9339269f888152892b35d7e5b80a561b66ad98bbd724d1362a7a70`. The table below refers to the revised grid that relinks moving fish.

| Pack | Scene | OFF ticks | ON ticks | Comparison |
|---|---|---:|---:|---|
| vanilla | dock | 0 | 0 | No matched captures |
| vanilla | deepwater | 0 | 0 | No matched captures |
| heavy | dock | 0 | 0 | No matched captures |
| heavy | deepwater | 0 | 0 | No matched captures |
| all7 | dock | 0 | 0 | No matched captures |
| all7 | deepwater | 0 | 0 | No matched captures |
