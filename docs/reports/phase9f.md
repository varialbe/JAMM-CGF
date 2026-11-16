# Phase 9f — Antiquity textures and detection polish

Implemented on `ui/browse-mods`; no commits, pushes, or git configuration changes.

## Antiquity investigation and handling

Read the 56,078,502-byte cached archive under `/tmp/jamm-dlprobe2` read-only.
SHA-256: `e1b5ebdb257e4143850763c3a06bb96328fe6449dcd31d3109bfdc39d7d81a28`.
It contains 1,457 TPAG items and 148 embedded texture pages. TPAG item 1297
has source/target/bounding dimensions 100×100, origin (0,0), and a serialized
signed page ID of **-1**. This is an explicit null page, not an out-of-range
positive reference or a lost embedded image. No sprite, background, or font
references it.

Reference verification now records texture items reached through resource graphs.
Unused null-page items with nonzero geometry are preserved without inventing
pixels, assigning another page, deleting the item, or changing its geometry.
Verification reports a warning for these items. Existing resource membership,
texture bounds, code compilation, and semantic reference checks remain active.
A referenced null-page item with pixels still blocks output: its exact reason
is classified as yellow **Tool limitation**, since JAMM cannot safely reconstruct
its texture. Analyzer revision 4 invalidates cached verdicts from the old rule.
The existing UI maps this verdict to its yellow question icon.

Real analysis against the verified 2023-08-16 original
(`dbb2e5c4b788358d0c6465d3005b2e9d46c4c540e423b064d7a5a3629286d2c1`)
now reports **Compatible**. The temporary merged archive reloaded and passed
reference, decompile/compile, and semantic checks for all 1,929 code entries.
Reported additions: 43 fish species, 81 sprites, 76 game objects, and 17 changed
game code entries. The cached Antiquity input hash was unchanged afterward.
All probe outputs and analysis storage were under `/tmp`; no install was performed.

## Detection

The lower legacy timestamp boundary is now 1692057440. The actual public
2023-08-15 manifest `2811289735487427888` archive was checked: it is usable,
yellow (`#A66B00`), and described as an older legacy build with differences.
It is not promoted to a hash-verified original. Earlier timestamps, unsupported
bytecode/builds, and YYC archives retain their rejection checks.

Shared candidate ordering sorts by suitability, then prefers plain archives over
`data.win.cgfmm-original`, then sorts paths deterministically. Steam detection,
mounted-folder search, setup results, and manually inserted results use this order.
Automatic selection therefore prefers a plain verified original over a verified
backup, even when the backup path sorts earlier alphabetically. Unverified yellow
builds still require explicit selection.

## Validation

- `dotnet build -c Release -m:1`: passed with zero warnings and zero errors.
- `dotnet test -c Release -m:1`: both test projects built, but VSTest aborted
  before executing tests because the sandbox denied its local TCP listener
  (`SocketException (13): Permission denied`).
- A temporary console harness directly invoked the actual xUnit test methods,
  retaining their assertions: both synthetic null-page cases, existing two-mod
  pixel/payload import coverage, all Phase 9d header classifications, the exact
  public timestamp and immediately preceding timestamp, hash/backup recovery,
  no-hash checks, larger modified archives, existing preselection behavior, and
  the new plain-original-versus-backup ordering test. All passed.
- Synthetic archives verify unused geometry survives a full build and reload,
  referenced geometry fails without publishing output and yields Tool limitation
  in content analysis, and base/mod input hashes remain unchanged.
- `git diff --check`: passed. External submodules were not edited; no game data
  or extracted game assets were added to the worktree.
