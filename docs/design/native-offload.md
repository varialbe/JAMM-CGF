# Native worker offload for the legacy GM 1.4 runner

**Phase 1 research and design, 2026-11-22.** This document records the feasibility probe and proposes a staged experiment. No player-facing feature is implemented here.

## Findings

### Runner and toolchain

The configured merge base is the 16 August 2023 legacy build. Its `Cat Goes Fishing.exe` is a PE32 Intel 80386 executable; the archive is GM bytecode 16 and `NewWorld` runs at 30 simulation steps per second. The current Steam build is YYC and is not the target. This means the extension must be a 32-bit Windows DLL even though the VPS and Wine host are 64-bit.

The VPS has Wine 11.22 at `~/opt/wine/bin/wine`. `i686-w64-mingw32-gcc` was initially absent; installing Ubuntu's `gcc-mingw-w64-i686` 13.2 toolchain succeeded. A minimal 84,789-byte test DLL was built and inspected as PE32 with the undecorated exports `jamm_noop`, `jamm_store_i32`, `jamm_sum16`, and `jamm_string`. Test archives were generated from the legacy `data.win` with numeric and string signatures, a 16-real-argument call, and a buffer-pointer round trip; all three call-count variants compile and read back as GM bytecode 16. The generated DLL and test archives are temporary files under `/tmp/native-offload-probe`; they are not game assets and are not included in this change.

**In-runner load and call measurements:** not obtained. I queued `NOBENCH=1 ~/jamm-night/bin/run.sh /tmp/native-offload-probe/native-1c.win native-probe-1 dock 180` behind the shared game lock. After two minutes it was still waiting; an older run held the lock with a `Cat Goes Fishing.exe` zombie, and other runs were queued as well. I stopped only my queued run and deleted its disposable `game` copy. I did not bypass the shared lock or restart/kill another run. Thus DLL loading, runtime signature limits, buffer-pointer round trip, and 1/100/10,000-call timing remain unverified; no timing has been estimated or substituted. The requested serialized measurements are an explicit Phase 2 prerequisite in the plan below.

### GML external calls and buffers

UndertaleModLib's GM 1.x compiler catalog recognizes `external_define`, `external_call`, `external_free`, the fixed-arity `external_define0` through `external_define8`/`external_call0` through `external_call8` helpers, and `buffer_get_address`. Those catalog entries establish that JAMM's compiler can emit the calls; only the in-runner probe establishes what this particular VM executes.

The external API is limited to `ty_real` and `ty_string`; the latter is a null-terminated string. Keep the ABI simple: fixed-width numeric fields in a buffer, and no strings or GameMaker handles inside native worker code. GameMaker's current reference describes `external_call` with up to 16 supplied arguments and the external definition's result and argument types. The 1.4 VM's accepted maximum remains a probe result, rather than inferred from the newer manual. See [external_define](https://manual.gamemaker.io/monthly/en/GameMaker_Language/GML_Reference/OS_And_Compiler/external_define.htm) and [external_call](https://manual.gamemaker.io/monthly/en/GameMaker_Language/GML_Reference/OS_And_Compiler/external_call.htm).

`buffer_get_address` is the intended zero-copy handoff. The manual calls its result a pointer to the raw, aligned buffer address and warns that native code must not free or resize it and must stay within the buffer bounds. The pointer therefore belongs to the GameMaker buffer: retain the buffer while workers can access the address, do not resize or delete it until all jobs have joined, and preferably allocate fixed-size double buffers. No documented guarantee makes a buffer safe for simultaneous GML and native access. Ownership must be exclusive by phase: GML fills the input buffer, workers read it and write a separate output buffer, then GML reads the completed output after an acquire/join boundary. A worker must never call back into GML or touch GameMaker instances, surfaces, DS structures, or engine APIs. See [buffer_get_address](https://manual.gamemaker.io/lts/en/GameMaker_Language/GML_Reference/Buffers/buffer_get_address.htm).

The live probe will demonstrate whether this VM exposes `buffer_get_address` and whether the pointer can cross the external-call ABI on this runner. It cannot establish general thread safety; that remains an explicit synchronization and ownership requirement.

### Existing profile evidence and candidate work

The profiler uses the 2023 legacy runner and reports relative CPU event time. Draw entries include render effects, so the VPS's software renderer makes their time unsuitable for claims about player GPUs.

| Work | Measured legacy-runner profile | What can be computed off-thread | Main-thread work that remains |
| --- | --- | --- | --- |
| `gml_Object_Fish_Step_2` | In existing pack captures it is roughly 40–75% of the loop (for example, `pub5` dock reports 44–57%). | Only isolated numeric calculations over a stable snapshot. | State dispatch, event calls, RNG, collision/engine queries, path and instance mutation. This event share is not an offloadable share. |
| `fish_avoid_fish` / bounded `FishLarge` avoidance | 110–166 helper calls per tick in vanilla/heavy/all-7 captures; 4.20–4.94% of profiled loop time. The all-7 dock capture reports 165.86 calls/tick and 4.94%. | Squared-distance tests and force-vector accumulation for an explicit candidate list, using captured positions/velocities/radius/force. | Preserve traversal order and original `sdist`/`dir` side effects; apply velocity changes on the GML thread. Other selectors and unbounded queries remain native. |
| `SceneItem_Step_2` and `CaveRock_Step_0` | All-7 dock/deepwater are about 8–9% and 9–10% of loop time respectively. In the decor report, the all-7 dock values are 2,880 SceneItem calls/tick at 14,957 µs and 3,836 CaveRock calls/tick at 17,969 µs. | Camera-distance, bounds, and sleep/wake eligibility decisions from copied positions and flags. | Visibility/activation, collision state, timers, camera-jump catch-up, and every instance API call. These event bodies are not pure as a whole. |
| `SceneItem_Draw_0` | About 6–7% in all-7 captures; marked render-affected. | At most a visibility/culling decision that could suppress a later draw. | All rendering; no claim about GPU or draw execution speed. |

The fish-grid report is especially relevant: its GML spatial index preserved behavior but did not improve the controlled heavy-dock pair (171.64 ms/tick Fish End Step off vs 179.27 on). The all-7 pair moved faster with the grid but had only one pair and could not isolate the effect. The report attributes much of the remaining cost to per-fish work and global selectors. Native work should start with a small measured kernel and must not assume a spatial index or a native rewrite wins.

Sources in this repository: [fish-perf-grid](../reports/fish-perf-grid.md), [decor-sleep](../reports/decor-sleep.md), [weak-pc-preset](../reports/weak-pc-preset.md), and [the measured progress page](../../site/cgf-mods/progress.html). The latter records local Proton runs on 2026-11-21/22: legacy runner, Ryzen 7 5700X, 30-second captures, and roughly ±5% run-to-run noise; those numbers are useful context but are not measurements from this VPS.

## Expected gain: Amdahl bounds

For a serial share `p` accelerated by factor `s`, total speedup is `1 / ((1 - p) + p/s)`. The limit as `s` tends to infinity is `1 / (1 - p)`; for four perfectly balanced workers it is `1 / (1 - p + p/4)`.

| Measured candidate share `p` | Infinite-speed kernel ceiling | Ideal four-worker ceiling |
| ---: | ---: | ---: |
| Fish avoidance, 4.20–4.94% of loop | 1.044–1.052× | 1.033–1.039× |
| Fish End Step, broad 40–75% event share (not all pure) | 1.67–4.00× | 1.43–2.29× |
| SceneItem Step + CaveRock Step, about 17–19% | 1.20–1.23× | 1.15–1.17× |

These are ceilings, not forecasts. Snapshot packing, DLL entry/exit, synchronization, applying results, and stale-result fallbacks reduce gains. The first hotspot proposal is therefore bounded `FishLarge` avoidance math only if measurement shows the per-call cost or computation batch amortizes the buffer boundary. Its measured loop share caps whole-loop gains near 4% even with an infinitely fast kernel. Decor distance decisions are the next candidate if a controlled event profile shows a larger cullable share. Broadly moving Fish Step or Draw to workers is not supported by this analysis.

## Proposed async shape

1. **Initialize once.** The GML controller checks the DLL's ABI/version and required exports. If the file, export, bitness, or signature is unavailable, mark native offload unavailable and run the existing GML path. Free handles only after workers stop.
2. **Begin Step: snapshot.** On the main thread, pack active eligible fish/decor data into a fixed-size input buffer: tick/generation ID, count, stable instance ordinal, x/y, required velocity/state flags, and query parameters. Never pass instance IDs as dereferenceable native pointers.
3. **Submit without waiting.** One short external call hands the address, count, generation, and output address to the DLL. The DLL owns its worker pool and reads only the immutable input snapshot. If the previous job is still active, keep the previous valid result or use the GML fallback; do not block the game loop.
4. **GML continues.** Existing GML simulation proceeds while native workers compute independent candidate lists, distances, avoidance accumulators, or culling flags.
5. **End Step / next Begin Step: collect.** Poll completion cheaply. Only consume results with the expected generation and valid count. Apply numeric results on the main thread in original instance order. If the result misses its deadline or no longer matches the live instance set, discard it and execute the original GML code for that tick.
6. **Shutdown and save/load.** Join workers and invalidate snapshots on room change, game restart, save/load, object destruction, or controller teardown. Do not serialize pointers, thread handles, pending jobs, or buffer addresses; recreate them on load.

The async overlap needs careful parity work. A same-tick avoidance result is consumed during Fish End Step, so computing it from Begin Step's earlier state naturally adds latency. A safe first prototype must either prove a prediction/use point that preserves results, split only independent work from the same tick, or explicitly accept and quantify one-tick stale input. It must not silently present delayed results as behavior-identical. Any missed/invalid job uses the original GML calculation.

## Risks and constraints

- **Determinism:** Worker scheduling and reordered floating-point accumulation can change velocity results. Use fixed input ordering, fixed partitions, deterministic reductions, explicit generation IDs, and parity hashes; preserve original reduction order if exact parity is required.
- **Save/load and lifecycle:** Buffers and threads are process-local. Never persist addresses. Cancel or drain work before room transitions, restart, save/load, DLL unload, or object teardown.
- **Missing DLL / bad ABI:** `external_define` may fail on missing exports or DLL load errors. Probe safely at startup and permanently choose the original GML fallback for that session. Do not call invalid external handles.
- **Steam / Wine / Proton:** The measured runner is 32-bit Windows code under Wine, but player Proton versions, prefix layouts, and filesystem policies differ. Keep the DLL next to the executable, avoid absolute install paths, test both Wine and Proton, and make the feature optional until supported prefixes are known.
- **Distribution/security:** Windows DLLs commonly trigger antivirus or SmartScreen warnings, especially for a small community mod. Ship source and reproducible build metadata, sign releases if practical, document the MIT-licensed source, and never download or execute a binary dynamically.
- **Threads:** The GameMaker VM stays single-threaded. The native library must own synchronization and worker lifetime; worker threads may not enter the VM or engine. Buffer APIs do not promise concurrent access safety.
- **Scope:** GPL-3.0 project linking UndertaleModLib remains unchanged; the new native code can be separately MIT-licensed as requested.

## Packaging proposal

Ship `jamm_native.dll` from a JAMM builtin/ModKit package beside the selected Windows runner executable, with its MIT-licensed C/C++ source and pinned MinGW-w64 build instructions in this repository. The package installer should copy only the DLL and metadata, preserve the user's originals, and remove or replace only files it owns. Build the initial target as PE32 for this legacy runner; do not bundle a 64-bit-only DLL. Do not commit generated test DLLs, game archives, or extracted game data.

## Implementation plan (Phase 2 review required before Phase 3)

| Step | Work | Measurement / exit condition |
| --- | --- | --- |
| 1 | Finish the legacy VM probe: load a minimal 32-bit DLL, confirm external signatures/argument limit and `buffer_get_address` round-trip, and capture 1/100/10,000 calls per tick plus GML-loop baseline. | Serialized in-game results, code-error log/screenshots on failure, DLL PE/export inspection, and raw CSV kept outside the repo. This table is the Phase 1 live-measurement checkpoint. |
| 2 | Add a minimal native package and opt-in GML startup/ABI probe with a guaranteed GML fallback. | No-DLL and wrong-ABI launch results; verify no invalid external call and no game-file overwrite. |
| 3 | Implement buffer ownership, fixed-size input/output layouts, worker pool, generation IDs, cancellation, and shutdown. | Stress launch/room-change/save-load tests; measure packing, submit, wait/poll, and apply costs separately. |
| 4 | Prototype only one bounded `FishLarge` avoidance batch, retaining a baseline GML implementation and a runtime switch. | Same seeded save and fish population; compare every-tick state/position/velocity hashes and RNG counts; report median/p95 loop and Fish End Step time across serialized runs. Reject if parity or total loop time regresses. |
| 5 | Explore decor distance/culling decisions only if Step 4 or a new profile identifies a larger pure share. | Visibility/sleep parity and wake latency, camera-jump/sonar/room-transition checks, then event and whole-loop timing. |
| 6 | Package the optional DLL through the builtin/ModKit path with source, MIT license, and reproducible 32-bit build. | Fresh install/remove, DLL missing, antivirus/quarantine recovery, Linux Wine and Windows/Proton smoke runs, no changes to original game files. |

Phase 2 must approve the scope and measured first hotspot before any player-facing Phase 3 implementation starts.

## Orchestrator review (Phase 2, round 1, 2026-11-22 02:40)
Not yet approved. Toolchain (PE32, i686 mingw-w64) and the offline archive generation are fine, and the Amdahl table is
honest (avoidance only ~4–5 % of the loop → ≤ 1.05×; Fish End Step 40–75 % but mostly not pure math). The decision for
Phase 3 depends on the live numbers, so Phase 1b runs first:
- run the prepared probes in-game: DLL load under Wine, `buffer_get_address` round trip, accepted argument count,
  and the 1 / 100 / 10,000 calls-per-tick overhead (µs per call). Queue on `~/jamm-night/bin/run.sh` and WAIT for the
  lock (it can take up to an hour; do not cancel after minutes; do not kill other runs). On EXITED run
  `~/jamm-night/bin/steam-restart.sh` and retry.
- also measure one worker-thread round trip (fill buffer → worker computes N=2,000 distances → join/read) per tick.
Then update this document with the measured numbers and a go/no-go for Phase 3 by the criteria: per-call overhead
small enough that one batched call per tick costs < 0.5 ms, and a first hotspot with a measured share large enough to
win ≥ 5 % tick time. If the numbers say no, write that clearly – a negative result is acceptable.

## Phase 1b results and decision (2026-11-22)

The prepared probes were queued through `~/jamm-night/bin/run.sh`; no shared lock was bypassed. The initial matrix
archive used the newer variadic `external_define` form and ended in a GameMaker code error (`Wrong number of
arguments to external_define()`). The matrix was regenerated with the legacy fixed-arity helpers and run again. That
attempt reached the game but timed out after Wine reported an unhandled write page fault at `00000000` (instruction
address `764F1518`). The 2,000-distance worker probe also reached the game and timed out after an unhandled read page
fault at `00000000` (instruction address `764F15C8`). Both runs showed the Wine debugger's program-error dialog and
produced no probe CSV. The earlier worker launches exited during Steam initialization; Steam was restarted and the
probe retried as directed. Disposable `runs/*/game` copies were removed after the runs.

| Phase 1b measurement | Result |
| --- | --- |
| DLL load and native call on the legacy VM | Not verified: the fixed-arity matrix crashed before recording a result. |
| `buffer_get_address` pointer round trip | Not verified: no round-trip value was recorded before the crash. |
| Accepted external argument count | Not verified: the variadic attempt was rejected by GML, and the fixed-arity probe crashed without recording whether the eight-real call completed. |
| 1 / 100 / 10,000 calls per tick, including µs/call and GML baseline | No measurements: the matrix produced no CSV. |
| One worker round trip per tick, N=2,000 distances | No measurement: the worker probe crashed before recording a checksum or timing. |

**Decision: no-go for Phase 3.** The call-cost gate (<0.5 ms for one batched call per tick) is unverified because
the runtime probes did not produce measurements. The currently measured bounded `FishLarge` avoidance share is
4.20–4.94% of loop time, below the review's ≥5% tick-time win gate even before snapshot, synchronization, and result
application costs. The larger Fish Step and decor event shares are not measurements of pure offloadable work. There is
therefore no measured first hotspot that clears the required gain threshold, and the worker probe did not demonstrate
a functioning native round trip. Phase 3 remains unapproved and was not started.
