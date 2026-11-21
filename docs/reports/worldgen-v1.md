# Endless Ocean v1

Endless Ocean 0.2.0 extends the legacy `NewWorld` from the eastmost collidable mask in
the resolved archive. A deterministic three-chunk blend connects that mask to the generated
seafloor. The recipe reserves room already occupied by the input archive, then adds 488
2,048-pixel chunks. `Boat7` can travel to 400 pixels before the new room edge.

## Terrain and streaming

The generator samples a continuous seeded floor profile on a shared 512-pixel lattice, so
adjacent chunks use the same boundary height. Build-time sprites are assembled from the
input archive's CaveRockBig texture and Boat3 sprite. A slope cap follows each profile strip;
a collidable rock-textured underlay fills from the lower side of the cap to the room bottom.
The generated underlay and its mask cover the same space. This removes the floating-rock and
bottom-edge gaps seen in v0.

Beyond the transition, depth and distance select sandy shelf, rocky slope and deep trench
biomes. Chunks use the available CaveRock variants with seeded flips, scale and rotation,
plus native kelp, coral, plants and Light instances. Rare wreck silhouettes combine native
boat and rock parts during the build; a wreck attracts a rarer fish. Fish spawn in three
depth bands, after a live collision-mask check. The five-chunk camera window owns at most
15 streamed fish. Hooked, netted or aboard fish can finish their vanilla interaction after
their terrain chunk unloads.

The controller streams the current camera chunk and two neighbors on each side. Returning
west releases all generated instances. The only generated-world value written to disk is the
seed in `jamm_endless_ocean.ini`; transient fish IDs are skipped by both native save events.
Revisiting an unloaded chunk regenerates its initial fish and terrain from the seed.

## Validation

`dotnet build -c Release` completed with zero warnings. `dotnet test -c Release --no-build`
passed 574 tests, skipped 16 opt-in real-file tests, and had no failures. The six synthetic
worldgen checks passed: seeded determinism and seed variation, transition easing, full
underlay and seam coverage, depth-band and biome bounds, live collision veto, and the
15-fish streaming/release cap. The generated vanilla archive was byte-identical to its input:
both SHA-256 hashes were `dbb2e5c4b788358d0c6465d3005b2e9d46c4c540e423b064d7a5a3629286d2c1`.

The vanilla validation route ran on a disposable save. It crossed the stock Boat7 limit and
the generated boundary, checked terrain collision, open water, bottom coverage and the seam
at checkpoints, exercised both save paths, and returned west. At each terrain checkpoint,
all eight floor probes hit native CaveRock collision, all eight water probes stayed clear,
and all eight bottom probes hit the generated underlay. The save check found zero generated
fish IDs. Returning west left zero generated chunks and zero owned instances.

| Checkpoint | Boat x | Chunks | Owned instances | Room instances | Fish | Rock / water / bottom probes | Save IDs |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Stock boat limit crossed | 65,250 | 2 | 16 | 4,295 | 0 | — | — |
| Generated edge crossed | 66,170 | 3 | 24 | 4,303 | 0 | — | — |
| Transition | 66,959 | 3 | 24 | 4,308 | 0 | 8 / 8 / 8; seam 2 | — |
| Sandy shelf | 109,967 | 5 | 68 | 4,352 | 15 | 8 / 8 / 8 | — |
| Rocky slope | 116,111 | 5 | 69 | 4,154 | 15 | 8 / 8 / 8 | — |
| Deep trench | 177,551 | 5 | 72 | 4,159 | 15 | 8 / 8 / 8 | — |
| Wreck | 150,927 | 5 | 70 | 3,976 | 13 | 8 / 8 / 8 | — |
| Final chunk | 1,099,151 | 3 | 45 | 3,954 | 9 | 8 / 8 / 8 | — |
| Save check | 1,099,151 | 3 | 45 | 3,952 | 9 | — | 0 |
| Returned west | 61,527 | 0 | 0 | 3,754 | 0 | — | — |

The transition started at x = 65,935 on the August 16 legacy archive. The route used seed
271828. Solo screenshots and raw route data are in `~/jamm-night/worldgen-v1/showcase/` and
`~/jamm-night/runs/worldgen-v1-solo-debug-worldgen/`.

The first personal-profile run exposed stale callback script indexes: other mods shifted the
script table after the recipe compiled, leaving the controller's `script_execute` calls
pointing at the solo indexes. The final merged-layout pass now recompiles the controller
Step event from symbolic source, and a fresh personal build resolves its callbacks to the
correct final indexes (200 and 202). The rerun passed on the personal profile containing
Perf, HD Lite, Console, Smooth, The Silo and Weather Pack: transition and biome collision
probes passed, the five-chunk window held at most 15 fish, the save check found zero streamed
fish IDs, and the westbound return released all generated instances. It completed without a
CODE ERROR. Its CSV is `~/jamm-night/runs/worldgen-v1-personal-fixed-worldgen/`; the first
run before the callback fix is retained at `~/jamm-night/runs/worldgen-v1-personal-worldgen/`.

The rebuilt all-7 profile completed its 600-second-window run without a CODE ERROR. It crossed
the stock Boat7 limit and the generated boundary, passed transition and biome collision probes,
kept the five-chunk fish count at or below 15, exercised both saves with zero streamed fish IDs,
and returned west with no generated instances remaining.

| All-7 checkpoint | Boat x | Chunks | Owned instances | Room instances | Fish | Rock / water / bottom probes | Save IDs |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Stock boat limit crossed | 115,500 | 0 | 0 | 15,087 | 0 | — | — |
| Generated edge crossed | 145,772 | 3 | 24 | 15,111 | 0 | — | — |
| Transition | 146,349 | 3 | 24 | 15,116 | 0 | 8 / 8 / 8; seam 2 | — |
| Sandy shelf | 181,165 | 5 | 67 | 15,160 | 15 | 8 / 8 / 8 | — |
| Rocky slope | 195,501 | 5 | 69 | 14,781 | 13 | 8 / 8 / 8 | — |
| Deep trench | 256,941 | 5 | 73 | 14,786 | 15 | 8 / 8 / 8 | — |
| Wreck | 222,125 | 5 | 71 | 14,440 | 14 | 8 / 8 / 8 | — |
| Final chunk | 1,170,349 | 3 | 45 | 14,414 | 9 | 8 / 8 / 8 | — |
| Save check | 1,170,349 | 3 | 45 | 14,410 | 9 | — | 0 |
| Returned west | 140,038 | 0 | 0 | 14,370 | 0 | — | — |

The full route data is `~/jamm-night/runs/worldgen-v1-all7-final-worldgen/jamm_bench_worldgen.csv`.

## Showcase captures

The solo showcase capture set is in `~/jamm-night/worldgen-v1/showcase/`. It contains the
transition, each biome, a wreck, the surface, and an alternate-seed comparison. The three
panorama positions for each stop are also retained. These captures use the legacy solo
archive; they are appropriate for public preview use. They were captured in-game and show
the original-style terrain texture, native plants, fish and build-time wreck composition.

## Package

The example recipe is version 0.2.0 and uses generator version 2. Rebuilt the site package
with `python3 site/cgf-mods/make-packages.py`; `endless-ocean.zip` is 1,600 bytes with SHA-256
`bf9d70131aae9f0651f93c2280b7722671c9c215673b940f3c4540b2aa6171f0`. The generated site
package was not deployed.

## v2 plan

Keep the current seed-only save rule and the five-chunk streaming and 15-fish limits. The
world is still a finite 488-chunk extension (about 1,000,000 pixels); a larger ocean needs a
floating origin. A floating origin must shift the boat, Girl, lure, line, caught fish, camera,
sonar overlays, collision and path anchors together while retaining a logical chunk/travel
coordinate for saves and waypoints.

Audit remaining fixed-coordinate systems before extending travel: normal and deep sonar
noise and scan windows; quest goals, exploration progress and daily reset behavior; NavBrain
nodes and cross-chunk graph links; boat caps, fuel and progression gates; room-width consumers
such as Noctis and the surface-wave allocation. Generated fish currently respawn after their
chunk unloads, so persistent depletion or quest credit will need stable logical fish IDs and
seed-plus-progress save data. Profile memory, fish behavior and streaming cost on long runs
before increasing the chunk window or the per-chunk spawn count.
