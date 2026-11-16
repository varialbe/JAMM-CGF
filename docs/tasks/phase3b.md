# Task: Phase 3b — keep numeric asset references correct in merged builds (blocker)

Read `AGENTS.md`, `docs/building.md`, `docs/conflicts.md` and the Phase 3/4 code. Branch: `experimental`.

## The bug (found by the orchestrator review of c741b36)
GameMaker 1.4 code stores many asset references as **plain integer indices**. The decompiler turns typed
contexts (e.g. `instance_create(x, y, obj)`) into names, but untyped ones stay numbers, for example the fish
catalog: `object[2, 58] = 641;`. Those numbers are indices into the *file the code came from*.

In a multi-mod build the indices change:
- Cave Expansion removes three base objects, so every later base object index shifts in the merged file.
- Each mod appends its new objects starting at the same base count, so their indices overlap.

Measured on the merged build Abyss → Cave → Colour: index 641 is `Overbite` in Abyss, `Fatneck` in Cave,
`Brushick` in Colour and `Shados` in the merged file. Base object 69 (`Bombat`) is index 68 in the merged file,
yet unchanged base catalog code still says 69. Between 573 and 677 object indices per input point to a different
object in the merged file, and 51 to 98 sprite indices per mod. The archive loads and passes `verify`, but the game
logic references the wrong assets. Single-mod builds hide the bug because the indices happen to line up.

## Required behaviour
1. **Index stability for base assets.** Never delete an indexed base asset (objects, sprites, sounds,
   backgrounds, paths, scripts, fonts, timelines, rooms, shaders if present) from the merged archive, even when a mod
   removes it. Keep it at its base index, report `INFO: retained <family>/<name> (removed by <mod>) so numeric indices
   stay valid`. Base code then stays correct without rewriting.
2. **Remap mod-space indices to merged-space indices** in every code entry that comes from a mod (changed, added
   or merged hunks). For each mod and family, build `modIndex → name → mergedIndex`. Preferred mechanism: make the
   decompiler emit **names** for asset-typed variables and arrays, then let the compiler resolve the names against
   the merged data, so no number rewriting is needed. UndertaleModTool 0.9 / Underanalyzer supports game-specific
   type definitions (look at `GameSpecificData` / `GameSpecificResolver` / the decompile settings in the submodule;
   do not edit the submodule, register definitions from our code). Fallback if that is not possible: token-level
   rewrite of integer literals in typed contexts only. Never rewrite arbitrary numbers.
3. **Typed containers for Cat Goes Fishing.** Find the variables and arrays that hold asset indices (at least the
   catalog's `object` array; check the BoatData, RodData, spawn and shop code, `sprite`/`spr` arrays, sound ids).
   Do it with an analysis step: for every container that receives integer literals, check across base and mods whether
   its values resolve to existing assets of one family whose names match related data (e.g. catalog `name[a,i]` vs
   the object name at `object[a,i]`). Store the result as a small checked-in definition file
   (`src/Jamm.Core/Building/cgf-asset-types.json`: container name → asset family, plus a one-line reason). Only
   identifiers, no game text or code. Add `cgfmm check --asset-literals` (or similar) that lists untyped integer literals
   in mod code that fall in a mod's added/shifted index range, so missing containers can be found later.
4. **Semantic reference verification** (new step in `ArchiveVerifier` / build): for every mod in the build and every
   mod-originated code entry, decompile it once with that mod's own data and once from the merged archive, and resolve
   every asset reference (named, or numeric in a typed container) to `family/name`. The resolved names must match,
   except where a conflict decision intentionally picked another mod's version of that hunk. Any mismatch fails the
   build with the code entry, container, expected and actual asset name. Do the same for base code: base references in
   unchanged base entries must resolve to the same names as in the base file.
5. Dangling references: if a mod's code references (by index) an asset that is not in the merged build, fail with a
   clear message instead of silently pointing at another asset.

## Also fix (lower priority, from the orchestrator's review)
- `BuildFiles.RefuseLink` refuses a game folder whose *parent* directory is a symlink (e.g. a game drive mounted
  through a link). Refuse only when `data.win`, the backup or the marker are links themselves; otherwise use the
  resolved path. Keep the input-alias protection.
- Phase 2 `check`: an asset changed by only one mod (binary "unsupported content", e.g. out-of-range page items) must
  be a `WARNING`, not a `CONFLICT`. Group repeated hunk rows of the same code entry into one row with a hunk count in
  the terminal table (keep per-hunk details in `--json` and `--details`).
- Docs (`docs/conflicts.md`, README): explain in plain words that The Abyss and Cave Expansion both claim the same
  catalog/boat slots (e.g. boat 8, catalog slots `[2,58]` onwards). "keep both" cannot keep both when the slots are
  identical. The later mod wins those slots unless the user picks another. Automatic slot renumbering is future work.

## Tests
- Unit tests with synthetic data: index maps for removal + overlapping additions; remap of a typed array assignment;
  untyped numbers left alone; semantic verifier catches a deliberately wrong index.
- Real-file tests (skipped without env vars): build Abyss → Cave → Colour and Colour → Cave → Abyss; the semantic
  verification passes; spot assertions: the catalog entry named "Overbite" (Abyss) and the entry for Cave's first new
  fish reference the objects with those names in the merged file; base `Bombat` references still resolve to `Bombat`.
  Single-mod builds still reproduce the mod's GML semantically (names, not raw numbers).

## Verify before finishing
- build zero warnings, tests green with and without env vars.
- Rebuild both 3-mod orders into `/tmp/cgfmm-build/` and report: retained base assets, number of remapped references
  per mod, the semantic verification result, decisions, timings. Re-run the index comparison described above (object and
  sprite names per index of each mod vs merged) and report the remaining mismatches. They must all be explained
  (retained/removed assets or intentional conflict decisions).
- Do NOT commit. No game content or personal paths in tracked files.
