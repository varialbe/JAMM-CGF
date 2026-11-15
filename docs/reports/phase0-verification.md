# Phase 0 fixes — verification

Verified locally on 2026-11-15 with .NET SDK **10.0.404** on Linux.

## Build and tests

- `dotnet build -c Release`: **passed**, zero warnings and zero errors.
- `dotnet test`: **32 passed**, one real-file test skipped when its environment variables were unset.
  The final repeat used `dotnet test -m:1 -nr:false` with the same result.
- Full Release suite with both real-file variables set: **33 passed, zero skipped**.

```sh
JAMM_ORIGINAL="$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Cat Goes Fishing/data.win" \
JAMM_MODS_DIR="$HOME/Cat-Goes-Fishing-Mods" \
dotnet test -c Release --no-build -m:1 -nr:false
```

The synthetic regressions cover either side being YYC, skipped bytecode families and null
percentages, inspect-only exit code 2, named-assets-only percentages, deterministic
unsupported markers for external textures / Spine / SWF / invalid regions, missing script
CODE entries, GMS2 room layers, path-free reporting, separate read/analysis errors, live-model
ownership and safe output validation (including symlink aliases and new `.win` opt-in).

## Real-file report

Regenerated [phase0.md](phase0.md) with:

```sh
dotnet run -c Release --no-build --project src/Jamm.Cli -- phase0 \
  --original "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Cat Goes Fishing/data.win" \
  --mods "$HOME/Cat-Goes-Fishing-Mods"
```

**Exit code: 0.** All four files were read and three comparisons were produced.

| File | Compilation | GEN8 version | Detected version | Bytecode | CODE entries |
| --- | --- | --- | --- | ---: | ---: |
| `data.win` (installed Steam build) | YYC | 2.0.0.0 | 2023.6.0.0 | 17 | 0 |
| `cave-expansion.win` | VM | 1.0.0.9999 | 1.0.0.9999 | 16 | 1926 |
| `colour-expansion.win` | VM | 1.0.0.9999 | 1.0.0.9999 | 16 | 1942 |
| `the-abyss-beta.win` | VM | 1.0.0.9999 | 1.0.0.9999 | 16 | 1921 |

Each comparison prints `NOT COMPARABLE: data.win is YYC (no bytecode)`, skips code,
scripts, variables and functions, and emits no unchanged percentage for the YYC pair.
The Steam build's GEN8 timestamp is **2024-08-05 UTC**. Pairwise mod comparisons have
**92.28–93.91% identical code** (approximately 93%); this supports seeking a shared
legacy base, rather than using the current YYC original.

There were **zero UTML warnings** and **six fingerprint warnings**, three invalid texture
regions each in Cave Expansion and The Abyss. Those regions and their sprites use stable
`unsupported:out-of-range region` fingerprints. The remaining assets are still analyzed.
Regions are processed page by page, retaining hashes and at most one decoded atlas.

## CLI and preservation checks

All checks passed:

- Inspect the installed YYC original: exit **2**, with the no-bytecode explanation.
- Compare the YYC original against all three mods with JSON: exit **0**, eight non-code
  families per comparison, both percentage fields `null`, file names only, no source paths.
- Compare Colour Expansion with itself: exit **0**, twelve families, 100% identical code
  and named assets, valid JSON and `LIKELY SAME BASE`.
- Compare the original with a missing mod followed by a readable mod: exit **1**, clear
  file-name-only read error, readable comparison still completed, valid JSON, no normal stack trace.
- `phase0 --mods` with a single mod: exit **0**, two files read and a report written.
- Selecting an input as output, or a `.win` report destination: rejected with exit **1** before loading.
- SHA-256 checks before and after verification: **all four input files unchanged**;
  hashes match the regenerated inspection report. No game files were written.
- Tracked files outside `external/` contain no absolute personal home paths. New implementation
  and documentation files were checked too. The supplied, untracked task specification retains
  its literal privacy-search examples and was left unchanged.
- `git diff --check`: clean. Build outputs, scratch JSON and captured CLI output remain
  ignored under build directories and `out/`.
- UndertaleModTool remains unmodified at release **0.9.2.0**, commit
  `03c7048438347799e0717b6e57a9358d000472e0`.

## Limits and tooling notes

A legacy original and matching runner were not available for this verification. The suggested
99%+ code match against a legacy base is a target to verify after following
[the legacy-version guide](../legacy-version.md), not a measured result here.
Unsupported marker equality cannot establish equal content, and a similarity verdict does not
prove that a merge will work.

Concurrent final test invocations hit an MSBuild named-pipe `Address already in use` error
in the sandbox. They were stopped and rerun serially with `-m:1 -nr:false`, which passed.
The test runner also prints an informational `VSTEST_ARTIFACTS_DIRECTORY` fallback notice;
it does not fail the tests. UndertaleModLib's package creation prints its existing missing-readme
notice; the Release build still reports zero warnings.

No commit, push, release, installation or game-file modification was performed.
