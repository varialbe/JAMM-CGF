# Phase 3 independent review follow-up

All 13 findings were checked against the implementation and the pinned UndertaleModLib
compiler. Findings 3 and 8 are false positives for the stated behavior; the remaining
11 findings are addressed. No commits were made.

| Finding | Disposition |
| --- | --- |
| 1 | Fixed: merge field and GML patches after the last effective deletion. |
| 2 | Fixed: accept `family/name` resolutions; shared bare names affect conflicting families, and bare `keep-both` affects code only. |
| 3 | Skipped: `ResolveCode` already selects the named mod per hunk; moving it preserves the other mods' relative order, and disjoint edits are intentionally retained. |
| 4 | Fixed: rebuild retained assets from surviving edits, check ancestor references for unchanged selections, and reject surviving references to winning deletions before compilation. |
| 5 | Fixed: event and timeline templates prefer mod metadata over base metadata, honoring the selected source and matching the event or moment. |
| 6 | Fixed: reconstruct room instances and tiles in the selected mod's order, then fall back to other sources and the base. |
| 7 | Fixed: order added assets by the first introducing mod and its original asset index. |
| 8 | Skipped: `CompileGroup` already registers locals per name/slot and replaces instruction references during linking; same-named locals in different script slots survive. |
| 9 | Fixed: intern imported strings in the shared destination table, import multi-mod texture pages through selected payloads, and select a font texture before importing it. |
| 10 | Fixed: recursively resolve link targets and their parents with cycle detection; patch-cache/report output safeguards use the same resolver. |
| 11 | Fixed: allow installation over the explicitly selected base only after verified backup; retain protection for mod inputs and provide a missing-archive diagnostic. Subsequent builds use the backup as base or restore first. |
| 12 | Fixed: permit verified replacement of previous outputs, preserve them on failure and during dry runs, and protect inputs, installed archives, markers and backups. |
| 13 | Fixed: missing structured resources, code and variable imports produce contextual errors naming the dependency and a recovery step. |

Regression coverage is in `BuildReviewTests.cs` and `BuildingTests.cs`. It exercises
post-deletion merges, qualified resolutions, three-mod hunk choices, retained edits,
winning deletion failures, ancestor references, action metadata, room order, introduction
order, local slots, shared strings, unused pages, merged font textures, nested symlink
targets and cycles, repeat builds, dry runs, failed-output preservation, installation
into the base directory, and missing dependencies.

Validation on branch `experimental`:

- `dotnet build -c Release`: zero warnings and zero errors.
- `dotnet test -c Release`, with both real-file variables set: 123 passed, zero skipped, zero failed (4 min 28 s).
- `env -u JAMM_ORIGINAL -u JAMM_MODS_DIR dotnet test -c Release --no-build`: 119 passed, 4 expected real-file skips, zero failed.
- `git diff --check`: passed; the pinned submodule remains unchanged.

Real-file tests built all three individual mods, merged both load orders,
reloaded/verified the results, round-tripped installation/restore in a temporary
game directory, and confirmed unchanged input hashes. No in-game runtime test was
possible; desktop validation with the legacy executable is still required.
