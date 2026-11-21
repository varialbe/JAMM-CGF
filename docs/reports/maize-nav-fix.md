# Maize patrol crash in the all-seven build

## Cause

`NewWorld` is the only room with Maize or placed navigation objects in the August 16, 2023 base, the seven source mods, and both old merges. Each `NavNode` initializes its edge list in Create. The single `NavBrain` builds edges in Room Start, after placed instances have run Create. It connects pairs of nodes with a clear line through `CaveRock` masks. Across these archives, `NewWorld` has no room creation code and none of the placed navigation objects or Maize has instance creation code. Every `NavEdge` is created at runtime.

| Archive | Placed nodes | Placement and order compared with vanilla |
| --- | ---: | --- |
| Vanilla | 37 | All nodes precede NavBrain and Maize. |
| TGE | 37 | Same navigation placements and relative order. |
| Antiquity | 37 | Same navigation placements and relative order. Adds rock masks over Verdant's patrol area. |
| PWB | 37 | Same navigation placements and relative order. |
| Abyss | 37 | Same navigation placements and relative order. |
| Colour | 71 | Keeps the vanilla sequence and places 34 additional nodes after Maize. |
| Verdant | 37 | Replaces all 37 node coordinates and moves Maize. All nodes still precede NavBrain and Maize. |
| Cave | 37 | Moves two vanilla nodes and retains the relative order. Adds a rock mask over Verdant's patrol area. |
| Old all-seven, with and without repack | 73 | Cave's two moved nodes precede NavBrain, then come Verdant's 37 nodes and Maize, then Colour's 34 nodes. |

ModMaker `names` and `dump` showed identical navigation and Maize bodies in all ten archives, including the unpacked comparison. `Fish_Step_2` also matches vanilla in the failing archive. ModMaker `rooms` and an ordered UTML placement scan established the room footprint. `cgfmm diff --show NewWorld` confirmed the changes to the placements and transforms. Room Start still runs after the node lists are initialized, and every Verdant node still precedes Maize's Create in the old merge.

Verdant places Maize at `(9104, 14864)` and its nearest node at `(53760, 13328)`. That node is clear in Verdant alone. The position-keyed room merge retains Verdant's node while combining it with Cave's `CaveRockBig` at `(54208, 13792)` and Antiquity's at `(53632, 12752)` and `(52912, 12496)`. The three transformed masks each cover the node. An offline collision-mask scan estimated three distinct connections from the vanilla starting node, 36 from the Verdant starting node in its source archive, and zero from Verdant's starting node in the old all-seven room. The two moved Cave nodes also survive Verdant's removal of their original positions because room instances are keyed by object and coordinates. The overlap already exists after merging the room instances and before texture repacking. The seven-mod profile contains no built-in performance mod.

At an isolated node, the vanilla selector returns `-1`. Maize leaves that value in `target_node` and the next patrol update uses it as an instance reference. In the legacy runner `-1` refers to self, which accounts for the error naming `Maize.edges`. Deep sonar was open in the reported crash. The collision failure comes from the navigation graph that Room Start builds.

The VPS reproduced the failure with the reported `all7-repack.win`. The runner counted 71 nodes with zero edges and two nodes with two edges each. Maize's starting node at `(53760, 13328)` had zero edges. The first patrol update changed its target from instance `105408` to `-1`. The next produced `CODE ERROR` with `Maize.edges` unset in `nav_get_random_connected_node`, called by `Maize_Other_10` from `Fish_Step_2`. The game log and error capture are under `~/jamm-night/runs/maize-nav-before-maize/`.

## Fix

`[CGF-NAV-001]` installs a repair in the merged room's NavBrain event after native graph creation. It keeps the merged placements, their creation code and the existing edges. If Maize starts without a usable target, it selects a connected node in the existing graph. If the graph offers no branching route, the repair uses original node coordinates from the selected base and restores the subset that clear the merged room's collision masks. It connects only clear pairs, then retargets Maize to a recovered node. It moves Maize onto that node if the approach from the placed fish crosses a rock.

The modded patrol selector also skips unavailable edges and endpoints. It backtracks to a valid previous node at a dead end or keeps the current valid node. Maize re-picks the nearest node before dereferencing a missing target and waits when no node is active. The existing candidate order and random selection are retained while the graph is healthy. The compatibility revision invalidates existing managed profile signatures. The empty-profile build uses the original byte-copy path.

The regression test builds an isolated one-node room from a synthetic three-node base and checks that the compiled room-start recovery and both patrol guards survive archive verification. It also checks preservation of the placed node's creation code and a byte-identical vanilla build.

## Verification

The real vanilla CLI build passed `cmp` against the 44,572,166-byte legacy base. Both archives have SHA-256 `dbb2e5c4b788358d0c6465d3005b2e9d46c4c540e423b064d7a5a3629286d2c1`.

`dotnet build -c Release` finished with zero warnings. `dotnet test` passed 506 core tests and 127 GUI tests. The runner skipped 17 opt-in tests. The staged site catalog test had carried forward the six-package catalog and a 1 MiB test cap. Its expectations now cover all nine staged packages and use the production download limit.

`cgfmm apply --profile maize-nav-fix --yes` built the seven mods in order TGE, Antiquity, PWB, Abyss, Colour, Verdant and Cave. The isolated profile enabled repacking and verified 120,294,230 bytes, with 119 texture pages after repacking from 417. The archive verifier reported six source texture rectangles extending outside their pages, whose existing sampling geometry the repacker preserved. The built archive and local evidence are under `~/jamm-night/maize-nav.d9LozN/`.

The Maize benchmark uses the placed fish and its initialized target. It activates the fish, starts it at that target to exercise the patrol immediately, and opens the advanced sonar centered on Maize. It logs the number of edges at each node at room start, then records the patrol calls, target changes and sonar state. The standard dock and deepwater scenes use the existing benchmark tool. All runs use `~/jamm-night/bin/run.sh` under the shared game lock.
