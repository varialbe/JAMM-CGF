# Phase 12 — parallel analysis and reusable base work

Implemented on `perf/parallel-analysis`; no commits, pushes, Git configuration changes, GUI edits, or submodule source edits.

## Implementation

- `AnalysisOptions` controls worker count, cache location/use, and serial phase-timing callbacks. The default is `max(1, Environment.ProcessorCount - 1)`. `CGFMM_ANALYSIS_WORKERS=N` overrides it; `CGFMM_ANALYSIS_CACHE=0` disables disk reuse. An explicit `MaxDegreeOfParallelism = 1, UseCache = false` selects the sequential comparison path.
- Parallel work covers CODE fingerprints, root GML decompilation, pure per-entry field diffs, semantic code normalization, independent patch preparation, and verification decompilation. Results occupy input-indexed slots and are assembled in the original order. Texture snapshot extraction and every `CompileGroup` queue/compile operation remain serial.
- UTML's `GlobalDecompileContext` constructor explicitly documents that rebuilding global functions mutates the archive and is not thread-safe. Registration and global-function initialization now happen before workers start. Each worker has its own context, source dictionary, and warning buffer, with the initialized global-function catalog supplied explicitly. Underanalyzer's per-decompilation state is local; the initialized macro registry and function catalog are read during this work. Compilation was not made concurrent.
- Progress callbacks are serialized behind a batch-local gate, incrementing completed roots only after work completes. Warnings are replayed in input order. Parallel failures preserve contextual exception types, including cancellation, rather than exposing aggregate exceptions.
- `<manager data dir>/base-analysis` stores base fingerprints, texture-region/pixel digests, and decompiled base GML. Identity includes the verified base SHA-256, JAMM/UTML/Underanalyzer module revisions, asset-type revision, and (for GML) the ambiguity set that changes emitted symbols. Synthetic models do not use disk caching by default because their caller-supplied identities are not necessarily archive hashes.
- Cache entries have a SHA-256 integrity envelope, atomic temporary-file publication, guarded ordinary storage paths, 64 MiB entry limits and 256 MiB total JSON limits with oldest-first eviction. Fixed lock stripes coalesce same-key work within a process; publication/eviction is serialized. Invalid/unavailable cache storage falls back to computation. Manager import, compatibility analysis, and profile builds pass their actual store root, including custom data directories.
- Profiling exposed repeated texture-atlas decoding during patch snapshots as the largest bottleneck, especially for HD Graphics. Snapshot extraction now reuses the region hashes computed during fingerprinting. Persistent entries contain small digests and GML, never decoded images or live archive models.

## Measurement

Measurements use the specified legacy base at `/run/media/varialbe/Festplatte 2/Games/CatGoesFishing-legacy/2023-08-16_manifest-9039707068725406600/data.win` and all seven local `~/Cat-Goes-Fishing-Mods/*.win` archives. Inputs were opened read-only. Baseline source was copied from HEAD into `/tmp`; benchmark binaries, patches, manager cache, and disposable merged archives were also outside the repository under `/tmp`.

This machine exposes 16 processors. Measurements use Release builds and four workers for the parallel runs. Times are single-run wall-clock seconds, not statistical claims. Each row loads base/mod, creates the patch, builds a new archive, reloads/verifies it, and hashes the output. Ordinary OS file caches are not flushed. The first parallel pass starts with no matching base-cache revision and shares newly created base work across later rows; the second pass has a warm base cache. Compatibility-facts result caching is bypassed, so the work is actually performed.

| Mod | Original | Parallel, first pass | Parallel, warm base | Warm reduction |
|---|---:|---:|---:|---:|
| cave-expansion.win | 29.99 | 17.86 | 15.14 | 50% |
| colour-expansion.win | 16.06 | 9.58 | 9.05 | 44% |
| jamm-hd-graphics.win | 370.93 | 21.97 | 20.69 | 94% |
| jamm-hd-lite.win | 9.64 | 7.18 | 6.56 | 32% |
| jamm-perf.win | 9.53 | 6.99 | 6.52 | 32% |
| jamm-smooth-fps.win | 9.61 | 7.28 | 6.67 | 31% |
| the-abyss-beta.win | 20.03 | 12.97 | 12.51 | 38% |

All seven single-mod merged archive hashes match the original implementation in both passes.

A separate phase-instrumented Cave run makes the source of the improvement visible:

| Phase | Original | Parallel, first pass | Parallel, warm base |
|---|---:|---:|---:|
| load | 1.247 | 1.287 | 1.206 |
| fingerprint | 7.261 | 7.401 | 5.010 |
| decompile | 0.887 | 0.360 | 0.296 |
| diff | 14.708 | 1.830 | 1.980 |
| build/compile | 0.990 | 1.057 | 1.016 |
| verify | 1.833 | 1.880 | 1.657 |
| write | 0.209 | 0.219 | 0.208 |

Load includes all input/reload operations. Decompile includes all measured batches; verification includes its own reload and decompilation, so these phase figures overlap and do not sum to the total. Diff covers patch extraction/normalization and field comparison. Other semantic linking/reference analysis is included in the end-to-end rows. The repeated instrumented original Cave run totaled 30.762 s.

The HD baseline spent 346.277 s between its decompile/diff progress markers, predominantly re-decoding texture atlases for snapshots. With region-digest reuse, its final first-pass total is 21.969 s. This is a reuse benefit, not evidence that compilation scales with worker count.

The build-only merge of all seven mods, using the same prebuilt patches in sorted filename order, also preserved its exact output hash:

| Operation | Original | Final parallel | Reduction |
|---|---:|---:|---:|
| Seven-mod build and verification | 29.300 | 27.636 | 6% |

Both produced SHA-256 `503d2ddff5b6c3e32fe604ccdb531a1aa4e713719ef07654b48207ab7cb3e77f`. The final run was taken after the other test workloads finished. This small single-run difference is much less conclusive than the large per-mod analysis gains; serial compilation and remaining reference analysis still dominate this build. An earlier measurement before parallel semantic normalization/preparation showed no meaningful combined-build gain.

`CreatedUtc` is the sole field normalized when comparing separately generated patch JSON; input identity and all substantive patch fields remain unchanged.

## Validation

- `dotnet build -c Release -m:1 -nr:false -p:NuGetAudit=false`: passed, **0 warnings, 0 errors**, across the solution.
- Full Core in-process xUnit pass: **250 passed, 0 failed, 6 opt-in real-file tests skipped**.
- The new synthetic comparison uses 32 independently compiled entries, compares sequential/parallel/warm-cache patch JSON and merged archive SHA-256, and compares complete `ModFacts` (including the ordered `Adds` values). It also exercises corruption recovery, cache eviction, different-base invalidation, and refusal to write through linked storage.
- The existing candidate-only decompilation test passes; unchanged synthetic entries still avoid decompilation.
- Every real patch from both measured parallel passes matches the original HEAD implementation, with only `CreatedUtc` normalized. Every corresponding merged archive SHA-256 also matches the original implementation. Input archive SHA-256 values were rechecked against the baseline patch identities and remained unchanged.
- `git diff --check`: passed.

The dedicated opt-in `RealParallelAndSequentialPatchesFactsAndArchiveHashesMatch` test was also run through the in-process harness with both real-file environment variables set: **1 passed, 0 failed**. It iterated all seven archives and checked the sequential/parallel/warm-cache paths. The final semantic-normalization extension was additionally checked against the original implementation by rerunning all seven measured patch/build comparisons.

The opt-in comparison can be rerun with:

```sh
JAMM_ORIGINAL='/path/to/legacy/data.win' JAMM_MODS_DIR="$HOME/Cat-Goes-Fishing-Mods" \
  dotnet test tests/Jamm.Core.Tests -c Release \
  --filter FullyQualifiedName~RealParallelAndSequentialPatchesFactsAndArchiveHashesMatch
```

This compares all local `.win` mods, including cold/warm patch results, extracted facts, sequential/parallel archive hashes, and ordered progress callbacks.

The standard test command cannot run its test host in this sandbox: VSTest fails opening its localhost listener with `SocketException (13): Permission denied`. An external, in-process reflection harness invokes the xUnit fact methods and theory data rows, awaits tasks, and disposes fixtures. It is a fallback for this environment, not a replacement checked into the project.

The normal parallel MSBuild invocation also exited unsuccessfully without useful diagnostics here; `-m:1 -nr:false` works. NuGet audit needs unavailable network access, so final offline validation uses `-p:NuGetAudit=false`. These are invocation-only workarounds; no build or Git configuration was changed.

## Practical limits

The improvement comes from CPU concurrency and avoiding repeated work; GPU execution was not introduced. Parsing, structured texture work, compilation, archive writing, and some semantic linking still limit scaling. Cache entries are disposable, and recompiling the tool intentionally invalidates them. Multiple independent manager processes can compute the same missing entry concurrently, while atomic publication prevents partial entries from being consumed.
