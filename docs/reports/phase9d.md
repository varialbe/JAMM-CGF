# Phase 9d — evidence-based game detection

Implemented on `ui/browse-mods`, without commits, pushes, or git configuration changes. The pre-existing README.md edit was left untouched.

## Behavior

- Green checks require a matching SHA-256 and size. The built-in original is manifest 9039707068725406600: `dbb2e5c4b788358d0c6465d3005b2e9d46c4c540e423b064d7a5a3629286d2c1`, 44,572,166 bytes. No additional original hashes were invented.
- Bytecode-16, build 1773-or-newer archives from the August 2023 legacy era receive yellow question marks unless verified. Older timestamps in that era and files no larger than the known original are described as older legacy builds; larger or later unverified copies are described as modified or other builds. This is a compatibility indication, not proof of original contents.
- Earlier builds and timestamps, unsupported bytecode, YYC/empty CODE, malformed files, and JAMM-built archives without a verified backup receive red crosses and actionable explanations.
- Either JAMM marker disqualifies `data.win`. A supported `data.win.cgfmm-original` is returned instead, read-only. Copy creation puts that backup into the new game's `data.win` and omits stale JAMM markers; the source is never changed. Use rechecks classification and rejects a source whose inspection redirects to a backup.
- Discovery reads chunk headers and GEN8 metadata without loading the full UTML model. Only compatible candidates whose size matches an injected/built-in original size are hashed. Header/hash progress is shown; cancellation and scan limits remain active.
- Setup sorts green before yellow, preselects only green, and groups red results in the initially collapsed “Other folders found (not usable)” expander. Yellow requires explicit selection. Manual folder selection uses the same classification and grouping.

## Validation

- `dotnet build -c Release -m:1 --no-restore`: passed, zero warnings and errors. Single-worker MSBuild was necessary because the default parallel invocation exited unsuccessfully without diagnostics in this sandbox.
- `dotnet test -m:1 --no-restore`: both test hosts aborted before executing tests because the sandbox denies VSTest's local TCP socket (`SocketException (13)`).
- A temporary, external `/tmp/phase9d-runner` reflection harness invoked the actual xUnit fact/theory methods for Phase9bTests, Phase9cTests, and Phase9dTests in process: **30 passed, 0 failed**. This bypasses the test-host socket, not the assertions. It does not replace a full standard suite run outside the sandbox.
- Synthetic fixtures cover the known original through injected hash/size evidence, older August legacy, later unverified legacy, a 56,078,502-byte unmarked copy, 2017/2019/2022 builds, YYC, JAMM markers with supported/missing/unrecognized backups, read-only input preservation, cheap-check hash avoidance, and green-only preselection/grouping. Existing setup tests now explicitly select yellow fixtures.
- `git diff --check`: passed. No real game files were required or added. No external submodule source was edited. Desktop visual verification and the standard full test suite remain for an environment permitting the test host/UI.
