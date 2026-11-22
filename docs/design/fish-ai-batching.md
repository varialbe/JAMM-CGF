# Fish AI batching research and plan

Status: research complete; no player-facing AI batching implementation is approved. The nearest safe prototype is the existing off-by-default FishGrid rewrite for FishLarge's bounded neighbour-avoidance query. It is a spatial batching/control experiment, not shared per-species AI, and it does not justify a broader batching feature.

## Findings

### Work per fish is not one interchangeable AI calculation

Vanilla `gml_Object_Fish_Step_2` executes for every active fish in End Step. It dispatches a state-specific user event, then performs global behavior checks: trawler handling, lure distance and catalog reveal, night transition and return, cave-rock collision, state/velocity bookkeeping, and time scaling. These have per-instance inputs and side effects. The state dispatch includes idle, lunge, returning, hooked, fleeing, net, dead, bombed and other states; a species-level shared result cannot replace it.

The `FishLarge` and `FishHuge` families are especially unsuitable for batching. Their events use fish-neighbour avoidance, prey/predator searches, lure and hook checks, and state-sensitive flee/lunge behavior. The existing fish-grid report audited these lookups and retained engine order, tie handling, activation semantics, collision masks, and random-call order. Even a visually similar movement result can change a catch, lure response, sonar state, or another fish's target.

Some ordinary species do inherit identical pieces of generic swimming code. That is code reuse, not equivalent state: position, velocity, path, target, timers, visibility, nearby fish, random choices, and event side effects remain per instance. `fish_lunge()` is called on the hot path for every fish. Per-species caching of its result is unsafe because its inputs and effects include each fish's lure distance, state, gear-dependent radius, collision obstruction, and possible hook transition.

### Vanilla population and representative species

The local 2023 archive census of `NewWorld` reports these most frequent fish placements (room-start placements, before runtime spawns/thinning):

| Species | Placements | Observed behavior / batching decision |
| --- | ---: | --- |
| Chaw | 131 | **Not batched.** Calls `fish_lunge`, chooses nearest medium/large/huge fish, applies repulsion and home-range forces, and flees huge fish. |
| Snobfish | 94 | **Not batched.** Calls `fish_lunge`, seeks nearest medium/large fish, has a Nightfish/small-fish partner, random decisions, and state-dependent hunting/flee behavior. |
| Mustardfish | 72 | **Not batched in the relevance zone.** Medium fish use common base logic, but lure, flee, collision and individual state still apply. Candidate for off-screen cadence tests only if individually proven irrelevant. |
| Roundfin | 72 | **Not batched in the relevance zone.** Small-fish base behavior is shared, but position/path and lure checks are per instance. |
| Bombat | 43 | **Not batched.** Child of Cuddlefish; has per-fish lure and nearest-large-fish avoidance behavior. |
| Rana | 38 | **Not batched.** Small-fish logic and per-fish movement and lure state. |
| Skrimp | 35 | **Not batched.** Cuddlefish-family swimming still has individual position, target, and lure inputs. |
| Swooper | 34 | **Not batched.** Individual path/movement and lure/catch behavior. |
| Snuggler | 29 | **Not batched.** Individual state and player interaction; no proof of interchangeable movement. |
| Boom Puff | 29 | **Never batch.** Puff/bomb state and player-triggered explosion affect nearby fish and catches. |
| Sourfish | 24 | **Not batched in the relevance zone.** Medium-fish common behavior only supports a gated off-screen experiment. |
| Carver | 24 | **Not batched in the relevance zone.** Same restriction as other ordinary medium fish. |
| Cuddlefish | 17 | **Not batched in the relevance zone.** Its shared family event still performs individual lure checks and nearest large-fish avoidance. |
| Furrow | 17 | **Not batched in the relevance zone.** Individual path/position and state; off-screen test only after eligibility proof. |
| Sizzer | 16 | **Not batched in the relevance zone.** Individual state and fish interactions remain. |
| Curby | 15 | **Never batch.** Special partner/possession and nearest-fish logic. |
| Conpas | 15 | **Not batched in the relevance zone.** Individual position, fish avoidance and lure behavior. |
| Other species, including pack additions | — | **Default: not batched.** A modded object needs the same structural and runtime audit; unknown/custom events fail closed. |

The table is deliberately conservative. “Off-screen candidate” means an individual can potentially skip work only outside a relevance zone; it does not permit one fish to stand in for another. Fish that see a lure, are hooked, fleeing, hunting, in a net/boat, associated with a quest or otherwise interact with gameplay must continue to run their exact individual logic. Visibility alone is not sufficient: sonar and game scripts can observe off-camera fish.

The same `NewWorld` census against the supplied full archives/private packs retains the leading vanilla placements, so Chaw and Snobfish remain the most frequent species in each snapshot. Pack additions appearing near the top include Abyss's Blaw (87) and Emfin (84), Antiquity's Pimfish (47), and Verdant's Lokust (29); TGE/PWB's most frequent additions remain below the first 20 rows. These additions inherit standard size parents in several cases, but parent lineage alone does not establish safe batching: inspect each overridden user event and fish target/interaction before inclusion. The all-seven and heavy runtime profiler captures also show that modded fish/event volume increases `Fish_Step_2` cost; pack name or common ancestry is not an eligibility signal.

### Profiler evidence and Amdahl bound

Existing event-profile captures use identical instrumentation and report inclusive CPU time. `Fish_Step_2` accounted for 39% of the vanilla dock loop (56.1 ms/tick in the 2026-11-20 raw capture), 53% in one heavy-pack capture, and 56% in an all-seven-pack capture. The independent fish experiment measured vanilla at 921 fish / 14.99 ms fish-step per frame and all-seven at 2,199 fish / 22.56 ms, while the all-seven full loop averaged 61.58 ms. These runs differ in archive, profiler, and capture setup; treat them as bounds and hotspot evidence, not a single directly comparable benchmark.

If all `Fish_Step_2` work could disappear, Amdahl's ideal speedup is `1/(1-p)`: 1.64× at p=.39, 2.13× at p=.53, and 2.27× at p=.56. That is a ceiling, not a batching forecast. Current approaches can only touch a subset of that work. Fish-grid work has already shown that neighbour-query rewrites can add fixed overhead and fail to improve a heavy pack; the measured heavy dock pair was 171.64 ms/tick off versus 179.27 ms/tick on. A new batching proposal therefore needs per-species and whole-loop measurements and exact behavior traces before any gain claim.

The strongest current lever is Fish Sleep's relevance-zone scheduling, not cross-instance result sharing. The `exp/fish-sleep` task reports fish-step no-op bounds and defines a conservative zone around views, player, lure, boat, sonar and quest targets. This is a different feature: sleep skips an individually irrelevant fish; batching would let one fish's calculation stand in for another. Do not duplicate Fish Sleep's zone, wake, state preservation, save, population, or eligibility work here.

### Closest safe prototype already measured: FishGrid

The branch has an opt-in `[Perf] FishGrid=1`/benchmark `--fish-grid` experiment that rewrites bounded `fish_avoid_fish` queries for FishLarge while preserving instance traversal order. This exercises a batchable neighbour-query subproblem for one fish family without sharing AI state. In the fixed-clock measured pair, heavy-pack dock Fish End Step rose from 171.64 to 179.27 ms/tick and whole-loop time from 394.00 to 407.97 ms/tick; the grid cost 0.860 ms/tick. That is a negative result for the heavy pack. The all-seven dock pair fell from 452.08 to 400.31 ms/tick for Fish End Step and 946.60 to 843.00 ms/tick whole-loop, but only one pair was available and both fish time and unrelated loop time moved together, so attribution is inconclusive. Grid build cost was 1.780 ms/tick. The fish-state traces and explicit RNG counts matched byte-for-byte over the compared ticks (all-seven dock matched for the 120-tick performance pair; the longer trace report has a matching 609-tick prefix). See `docs/reports/fish-perf-grid.md` for the measurements and limits.

That prototype preserves state and RNG parity but does not include autoplay catch-rate/sonar CSV comparison. It proves that an exact-order, one-family spatial-query rewrite is possible; it does **not** prove that same-species AI results can be shared or that doing so is faster. No second species has a demonstrated safe shared-AI calculation. A new gameplay prototype is not warranted until code inspection identifies a genuinely pure common calculation; if Hydrogen proceeds with an off-screen individual cadence experiment, that belongs in Fish Sleep.

## Gameplay rule

| Fish / condition | Allowed? | Reason |
| --- | --- | --- |
| Any species with lure visibility/proximity, hooked/caught/reeling/net/boat state, flee/hunt/lunge transition, active quest/area role, or sonar relevance | **No** | Must preserve individual decisions, exact update order, catch rate, lure response, sonar and spawn behavior. |
| FishLarge/FishHuge and custom/predator/boss/special lineages (including Dragon, Noctis, Torby, Ghost, Abyssal, Kingfish, Shaku, Bagu, Curby) | **No** | Target selection, partner references, avoidance, random calls, hooks, or other cross-instance effects. |
| Ordinary FishSmall/FishMedium in view or relevance zone | **No** | A shared parent event does not make per-instance position, movement, timer, path, collision, or random state interchangeable. |
| Ordinary FishSmall/FishMedium outside the full relevance zone, individually idle and structurally standard | **Maybe, individually only** | Off-screen cadence/sleep can be explored if no alarm, target, cross-instance write, spawn/sonar/save dependency, and wake-up behavior is proven. This overlaps Fish Sleep and should be implemented there. |
| Off-screen fish whose AI is shown to be a pure function of identical shared inputs and which cannot affect any other entity | **Potential future batch candidate** | No vanilla or inspected pack species currently satisfies this proof. Require exact state and RNG parity before approval. |
| Unknown or modded species / changed code pattern | **No (fail closed)** | Cannot assume parent behavior covers custom events or new gameplay effects. |

## Research prototype and measurement gate

The branch must keep any new experiment test-only or disabled by default. The measured FishGrid prototype is the current one-species comparison; its code is already opt-in and its measurements are cited above. Do not claim the requested same-AI-per-species batching prototype exists. If Hydrogen finds a pure common calculation, add the smallest opt-in prototype for at most one FishSmall and one FishMedium candidate, skipping no lure-visible, hooked, fleeing, hunting, quest, sonar-relevant, or otherwise active fish. Preserve fish count and instance order, provide a per-fish fallback, and keep it off in normal builds.

Measure serially via `~/jamm-night/bin/run.sh`, after checking no game process or active game-run lock. Use the same legacy build, seed, scene, time controls, capture duration and profiler settings for baseline/prototype. Capture at least vanilla and a pack build; report `Fish_Step_2`, per-species event calls/time, fish count, whole-loop mean/p95, and prototype overhead. Run the autoplay catch-rate and sonar CSV probes against baseline and prototype on identical seeds. Require identical CSV outcome metrics within the established run-to-run noise, and identical fish-state/RNG traces for any claim of exact batching. If the probe cannot maintain this parity, stop and report the hypothesis falsified. On runner `EXITED`, follow the runner procedure: restart Steam and retry; remove only the generated `~/jamm-night/runs/<label>/game` copy after capture. Never point a writer at source archives.

## Relationship to adjacent work

### Fish Sleep (`origin/exp/fish-sleep`)

Fish Sleep is overlapping only for distant-fish scheduling. Its task proposes individual sleep outside a conservative relevance zone, wake handling, exemptions, population/save/sonar invariants, and a default-on Perf switch. Do not add a second distance zone, scheduler, or wake mechanism in this feature. If a measured gain comes from updating a fish less often, contribute the code and evidence to Fish Sleep instead. Keep this task focused on proven shared calculations among simultaneously active fish. The current Fish Sleep branch contains implementation work but no `docs/design/fish-sleep.md`; use `docs/tasks/fish-sleep.md` as the current design contract and reconcile against its eventual reviewed design before implementation.

### Native DLL offload (`exp/native-offload`)

At this research point, `exp/native-offload` contains `docs/tasks/native-offload.md` but no committed `docs/design/native-offload.md`. Its task calls for measuring GM 1.4 external-call/buffer support and proposes async pure-math offload. Keep GML responsible for eligibility, state transitions, instance order, RNG, collision and side effects. A native worker could be considered only after the native-offload task proves a safe buffer lifecycle and deterministic pure-data interface. Candidate payloads are independent numeric calculations already identified as pure; never send live instance references or let the DLL decide catches, targets, random choices or spawn behavior. Avoid building a second spatial index if FishGrid owns that data; compare one shared index/API and measure transfer overhead before choosing.

## Hydrogen implementation plan (after orchestrator review)

1. **Baseline the merged inputs.** Build or select vanilla, representative public pack, heavy pack and all-seven archives; decompile `Fish_Step_2`, `FishLarge`, the top-population species and changed pack fish with ModMaker/UndertaleModLib. Record per-species room/runtime counts, code lineage, RNG, lure, target, sonar and cross-instance effects. **Measure:** archive/object/code hashes, static counts, `--profile-scripts` per-species calls and time; fail closed on changed code patterns.
2. **Define the exact candidate set.** Establish a machine-checkable eligibility predicate and explicit per-fish fallback. Exclude all state and interactions in the gameplay rule; unknown parent/event patterns are rejected. Reconcile with Fish Sleep and FishGrid before code. **Measure:** synthetic eligibility matrix plus runtime decision counts; zero eligible fish in every interaction state.
3. **Build the isolated prototype.** Add the smallest test-only opt-in patch for at most one FishSmall and one FishMedium candidate (or one shared arithmetic term if no species is safe). Preserve normal behavior with the option off. **Measure:** build validation, trace hash, explicit RNG calls, and event counts; option-off archive should match unmodified output apart from benchmark instrumentation.
4. **Run serial performance and behavior comparisons.** Use the same archive/scene/seed and `--profile-scripts` before/after, then repeat in autoplay for catch-rate and sonar CSVs. Include fish count, species event time, `Fish_Step_2`, whole-loop mean/p95, and noise from repeated baselines. **Measure:** only accept gains exceeding observed baseline variance with exact parity; otherwise delete/revert the prototype and document the negative result.
5. **Choose GML or native boundary.** Keep instance selection, update order and gameplay decisions in GML. First implement proven pattern rewrites as a guarded Perf patch with diagnostic decisions; consider native only for pure numeric loops if the offload research proves end-to-end benefit after serialization costs. **Measure:** GML-only versus native boundary including copy-in/copy-out, DLL overhead, fallback path, and identical trace/catch/sonar results.
6. **Review compatibility and ship decision.** Test vanilla, public/heavy/all-seven packs and changed-pattern negative cases; keep the feature off by default until orchestrator review and player-machine measurements. **Measure:** zero warnings, tests, repeated profiler pairs, autoplay parity, and no save/population regressions. A failed or noisy result means no player-facing feature.

## Source notes

- Legacy census/decompilation: `~/cgf-files/legacy/2023-08-16_manifest-9039707068725406600/data.win`; ModMaker `census` and `dump` commands. No archive or extracted game data is included in the repository.
- Profile rows: `~/jamm-night/runs/vanilla-prof-dock/jamm_profile_dock.csv`, `heavy-prof-scripts-dock/jamm_profile_dock.csv`, and `fish-grid-baseline-all7-dock/jamm_profile_dock.csv`; aggregate context in `docs/reports/overnight-2026-11-19.md`, `docs/reports/fish-perf-grid.md`, and `origin/exp/fish-sleep:docs/reports/fish-experiments-2026-11-21.md`.
- `Fish_Step_2` decompilation calls state handlers, `fish_check_trawler`, lure distance/reveal and boundary checks for every fish. `Chaw_Other_10` and `Snobfish_Other_10` are examples of species-specific nearest-target, random and lure behavior. `Cuddlefish_Other_10` demonstrates that a repeated family event still uses each instance's position and fish targets.
- The measured FishGrid prototype was run serially in the existing fish-grid experiment and is documented in `docs/reports/fish-perf-grid.md`. At this research session's inspection time, the shared runner was occupied, so no new autoplay catch-rate/sonar comparison was launched. The state/RNG trace parity claim is limited to the exact completed captures listed in that report.
