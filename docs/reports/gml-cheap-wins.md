# GML cheap wins: implementation and measurements

Date: 2026-11-23. Branch: `exp/gml-cheap-wins`.

## Continuation after initializing UndertaleModTool

The pinned submodule (including nested Underanalyzer) is now available. Validation on this
worktree succeeds with constrained parallelism:

| Command | Result |
|---|---|
| `dotnet build -c Release -m:1` | Passed; 0 warnings, 0 errors; 17.98 s |
| `dotnet test -c Release -m:1` | Passed; 797 passed, 19 skipped, 0 failed; 43 s |

The skipped tests are the repository's opt-in real-file tests. These checks prove the toolchain
works; they do not measure any of the proposed GML transformations.

## Result

The source audit is committed in [gml-cheap-wins-analysis.md](gml-cheap-wins-analysis.md). No runtime transformations were enabled or shipped in this pass. The proposed transformations cannot be called behavior-identical from the available evidence: collision-mask changes alter collision geometry, fish cadence changes AI/collision/RNG timing, and the high-call-count controller/event bodies mix visual work with gameplay or instance lifecycle. The local clean archive inspection found no wholly animation-only candidate among the inspected high-frequency events (`Fish_Step_2`, `Buoy_Step_0`, `BuoyAbstract_Step_0`, `Weed_Step_0`, `SceneItem_Step_2`, `SonarPanel_Step_1`). A default-on optimization would therefore be unjustified.

## Available baseline numbers

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

## Build and test status

`dotnet build -c Release` was attempted. It failed before project compilation because `external/UndertaleModTool/UndertaleModLib/UndertaleModLib.csproj` and the nested Underanalyzer project are absent from this checkout. The submodule pointer is `03c7048438347799e0717b6e57a9358d000472e0`; I did not edit or commit the submodule. A temporary link to a local backup at that commit was also tried and removed; its nested checkout/package setup was incomplete and compilation failed. `dotnet test` was not run because the solution cannot build in the current checkout.

The workspace volume reported 100% usage during this work (availability fluctuated from 28 KiB to 218 MiB). No benchmark matrix or crash-scene run was started: without a buildable changed artifact it would only repeat baseline data and would not measure this implementation.

## Open issues before implementation can be completed

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
