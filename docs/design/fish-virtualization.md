# Fish virtualisation research and implementation plan

Status: research complete; a bench-only Garder record/materialise prototype is available with its feature default OFF. The prototype is not production-ready and does not claim gameplay parity.

## Goal and decision

Keep only fish that can affect the camera, lure, sonar, or active gameplay as GameMaker instances. Store eligible distant fish as records grouped by zone/chunk, advance them with coarse deterministic movement, and materialise them before any interaction can observe them. The target is 100–300 relevant instances instead of roughly 2,000. A round-robin far-fish work queue with a per-tick cap (initial hypothesis: 8 ms) is a separate layer; near/relevant fish always receive the normal full step.

Do not begin by virtualising arbitrary `Fish` descendants. A missing instance changes GameMaker's active-instance set, which affects much more than rendering and AI:

- `instance_number`, `instance_find`, `instance_nearest`, `instance_furthest`, `instance_place`, `with (Fish)`, `random_fish`, and selection order observe a different population.
- `FishThinner`, per-species spawners, `fish_respawn`, and `CatalogData` use active instance counts for population and spawn decisions.
- Sonar reads per-instance `s1..s5` and `drawn`; lure/rod code reads lunge modifiers; catch, quest, collection, save/load, bomb, boat, and CatSwim code hold instance IDs or read state.
- A mod can keep its own fish IDs or expect a custom Step/Draw/Alarm/Other event to run. A generic snapshot cannot safely infer all instance variables or external references at runtime.

The safe first target is ordinary, non-interacting fish that pass an explicit capability audit. Bosses, quest/singleton fish, fish with custom cross-instance links, held/caught fish, and unknown modded species remain real. Keep a stable virtual population API so population consumers can count records plus instances without changing spawn logic invisibly.

## Existing streaming template and differences

Endless Ocean already owns a chunk lifecycle in `src/Jamm.Core/Builtins/Worldgen/`:

1. `jamm_chunk_stream` keeps a bounded range of chunks, creates missing chunks, and releases chunks outside the range.
2. `jmk_endless_ocean_load` deterministically derives fish species and coordinates from the world seed/chunk layout, creates up to three fish for a content chunk, initializes sonar and lure fields, assigns catalog/path fields, and records their IDs in the chunk's `ds_list`.
3. `jmk_endless_ocean_release` delegates fish to `jmk_endless_ocean_dispose`. That function preserves caught/hooked/held instances in `ocean_catch`, then destroys other generated fish. A revisit regenerates those ordinary fish from the chunk seed; it does not preserve their live state.

This is a good lifecycle/map pattern, not a drop-in fish virtualiser. It is bounded to Endless Ocean's generated population; ordinary world fish have live vanilla population, save, sonar, catch, and mod semantics. The current chunk code also depends on species `Create` chains and performs explicit post-create initialization (`jmk_endless_ocean_sonar`, `base_lunge_mod`, `base_lunge_offset`, `catalog_index`). `docs/reports/ocean-fish-parity.md` records the first-frame sonar race (`s1..s5`), missing `base_lunge_mod`, `FishThinner` reactivation, and unsafe `Torby.mate` cases. Those are mandatory lifecycle tests for any materialisation path.

The shared `jamm_fish_sonar_init` work on `fix/fish-sonar` should be used as the single sonar initializer; do not copy its field calculations into a second implementation. This branch does not yet contain that shared helper (the branch is not an ancestor of this worktree), so merge/reconcile it before wiring the prototype. Both initial spawn and wake must call the same helper.

## Record contract

Use a struct/array record in a zone-owned `ds_list` or `ds_grid`, indexed by a stable record ID. Avoid a single global list scan. Store the source object asset (not an assumed object index), the record ID, zone/chunk, position, last simulation tick, RNG seed/state, and a schema version.

The minimum audited fields are conditional on species and current state. A type-specific serializer must be generated from the object hierarchy and known reads/writes; there is no safe universal fixed list.

| Field family | Required data / examples | Restore rule |
|---|---|---|
| Identity and pose | species asset, `x`, `y`, `xprevious`, `yprevious`, `direction`, `speed`, `hspeed`, `vspeed`, `depth`, `visible`, `sprite_index`, `image_index`, `image_speed`, image scales/angle/alpha | Set after `instance_create`; preserve animation phase and movement continuity. |
| Base Fish lifecycle | `state`, `life`, `respawn_time`, `onBoat`, `stuck`, `stick_*`, `isDragging`, `exotic`, `catalog_index`, `display_size`, `size`, `hasGivenExperience`, `hasBeenSold` where present | Keep caught, sold, quest, held, or state-4/15 fish materialised until terminal vanilla logic finishes. |
| Species AI | `path_position`, path target/phase/speed fields, stamina, lunge/flee/hunt timers and targets, schooling/mate/parent IDs, alarms and custom variables | Only serialize if each reference is rewritten to a stable record ID or the fish is excluded. Never restore a stale instance ID. |
| Vanilla consumers | `s1..s5`, `drawn`, `base_lunge_mod`, `base_lunge_offset`, `object_id`, `name`, `value`, `light`/`light2` handles | Recompute derived values at materialisation; recreate owned light instances; do not serialize engine instance handles. |
| Determinism | per-record RNG seed/state, simulation tick and coarse-motion accumulator | No global `random()` consumption while virtual. On wake, ensure a fish makes the same choices as the declared coarse model, not a different global random stream. |

GameMaker 1.4 does not provide a reliable generic “enumerate every dynamic instance variable” snapshot. The implementation should use a conservative allowlist for the pilot species, explicit serializer/versioning, and build-time rejection when an eligible object's own or inherited code references variables outside that schema. This rejects unknown species rather than silently losing state.

## Who observes fish and required virtualization seam

| Consumer | Current assumption | Required adaptation before broad rollout |
|---|---|---|
| `FishThinner` and `random_fish(Fish)` | Active instance count/order is the population; may deactivate a random distant fish. | Decide whether virtual records count toward thinning and preserve selection distribution. Never let thinning and virtualization double-remove population. |
| Sonar (`SonarPanel_Step_1/Draw_0`, advanced sonar) | Active `with (Fish)` updates and draws fields on every sonar-relevant fish. | Sonar radius is always inside the materialisation zone; wake and run sonar initialization before the next Draw. |
| Spawners / `fish_respawn` / `FishThinner_Step_0` | `instance_number(species)` and `CatalogData.live/pop` represent living population. | Replace count reads with a single `jamm_fish_population_count(species)` (real + virtual + reserved spawns); make every spawn/death/catch transition update it exactly once. |
| SaveManager | Enumerates real Fish and writes per-instance `[Fishes]` state; restores through species Create + load callbacks. | Serialize virtual records in a versioned save section; migrate old saves; never save the same fish both as a record and an instance. |
| CatalogData / Fishalog / quests | Reads `with (Fish)`, catalog fields, counts, state and caught/quest flags. | Keep counters sourced from logical population; materialise quest targets; ensure all fish queried for interaction are visible as instances. |
| Lure, Boat, CatSwim, bomb, catch/release | Stores IDs or directly reads fish state; IDs must remain valid through an interaction. | Freeze eligibility as soon as an interaction starts; keep the actual instance through the complete vanilla transaction. |
| FishGrid / fish targeting / collision | Scans active fish and relies on engine ordering, collision masks, target IDs and random selection. | Include only when each helper has a record-aware equivalent or the query radius is fully materialised. Maintain ordering/tie behavior where gameplay observes it. |
| Mods | Arbitrary `with`, instance scans, parent events, alarms, or IDs can bypass JAMM's known hooks. | Default to unsupported for unknown/unsafe species. Add per-pack compatibility declarations and diagnostics; no blanket promise for a modded build. |

## Seven-pack compatibility audit (local VPS pack set)

Audit scope: patch source in `~/jamm-night/n21/{pub5-main,igr,ul}/patches`; only names/findings are recorded here. Pack archives, patch JSON, decompilations, and game data remain local and are not committed. “Not eligible” means the pack may still be installed, but its fish stay as ordinary instances until a more specific audit clears them.

| Pack | Source evidence relevant to fish lifecycle | Pilot compatibility |
|---|---|---|
| **Antiquity** | Adds many fish-specific Create/Other handlers; changes `FishThinner_Step_0`; includes `HellFishes_Other_13` and custom Demonfish behavior. | Not eligible initially. Population/thinning and custom state/Other events need per-species review. |
| **Abyss** | Adds fish Create code including Cuddlefish, Catfish, Cubblefish, Fishopod, Howfish and others; changes Fishalog/catalog setup. | Not eligible initially. New species and catalog/population integration. |
| **Cave Expansion** | Adds species scripts and fish Create/Other code (including Elder Mustardfish and several custom species); cave-local behavior and spawns. | Not eligible initially. Area-local spawners and species AI/collision need record-aware handling. |
| **Colour Expansion** | Changes `FishThinner_Step_0`, `fish_was_just_bombed`, and `fish_lunge_d`; its patch is not purely visual at runtime. | Not eligible initially. Global thinning and bomb/lunge semantics intersect virtualisation. |
| **Verdant** | Adds fish species/paths and custom movement parameters; adds fish to the catalog and changes boat/population assets. | Not eligible initially. Custom path/AI and population parity need explicit serializers. |
| **TGE** | Replaces shared `Fish_Create_0`, `Fish_Draw_0`, `Fish_Step_2`, `Fish_Alarm_4`, and `FishThinner_Step_0`; adds common fish draw/update helpers and custom fish events. | Incompatible with generic pilot. Changes base lifecycle globally; requires an adapter and its own parity run. |
| **PWB** | Replaces shared `Fish_Create_0`, `Fish_Draw_0`, `Fish_Step_2`, `Fish_Alarm_4`, and `FishThinner_Step_0`; adds/changes common fish helpers. | Incompatible with generic pilot. Changes base lifecycle globally; requires an adapter and its own parity run. |

This is a compatibility gate, not a statement that the seven packs cannot ever work. The vanilla + single-pack prototype should use **Antiquity** only if the vanilla pilot species stays unchanged by its patch; otherwise use unmodified vanilla first and add Antiquity as the first pack compatibility test. Do not infer safety from a pack merely adding fish: its records still interact with vanilla population scans.

## Prototype boundary and evidence

The first prototype should be test-only and opt-in (`jamm_bench.ini [Bench] FishVirtualization=1`, default `0`); it must not change a normal JAMM build. Use one ordinary vanilla species with no custom Step/Draw/Alarm/Other event, no owned light, and no cross-fish references. Snapshot only state that has been audited for that exact object. Leave every other species materialised. The prototype transitions a distant eligible fish to a record, advances its pose coarsely, and restores it when it approaches the camera/lure/sonar zone. Caught, hooked, held, quest, boss, alarmed, and unsupported fish are excluded.

Record transition sketch (GameMaker 1.4 pseudocode; field list is intentionally species-specific):

```gml
// On a slow round-robin pass. Eligibility rejects interactions and unknown state.
with (jamm_virtual_candidate)
{
    var rec = ds_map_create();
    rec[? "species"] = object_index;
    rec[? "x"] = x; rec[? "y"] = y;
    rec[? "xprevious"] = xprevious; rec[? "yprevious"] = yprevious;
    rec[? "direction"] = direction; rec[? "speed"] = speed;
    rec[? "image_index"] = image_index; rec[? "image_speed"] = image_speed;
    rec[? "state"] = state; rec[? "life"] = life;
    rec[? "path_position"] = path_position;
    rec[? "seed"] = jamm_virtual_seed;
    ds_list_add(jamm_virtual_zone_records, rec);
    instance_destroy();
}

// On entering the relevance radius, after removing the record from its zone list.
var fish = instance_create(rec[? "x"], rec[? "y"], rec[? "species"]);
with (fish)
{
    xprevious = rec[? "xprevious"]; yprevious = rec[? "yprevious"];
    direction = rec[? "direction"]; speed = rec[? "speed"];
    image_index = rec[? "image_index"]; image_speed = rec[? "image_speed"];
    state = rec[? "state"]; life = rec[? "life"];
    path_position = rec[? "path_position"];
    jamm_fish_sonar_init(id);
    jamm_fish_restore_derived_fields(id);
}
```

This is not safe to enable as written: `instance_create` reruns Create and may consume global randomness or create related instances; the sketch omits collision/path/AI variables, alarms, saves, population accounting, and external IDs. The prototype's first acceptance criterion is to demonstrate a round-trip for one audited species with no missing reads and no global RNG drift, not to turn on every fish.

Performance evidence from the earlier fish-isolation experiment is an upper bound, not a virtualisation result:

| Dock build | Mode | Avg FPS | Frame p95 | Loop mean | Fish-step mean | Active Fish mean |
|---|---:|---:|---:|---:|---:|---:|
| Vanilla | full | 46.54 | 24.52 ms | 20.86 ms | 14.99 ms | 921 |
| Vanilla | no Step | 163.60 | 7.97 ms | 4.57 ms | 0.00 ms | 980 |
| Public-five build | full | 10.20 | 134.35 ms | 95.29 ms | 48.57 ms | 2,185 |
| Public-five build | no Step | 37.54 | 32.85 ms | 24.05 ms | 0.00 ms | 2,161 |
| All-seven build | full | 15.67 | 76.65 ms | 61.58 ms | 22.56 ms | 2,199 |
| All-seven build | no Step | 59.42 | 17.63 ms | 14.69 ms | 0.00 ms | 2,198 |

The source is `docs/reports/fish-experiments-2026-11-21.md` in the Fish Sleep worktree. “Public-five” is a five-pack merged build, not a single-pack test. These measurements show that fish Step work is a substantial hotspot, but do not demonstrate that record creation, per-zone iteration, or wake-up is cheaper or behavior-equivalent. Record CPU, instance count, and transition cost in the prototype CSV before claiming a gain.

Required serial experiment: vanilla and vanilla + one selected pack, each OFF then ON, same archive, scene, save seed, runner, duration, and warmup; run only through `~/jamm-night/bin/run.sh` (shared `game-run.lock`). If outcome is `EXITED`, run `~/jamm-night/bin/steam-restart.sh` and retry once. Remove every created `~/jamm-night/runs/<label>/game` directory after collecting CSV/log/screenshot. Compare `fish_count`, active eligible count, record count, per-frame and `fish_step_us`, transitions, and errors. No conclusion on parity from frame time alone.

## Parity contract

- Logical fish population by species/area is unchanged. A virtual fish counts exactly once for `FishThinner`, spawners, `CatalogData`, save/load, and quests.
- The same seeded spawn decisions and global RNG sequence occur. Virtual updates use per-record RNG; they do not call shared `random()` or alter order-dependent vanilla queries.
- Materialisation happens before entering the union of camera view + margin, lure reach, normal/advanced sonar radius, boat/cat, and quest/area interaction radius. Use separate enter/exit radii to prevent thrashing.
- A materialised instance receives vanilla Create once and then an explicit audited restore. It has sonar fields and lure modifiers initialized before any Draw/Step consumer. Wake positions must be collision/path safe.
- No fish is virtual while hooked, being reeled, held, on the boat/net, caught/releasing, a quest/singleton/boss, carrying an alarm, or referenced by another live instance.
- `SaveManager` writes and restores exactly one logical fish record. Crash/reload and old-save migration are required before release.
- Catch-rate and sonar parity are compared with autoplay CSVs over identical seeds/scenes; investigate differences rather than using a tolerance to mask systematic drift.

## Relationship to adjacent work and order

| Work | Overlap | Recommended order |
|---|---|---|
| Fish Sleep (`exp/fish-sleep`) | Keeps instance IDs and variables stable while skipping distant fish AI. Uses zones, exemptions, hysteresis, and catch-up, but does not remove instance overhead or change instance enumeration. | First compatibility baseline. Reuse the relevance-zone predicate and exemption audit. Virtualisation is a later optimization after Sleep behavior and save semantics are stable. |
| Fish AI batching (`exp/fish-ai-batching`) | Can reduce per-fish computation for similar schools and distant LOD; does not remove instances. | Combine after virtual records work: batch coarse updates per virtual zone/species; never batch nearby lure/flee/hunt behavior. |
| Native offload (`exp/native-offload`) | Could compute pure coarse movement/zone decisions for record arrays off-thread. Does not solve GML instance consumers or serialization. | Last. First define a packed, deterministic record buffer and prove GML fallback. Keep all instance creation/destruction and gameplay state transitions on the VM thread. |

Therefore: relevance/exemption audit → instance-preserving Fish Sleep baseline → bounded record/wake pilot → population/save/query adapters → pack compatibility → optional batched coarse simulation → optional native math offload. Avoid implementing Fish Sleep's scheduler twice.

## Implementation plan with measurement gates

1. **Inventory and schema generator.** Build an asset-level report for fish hierarchy events, cross-instance variable reads, alarms, owned instances, and instance scans; compare vanilla plus each of the seven local packs. Gate: zero unclassified reads/events for the pilot species; generated allowlist and exclusions reviewed.
2. **Opt-in record round-trip.** Add test-only feature flag default OFF and a one-species record store/materialise path. Gate: 10,000 snapshot/restore cycles with exact audited fields, no engine handles in records, no RNG state delta, no duplicate/lost IDs; benchmark transition micro-cost against `instance_create` + Destroy.
3. **Spatial relevance and hysteresis.** Union camera, lure, sonar, boat/cat, quest/area regions and wake margin. Gate: no visible edge pop during a controlled camera sweep; active fish stay below target only in empty, distant water; record scans bounded by visited zones.
4. **Coarse deterministic movement + time budget.** Per-zone update queue, persistent round-robin cursor, cap far-fish work initially at 8 ms/tick; near fish are never budget-skipped. Gate: report p50/p95/max `virtual_update_us`, carry-over backlog, and frame-loop p95 across vanilla and one pack at equal seeds; backlog must drain before any fish enters the wake band.
5. **Population/catch/save integration.** Introduce logical-population API and versioned save/load records, then adapt sonar/catch/spawner/catalog/quest call sites. Gate: autoplay catch-rate and sonar CSVs match OFF control across 2 seeds × 10 minutes; reload preserves positions/state; counts by species and area match.
6. **Broaden compatibility one pack at a time.** Add declarations/adapters for Abyss, Antiquity, Colour, Cave Expansion, Verdant, then separately TGE and PWB. Gate: each pack passes the same parity suite; unknown custom event/reference remains excluded with a clear reason. Never claim blanket compatibility from the initial pilot.
7. **Tune and decide.** Measure 100/300/500/1,000/2,000 logical population and camera/lure traversal scenes; compare full, Fish Sleep, virtualization, and combined modes. Gate: demonstrate improvement in loop p95 and frame-time p95 without increased catch/sonar variance; otherwise keep off or stop.
8. **Optional AI batching/native offload.** Batch only pure coarse record updates, then evaluate native compute separately. Gate: per-step determinism/RNG parity and fallback benchmark; no native dependency for correctness.

## Open implementation questions

- Which exact vanilla object is both numerous in the benchmark scene and has no custom event, external ID references, or owned light? Resolve from vanilla profiler traces before selecting the pilot.
- What is the real target set and source revision for “seven packs”? This document uses the local TGE, PWB, Abyss, Antiquity, Colour, Verdant, and Cave Expansion patch set; re-audit any other load order/version.
- Can logical population queries be centralized without rewriting every mod's direct `instance_number` / `with (Fish)` calls? If not, supported packs must be explicitly limited and virtual fish may need proxy instances for some queries.
- What is the authoritative fish RNG model? If fish consume global RNG in parent or species events, the pilot must use instance-preserving sleep or exclude that species until a deterministic per-fish RNG shim is proven.

## Orchestrator review (Phase 2, 2026-11-22 02:25)
Plan approved as the structural lever for far fish. Condition: the requested prototype is still missing, so steps 1
and 2 run now (Codex Luna, this branch) before any Hydrogen implementation:
- Step 1: generated inventory for vanilla + the seven local packs; pick the pilot species from vanilla profiler data.
- Step 2: opt-in (default OFF) one-species record store/materialise path with the 10,000-cycle round-trip test and a
  measured transition cost vs `instance_create`/`instance_destroy`, plus one serial in-game measurement (vanilla) of
  fish_step/loop with N pilot fish virtualised vs OFF.
Answer the open questions on RNG model and population queries with evidence. Steps 3–8 go to Hydrogen after Fish Sleep,
combined with its relevance zone (one shared zone implementation, not two).

## Phase 2 implementation and measurements (2026-11-22)

### Step 1: generated inventory and pilot

The generated vanilla + seven-pack inventory is in [`docs/reports/fish-virtualization-inventory.md`](../reports/fish-virtualization-inventory.md). The local source revisions are pinned there by patch-manifest SHA-256 prefixes. It reports 261 fish-related vanilla code entries; the fish-code scan found 21 entries with instance/population-query signatures, 4 entries naming Alarm events, 21 calling `instance_create`, and 29 with explicit random calls. These are syntactic counts, not proof of complete data flow.

Garder is the pilot candidate: it is a leaf under `FishSmallTropic → FishSmall → Fish`, and its own vanilla event list contains only `Create`. The vanilla dock profiler reports `Fish_Step_2` at 56,109 µs/tick, or 39% of the profiled loop (270 captured ticks). The event is inherited, so the capture does not establish Garder's per-species share. The runtime prototype limits itself to unmodified vanilla, far-from-view/lure Garder fish, and an explicit primitive record schema.

**RNG evidence:** vanilla has global random calls across 29 fish-related code entries; no per-fish RNG state was found in the inspected hierarchy. `instance_create` reruns Create and can consume that shared stream. The prototype's materialisation path saves `random_get_seed()` and restores it after Create. It does not claim deterministic per-fish simulation; virtual updates do not run in this prototype.

**Population-query evidence:** vanilla and pack patch inventories contain direct instance/population queries; the seven manifests have between 1 and 19 fish-related entries matching the supported query signatures. There is no central logical-population seam in those GML call sites. Virtual records therefore remain invisible to normal gameplay queries, and all packs remain outside this prototype's support claim.

### Step 2: opt-in record/materialise prototype

`jamm-modmaker bench` accepts `--fish-virtualization-count <n>` (default `0`, OFF); the runtime override is `[Bench] FishVirtualization=0|1` with count defaulting to the baked value. The path uses a DS list containing the source object asset and primitive pose/lifecycle values. Materialisation restores those values after `instance_create`, then restores the global RNG seed. The CSV records requested/actual virtual counts, virtualise and materialise time, and a comparable create/destroy pass. A 10,000-cycle unit test checks exact equality for all fields in the declared record schema and uniqueness of record IDs; records contain only integers/doubles, no engine handles.

The GML transition is benchmark instrumentation only. It does not implement zone streaming, coarse movement, full per-species state capture, population accounting, save/load, or gameplay wake conditions. Do not enable it in a normal game build or treat its round-trip as parity approval.

**Serial vanilla measurement:** pending completion of the shared-lock `run.sh` OFF/ON pair. Results, including the actual virtualized count and `fish_step_us` / `loop_us` deltas, are recorded here after both CSVs are collected.
