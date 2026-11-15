# Task: Phase 1 — diff and patch files

Read `AGENTS.md` and `docs/legacy-version.md` first. Base for all real tests: the legacy 2023 build
(`JAMM_ORIGINAL`), mods in `JAMM_MODS_DIR`.

## Goal
`cgfmm diff <original> <mod>` finds every difference between a mod and the original per asset kind,
compares code as **decompiled GML text**, and saves the result as a JSON patch file so a mod never
has to be analysed twice. Phases 2 (conflicts, 3-way merge) and 3 (build) will consume these patches —
design the format for that.

## Command
```
cgfmm diff <original.win> <mod.win> [--name <mod name>] [--out <file>] [--force] [--json-summary]
                                     [--show <asset name>] [--verbose]
```
- Default output: `<mod file name without .win>.cgfmm-patch.json` in the current directory.
- If the output exists and its `base.sha256` + `mod.sha256` + `formatVersion` match, reuse it and say so
  (`--force` recomputes).
- Prints a summary table per asset kind: added / changed / removed, plus the patch path.
- `--show <asset>` prints a readable unified diff (GML for code; field changes for other kinds) of one asset.
- Exit codes: 0 ok, 1 read/analysis error, 2 a file is YYC (not comparable).
- Refuse clearly if base and mod differ in GameMaker version or bytecode version.

## Asset kinds and what "changed" means
Match by name (strings by value). Reuse the existing fingerprints to skip identical assets cheaply.
| Kind | Record in patch |
|---|---|
| code | decompiled GML of base and mod (full text) for changed; mod GML for added; name for removed. Decompile only entries whose fingerprint differs or that are added. Also record locals count / arguments count. Child code entries (if any) by name. |
| scripts | name → code entry name |
| objects | field-level changes: sprite, mask, parent, depth, visible/solid/persistent, physics flags; events added/removed/changed as (event type, subtype) → code entry name |
| rooms | field changes (size, speed, flags, views, backgrounds); instances added/removed (object, x, y, scale, rotation, creation code name) — match instances by (object, x, y) rather than instance id; tiles summary |
| sprites | dimensions/origin/bbox/flags changes; frame count; which frames' pixels changed (by frame index, with per-frame content hash). Pixel data itself is NOT stored in JSON (see below) |
| texture pages | added/changed by index with content hash |
| sounds | flags/volume/pitch/type/file name changes; audio content hash |
| fonts, backgrounds, paths, timelines | field-level changes in the same style |
| strings | added / removed values (no "changed", strings are values) |
| variables, functions | added / removed names (with instance type for variables) |

## Patch file format (JSON, versioned)
```jsonc
{
  "formatVersion": 1,
  "tool": "cgfmm <version>",
  "createdUtc": "...",
  "mod":  { "name": "...", "fileName": "the-abyss-beta.win", "sha256": "...", "gen8Timestamp": 1692207312 },
  "base": { "fileName": "data.win", "sha256": "...", "gen8Timestamp": 1692207312, "gmVersion": "...", "bytecode": 16 },
  "warnings": [ ... ],
  "changes": {
    "code":    { "added": [ {...} ], "changed": [ {...} ], "removed": [ "name", ... ] },
    "objects": { ... }, ...
  }
}
```
- Binary payloads (pixels, audio, texture pages) are referenced by content hash; Phase 3 reads them from
  the mod file identified by `mod.sha256`. Document this in `docs/patch-format.md`.
- Deterministic output: stable ordering (by name), so re-running produces an identical file except `createdUtc`.
- Patch files contain decompiled game code: add `*.cgfmm-patch.json` to `.gitignore`; never commit them.
- Model the patch with C# records in `Jamm.Core/Patching/` and serialize with System.Text.Json (source-generated
  context preferred). Phase 2 must be able to load patches without UndertaleModLib.

## Implementation notes
- Decompiler: UndertaleModLib 0.9 uses Underanalyzer (`GlobalDecompileContext` + `DecompileContext`
  with default settings). Create one global context per file and reuse it. Catch per-entry decompile
  failures: record a warning and fall back to the disassembly text so the entry still diffs.
- Unified diff: DiffPlex (Apache-2.0, GPL-compatible) is allowed; or a small Myers implementation in Core.
  Phase 2 needs line-based 3-way merge on top of it — keep the diff API line-based and reusable.
- Keep `Jamm.Core` UI-free; the CLI formats tables. Report progress (decompiling n/m) via a callback.
- Known quirk: Colour Expansion was built from a build 5 minutes older than the base (differs in one code
  entry). Do not special-case it; just make sure the diff is correct.

## Tests
- Unit tests for the diff engine with synthetic models (objects events, room instances, strings, code text).
- Patch serialization round-trip and determinism test.
- Real-file test (skipped without env vars): diff the base against each mod; counts per kind must match
  the `compare` command's changed/added/removed for code, sprites, objects, rooms, sounds.

## Docs
- README: add `diff` with examples and update the roadmap (Phase 1 done).
- `docs/patch-format.md` (format, field meanings, binary references, versioning rules).

## Verify before finishing
- `dotnet build -c Release` zero warnings; `dotnet test` green with and without the env vars.
- Run `cgfmm diff` for all three mods against the base (write patches to /tmp/cgfmm-patches/), run each twice
  to check caching, and run `--show` on one changed code entry per mod. Put the summary tables and timings
  in your final message (no GML content in the message beyond ~10 lines of one example).
- Do NOT commit. Grep tracked/untracked files for `/home/` and `.cgfmm-patch.json` before finishing.
