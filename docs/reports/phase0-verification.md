# Phase 0 verification

Verified locally on 2026-11-15 with .NET SDK 10.0.404 on Linux.

- `dotnet build -c Release`: passed, zero warnings and zero errors.
- `dotnet test`: 23 passed, one real-file test skipped because its environment variables were unset.
- Opted-in real-file test: one passed, zero skipped. Command:

```sh
JAMM_ORIGINAL="$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Cat Goes Fishing/data.win" \
JAMM_MODS_DIR="$HOME/Cat-Goes-Fishing-Mods" \
dotnet test --no-build --filter FullyQualifiedName~RealFileTests
```

The required real-file run used:

```sh
dotnet run -c Release --no-build --project src/Jamm.Cli -- phase0 \
  --original "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Cat Goes Fishing/data.win" \
  --mods "$HOME/Cat-Goes-Fishing-Mods"
```

All four files were read, all three comparisons were produced, and `docs/reports/phase0.md` was written. Exit code 2 is expected: the installed original is YYC with zero CODE entries. Its GEN8 version is 2.0.0.0, UTML detects 2023.6.0.0, and bytecode metadata is 17. All three mods are VM builds with GEN8 1.0.0.9999 and bytecode 16. A code-similarity base verdict is unavailable without the original bytecode. There were no UTML load warnings. Six out-of-bounds texture-region notices were recorded, three each in Cave Expansion and The Abyss; available region pixels were hashed with transparent padding and an invalid-region marker.

Additional CLI checks passed:

- Inspecting the real original prints the YYC explanation and exits 2.
- Comparing Colour Expansion with itself exits 0, reports LIKELY SAME BASE and 100% identical code/original assets, and writes valid JSON covering all twelve asset families. JSON string identifiers are hashes rather than game text.
- Comparing the original with a missing mod followed by Colour Expansion exits 1, records the missing file, still completes the readable comparison and writes valid JSON. Normal error output has no stack trace.
- `phase0 --mods` also accepts a single mod file and writes its selected report.
- Selecting the original as the report destination, or using a `.win` JSON destination, is rejected before loading/writing.
- SHA-256 hashes of all four input files were checked before and after the CLI verification and remained unchanged; they match the inspection report.
- UndertaleModTool remains unmodified at tag 0.9.2.0 (`03c7048438347799e0717b6e57a9358d000472e0`). Its compressed-texture `ToSpan` closed-stream issue is avoided through its public writer API in JAMM's fingerprint implementation.

Scratch JSON and captured CLI output are under ignored `/out/`; no game data, extracted content, or build outputs are included in the commit.

## Local commit handoff

The checkout's `.git` directory is mounted read-only in the implementing environment: `git add` fails creating `.git/index.lock`. The verified change set is therefore committed using separate temporary Git metadata and exported as `out/phase0.bundle`. The original checkout's branch/index cannot be updated here. The bundle contains an initial `main` commit with the pinned submodule gitlink and all Phase 0 source, tests and documentation. It can be imported into writable repository metadata with `git fetch out/phase0.bundle main`; the orchestrator can then update its branch to the fetched commit. No push was performed.
