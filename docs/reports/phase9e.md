# Phase 9e — downloader, checker and operation feedback

Implemented on `ui/browse-mods`. No commits, pushes, git configuration changes, `tools/` changes or submodule source edits. The pre-existing README.md edit was preserved.

## Downloader and resolver

- Game Jolt project, `get/build?game=…&build=…`, and dashboard game/package links use the existing public overview and get-download-url endpoints. Explicit build IDs and package IDs filter the published files exactly; there is no substitution with a newer build.
- Transfers retry transient connection/DNS failures, server errors, timeouts and truncated bodies with bounded exponential backoff. Download attempts resolve signed URLs again, including after authorization expiry. Metadata requests also retry transient failures.
- A 45-second resolution/header deadline, 30-second body stall deadline and 15-minute overall deadline bound transfers. Cancellation removes partial files.
- Resume requires byte-range support and an entity validator (strong ETag or Last-Modified). Requests send Range/If-Range; partial responses must match the requested offset and declared complete length. A server returning a full response restarts safely. A rejected range triggers a fresh transfer.
- Declared length and size limits remain enforced. Public-address/DNS pinning, redirect checks, content signatures, FORM lengths and ZIP safety checks remain in place. Permanent unsafe-source failures are not retried.
- Concurrent requests for the same provider source serialize and reuse the validated SHA-256 cache. Publishing identical bytes from different sources tolerates a concurrent cache publisher.
- Arctic actually publishes `mod-do-artico-feito-por-eu.win`, build 1891559, 55,004,182 bytes. Game Jolt marks it `is_installer: true` despite the .win filename. Supported archive filenames now proceed to byte validation regardless of that advisory flag. Executable filenames remain excluded; downloaded files are never executed.
- No RAR/7z dependency was needed for the observed release. Unsupported formats retain the explicit local .win/ZIP fallback. The documented itch.io download API requires authentication/scopes; Open Silo retains Open source page rather than using an undocumented scraping/download flow. References: [itch.io OAuth scopes](https://itch.io/docs/api/oauth), [server API](https://itch.io/docs/api/serverside).

## Checker

The Schizophrenia failure was reproduced on the real archive. Its cause was an object literally named `[REDACTED]`, emitted as a bare asset reference by the decompiler. The compiler interpreted that reference as an array literal. This was not a real array initialization or a mod incompatibility.

Invalid GML asset identifiers now pass through the existing family-qualified, hex-encoded temporary symbol mechanism, including typed numeric references. Already imported legacy patches also repair the exact bracketed asset spelling through token spans, without replacing strings or comments. Symbols link to the final archive's resource indices before compilation. No general textual array rewriting was retained. A synthetic legacy archive regression covers decompilation, patch creation, compilation and output verification for this exact object name.

Compilation/decompilation and semantic-processing failures receive a distinct `Tool limitation` verdict and explanation. Engine mismatch and patches that cannot apply/verify remain Incompatible. Successfully built and verified matching-engine bytecode-16 legacy 2023 archives are Compatible despite GEN8 timestamp differences; unrelated source dates remain Unsure. This is verified applicability against the configured base, not proof of a mod's historical code ancestor or in-game behavior. Source GEN8 date is included in the explanation. Analyzer revision is 3 and asset-symbol/decompiler revision is 3, invalidating older facts and identifying revised patches.

## UI

- Both normal and advanced Your mods lists have expandable rows with small catalog artwork associated through provenance, or a neutral geometric placeholder. Artwork is also loaded for known local imports without first opening Browse mods.
- Expanded content shows computed Adds lines, size, source and compatibility. Saved ModFacts are preferred when available; cached patch counts provide the local fallback. Enable/disable controls remain separate.
- Long manager operations show a plain-language step and progress bar. Counts such as decompilation/reference-resolution totals are determinate; steps without counts are indeterminate. Downloads report byte progress. Browse content checks also expose download/analysis progress.
- Raw manager status text is hidden while the primary operation indicator is active. Full processing messages remain available in the collapsed Show details log.
- Headless UI regression expands a local row, checks computed content, verifies 812/1839 determinate progress and the collapsed details control. A rendered screenshot was inspected at `/tmp/jamm9e-ui.png` (not added to the repository).

## Validation and live evidence

- `dotnet build -c Release`: passed, zero warnings and zero errors in the build summary.
- `dotnet test -c Release`: 247 Core tests and 68 GUI tests passed; 5 Core and 1 GUI opt-in tests skipped because their environment variables were unset.
- Added fake-handler coverage for flaky connection recovery, conditional range resume, full-response restart, changed-validator restart, expired signed URL re-resolution, wrong length, stalled bodies, source/build/package filtering (including misleading installer metadata), and concurrent cache reuse. Existing cancellation, unsafe redirect, size/content and ZIP tests also pass.
- `git diff --check`: passed.

Targeted live probes used a separate `/tmp/jamm9e-probe` store and the read-only 2023-08-16 legacy base. They resolved, downloaded and checked the actual catalog releases:

| Catalog mod | Published build | Result |
| --- | --- | --- |
| 7 Schizophrenia | 1989286 | Compatible; built and verified after the asset-symbol fix |
| 4 Antiquity | **1700162**, exactly as requested | Download succeeds; verification rejects `PageItem 1297`: pixels but no embedded texture page |
| 23 Flora N Flores | 2024065, package **1119262** | Compatible; built and verified |
| 33 mod do artico | 1891559 | Compatible; built and verified |

The Antiquity source itself contains one nonempty texture item with a null texture page, confirmed by inspecting the downloaded source before any merge. Its SHA-256 is `e1b5ebdb257e4143850763c3a06bb96328fe6449dcd31d3109bfdc39d7d81a28`. That existing integrity check was retained rather than reporting a successful build. No input game archive was modified or installed by these probes. The orchestrator's complete catalog harness remains the full live acceptance check.

## API and limits

Existing public resolver, downloader, cache and analyzer entry points and record shapes are retained, including `ModHttp.SendAsync`'s original signature. Additions are `ModDownloadService.DownloadResolvedAsync`, configurable stall/retry timing, and a progress-reporting `ModFactAnalyzer.Analyze` overload. Existing `Analyze(archive, token)` still works. `Tool limitation` is an additional compatibility string consumers should display as an unknown/tool-processing state, not Incompatible.

Range support is opportunistic and validated; servers without it restart. HTTP backoff is bounded to four transfer attempts (three metadata attempts). Local summaries do not invent fish counts when only cached patch metadata is available. Historical code ancestry and actual gameplay still require the orchestrator's/in-game checks; matching 2023 dates alone never bypass build and verification.
