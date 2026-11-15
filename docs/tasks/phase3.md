# Task: Phase 3 — build and install a merged data.win

Read `AGENTS.md`, `docs/patch-format.md`, `docs/conflicts.md` (from Phase 2) and `docs/legacy-version.md`.
Branch: `experimental`.

## Commands
```
cgfmm build --base <data.win> --order <mod1>,<mod2>,... [--out <merged.win>] [--resolve <file.json>] [--install <game dir>] [--dry-run]
cgfmm restore --game <game dir>
cgfmm verify <data.win>
```
- `--order`: load order; mods may be `.win` files or patch files (computed/reused as in Phase 2).
- Default `--out`: `./build/data.win` (never the base path, never a mod path — refuse with a clear message).
- Conflicts: the later mod wins, unless `--resolve` (JSON: asset name → winning mod name, or for code
  `"keep-both"` for list/insertion conflicts) says otherwise. Print every decision taken.
- `--dry-run`: run the merge and print what would be written, write nothing.
- `--install <game dir>`: copy the merged file to `<game dir>/data.win`. **Before overwriting**, if
  `<game dir>/data.win.cgfmm-original` does not exist, copy the current `data.win` there and verify the copy
  by SHA-256. Never overwrite or delete that backup. Refuse if the game dir's `data.win` is neither the
  original base (by hash) nor a previous cgfmm build (marker file `data.win.cgfmm-build.json` with hashes).
- `restore --game <dir>`: copy `data.win.cgfmm-original` back over `data.win` (verify hash), keep the backup.
- After writing, **always reload the output with UndertaleModLib** and check: loads without exception,
  same GM/bytecode version, every asset from the build plan present, code entries compile/decompile, string
  and texture references valid. Report success/failure clearly. `cgfmm verify` does the same for any file.

## Applying patches
- Start from a fresh load of the base. Apply each mod in load order. Use the Phase 2 merge engine for code:
  for code changed by several mods use the three-way merged GML; compile merged/changed/added GML with
  UndertaleModLib's compiler (`CompileGroup` / the 0.9 API) into the base data. Compilation errors → stop
  with the code entry name and the compiler message, write nothing.
- Binary assets (sprites, texture pages, sounds, fonts, backgrounds): read the payloads from the mod file
  identified by `mod.sha256` (the mod `.win` must be available; say clearly which file is missing). Import
  texture page items / embedded textures / audio as new entries, keep references consistent. Prefer copying
  the mod's texture pages that contain the needed items over repacking, as long as references stay correct.
- Objects/rooms/paths/timelines/fonts: apply field-level changes; added assets in the mod's original order.
- Strings/variables/functions: add missing ones; **never remove** base strings/variables/functions (base bytecode
  may reference them), even if a mod removed them — report it as info.
- Collision events: resolve the other object by name in the merged data.

## Tests
- Unit tests on small synthetic data where possible (apply field changes, resolve rules, backup logic with temp dirs).
- Real-file tests (skipped without env vars): build each single mod alone (base + one mod) and check the
  result is equivalent to the mod for code (same decompiled GML per entry) and asset counts; build all three
  in two orders; `verify` passes; backup/restore round-trip in a temp game dir copied from the legacy folder.

## Docs
README: `build`, `restore`, `verify`, resolve file format, safety rules (backup, never overwrite originals),
roadmap update. `docs/building.md` with details and known limitations.

## Verify before finishing
- build zero warnings, tests green with and without env vars.
- Produce merged builds for all three mods (orders Abyss,Cave,Colour and Colour,Cave,Abyss) into `/tmp/cgfmm-build/`,
  run `verify` on each, and report sizes, asset counts, decisions and timings. The game cannot be launched on this
  machine: say clearly that an in-game test on the desktop is still required.
- Do NOT commit. No game content or personal paths in tracked files.
