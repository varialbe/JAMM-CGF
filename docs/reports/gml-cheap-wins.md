# GML cheap wins: implementation and measurements

Date: 2026-11-23. Branch: `exp/gml-cheap-wins`.

## Round 3 implementation and measurements

This section supersedes the earlier audit-only result below. Each experiment has its own
`PerfSettings` flag and `cgfmm profile perf <name> <flag> on|off` switch. `cache-hot` is now
default-on because it keeps event ordering and bypasses the `fish_lunge` script dispatch when
cooldown is its only work; behavior-changing experiments remain default-off. The archive
builder applies the selected transforms. The ModMaker `perf --only-cheap-wins` path
creates isolated local comparison archives with Fish Sleep and Decor Sleep held on in every
variant. Generated game archives and captures stay outside the repository.

| Flag | Build transform | Experimental behavior change / eligibility |
|---|---|---|
| `masks-rect` | Fish and selected visual-only Bubble decor sprites with precise masks use axis-aligned rectangle masks. | Collision outlines change; fish catches/wall avoidance can differ. All changed sprite names will be listed below. |
| `fish-lod` | Beyond 1.5 view widths, Fish state-AI dispatch runs every second End Step. Engine motion stays per-frame; alarms receive one extra decrement on dispatched AI steps. | Custom state counters, target selection, collision checks and interactions can be delayed. |
| `decor-anim-native` | Weed's existing offscreen guard compares squared distance against the same 5,000-pixel radius, avoiding a square root. | Equivalent except for floating-point boundary rounding. |
| `cache-hot` | Lure and camera coordinates are captured once per event, Sonar advanced/cave mode is captured before the `with (Fish)` walk, and the Fish End Step inlines the cooldown-only `fish_lunge` path. | The lunge return value is discarded; its cooldown decrement is preserved. The other snapshots remain event-local, so event ordering is unchanged. |
| `particles-fx` | Replaces direct standalone Bubble construction with a shared sprite-backed particle when a reusable particle system exists; skips assigned/modified creation calls. | Particle lifespan and ownership differ. PotEffect stays excluded because its Create event changes Witchy state. No eligible direct Bubble call was found in either measured pack. |

Mask census: `masks-rect` changed 243 fish sprite masks in all7 and 242 in pub5. The pub5 set is
the all7 set without `spr_jade`; no precise Bubble decor mask was eligible. The full changed-name
list is in [gml-cheap-wins-mask-census.txt](gml-cheap-wins-mask-census.txt).

Measurement setup: `~/jamm-bench/run.sh` (serialized by its `flock`), all7 and pub5, dock and
deepwater, two 12-second captures per flag and main baseline, seed 1337, same clean runner and
the same Fish Sleep + Decor Sleep baseline. No `CGF-Personal` install, personal save, or
`xdotool` was used. Each run directory is under
`~/.var/app/com.valvesoftware.Steam/data/JAMM-CGF/bench-runs/gml3-*`.

The values below are arithmetic means across the eight paired captures (all7/pub5 × dock/deepwater
× two repetitions). Loop p95 is the mean of each capture's p95. Times are milliseconds.

| Flag | FPS old → new | Loop mean old → new | Loop p95 old → new | Verdict |
|---|---:|---:|---:|---|
| `masks-rect` | 19.51 → 20.30 | 49.28 → 46.99 | 57.93 → 52.54 | Measured win; retain experimental. Rectangle masks change collision outlines and may alter catches/wall avoidance; sonar smoke recorded cleanly. |
| `fish-lod` | 19.51 → 22.23 | 49.28 → 42.79 | 57.93 → 51.22 | Largest measured win; retain experimental. Far-fish AI/collision/state cadence changes; sonar smoke recorded cleanly. |
| `decor-anim-native` | 19.51 → 19.70 | 49.28 → 48.89 | 57.93 → 54.64 | Small, inconclusive change; keep default-off and experimental. |
| `cache-hot` | 19.51 → 20.54 | 49.28 → 46.49 | 57.93 → 52.26 | Measured win; retain experimental. Sonar smoke recorded cleanly. |
| `particles-fx` | 19.51 → 20.22 | 49.28 → 47.14 | 57.93 → 53.09 | No eligible call in either pack; archives are byte-identical to baseline, so the apparent difference is run noise. |

Each measured win passed a 60-second `sonar` smoke (`gml3-smoke-masks-rect-sonar`,
`gml3-smoke-fish-lod-sonar`, `gml3-smoke-cache-hot-sonar`) with no Code Error or early exit. These
checks cover crashes; they do not establish gameplay parity. Fish LOD can delay state dispatch,
collision checks, target choice, and custom counters for far fish; rectangle masks change collision
outlines. No autoplay was run because the benchmark task forbids `xdotool`. One initial all7
cache-hot dock capture failed from a generated local-variable scope error; the transform was
corrected and that paired sample was re-recorded successfully. The failed capture is excluded from
the table.

Particle output is a true no-op for these packs: no eligible Bubble creation call was found, and
SHA-256 is identical to the baseline for both all7 (`a0647861e012045dae624ab08175aa0048f32ec99f4e81ef8e699758ae77967e`)
and pub5 (`fd4aaca4cbe297c5a43d48df761e055da30351593654ab96df84ccf967b3dd00`).

| Validation | Result |
|---|---|
| `dotnet build -m:1` | Passed; 0 warnings, 0 errors. |
| `dotnet build -c Release -m:1` | Passed; 0 warnings, 0 errors. |
| `dotnet test -c Release -m:1` | Passed; Core 670 passed/18 skipped, GUI 130 passed/1 skipped, 0 failed. Skips are opt-in real-file tests. |

The benchmark matrix used eight valid captures per flag, excluding the corrected-and-repeated
cache-hot sample's initial failed attempt. All generated archives, captures, and runner logs stayed
outside the repository.

## Historical build state before Round 3

The pinned submodule (including nested Underanalyzer) is now available. Validation on this
worktree succeeds with constrained parallelism:

| Command | Result |
|---|---|
| `dotnet build -c Release -m:1` | Passed; 0 warnings, 0 errors; 17.98 s |
| `dotnet test -c Release -m:1` | Passed; 797 passed, 19 skipped, 0 failed; 43 s |

The skipped tests are the repository's opt-in real-file tests. These checks prove the toolchain
works; they do not measure any of the proposed GML transformations.

## Historical audit-only result before Round 3

The source audit is committed in [gml-cheap-wins-analysis.md](gml-cheap-wins-analysis.md). No runtime transformations were enabled or shipped in this pass. The proposed transformations cannot be called behavior-identical from the available evidence: collision-mask changes alter collision geometry, fish cadence changes AI/collision/RNG timing, and the high-call-count controller/event bodies mix visual work with gameplay or instance lifecycle. The local clean archive inspection found no wholly animation-only candidate among the inspected high-frequency events (`Fish_Step_2`, `Buoy_Step_0`, `BuoyAbstract_Step_0`, `Weed_Step_0`, `SceneItem_Step_2`, `SonarPanel_Step_1`). A default-on optimization would therefore be unjustified.

## Historical profiler baseline numbers

The retained all-7 dock profiler capture is `ds-all7-prof-mA-dock/jamm_profile_dock.csv`: 104 profiled ticks, mean loop 141,355 µs. Its top measured entries were:

| Entry | µs/tick | Share |
|---|---:|---:|
| `Fish_Step_2` | 37,770 | 27% |
| `CaveRock_Step_0` | 16,640 | 12% |
| `SceneItem_Step_2` | 12,628 | 9% |
| `Weed_Step_0` | 4,585 | 3% |
| `SonarPanel_Step_1` | 1,948 | 1% |
| `Buoy_Step_0` | 1,026 | <1% |
| `BuoyAbstract_Step_0` | 1,007 | <1% |

This is historical baseline evidence only, not an old/new comparison. No optimized artifact was produced, so no claim of improvement or parity is made.

## Historical build and test status

`dotnet build -c Release` was attempted. It failed before project compilation because `external/UndertaleModTool/UndertaleModLib/UndertaleModLib.csproj` and the nested Underanalyzer project are absent from this checkout. The submodule pointer is `03c7048438347799e0717b6e57a9358d000472e0`; I did not edit or commit the submodule. A temporary link to a local backup at that commit was also tried and removed; its nested checkout/package setup was incomplete and compilation failed. `dotnet test` was not run because the solution cannot build in the current checkout.

The workspace volume reported 100% usage during this work (availability fluctuated from 28 KiB to 218 MiB). No benchmark matrix or crash-scene run was started: without a buildable changed artifact it would only repeat baseline data and would not measure this implementation.

## Historical open issues (resolved or superseded above)

- Initialize the pinned submodule, including its nested Underanalyzer dependency, and restore packages.
- Complete the exact sprite/mask census for vanilla, pub5, and all7, and trace each mask consumer.
- Identify an event whose full Step body is animation-only across the merged builds, or close that candidate with evidence.
- Implement only independently gated transforms with measured benefit and behavior parity. The LOD and collision-mask proposals remain experimental until traces and interaction tests prove parity.
- With a buildable toolchain, serialize the requested vanilla/pub5/all7 × dock/deepwater matrix (two old and two new runs) and crash scenes through `~/jamm-bench/run.sh`; report medians and raw run locations.

## Continuation outcome

No candidate transformation was retained in this continuation. This is not a measured rejection:
the existing dock/deepwater captures compare earlier Perf/Smooth configurations, and there is no
old/new artifact pair for masks, animation steps, particles, caching, or awake-fish LOD. Running
those captures again would only measure the existing artifacts. The newly available build and test
results above do not satisfy the requested local matrix or crash-scene checks.

The candidate audit still rules out turning these ideas on by default without further evidence:

| Candidate | Current decision |
|---|---|
| Collision masks | No sprite was proven behavior-neutral across its collision consumers; geometry changes can alter catches and interactions. |
| Animation-only Step removal | No full Step body among the inspected high-frequency candidates was animation-only. |
| Particles | No high-frequency effect was proven free of collision, ownership, or timing semantics. |
| Per-tick caches | Fish Grid already covers its separate neighbor-query experiment. SceneItem transform caching previously measured slower; other cache invalidation was not proven exact. |
| Awake-fish LOD | Skipping awake AI changes collision, RNG, and targeting timing. Existing Fish Sleep and Decor Sleep remain the relevant dormant policies. |
| Freeze trace | No zero-allocation event-ring implementation or hang-time dump path was added; runtime GML cannot dump its own ring after the event loop has stopped. |

Accordingly, this report does not claim that the requested transform implementation or old/new
benchmark matrix is complete. Implementing experimental transformations behind gates and measuring
them is still required before this task can close.

## Orchestrator confirmation (2026-11-23, 60 s interleaved, all7, 2 reps, null control)

Base vs. base2 (identical archives) differed by ~1% (one base deepwater outlier at 28.1 FPS excluded).

| Flag | Dock | Deepwater |
|---|---:|---:|
| fish-lod | +4.3% | +8.6% |
| masks-rect | +0.4% | +2.0% |
| cache-hot | +0.9% | −0.7% |

Only `fish-lod` is a clear win; it changes far-fish behaviour, so it stays default-off (player opt-in via
`cgfmm profile perf <name> fish-lod on`). masks-rect and cache-hot show no meaningful gain at 60 s captures.
