# Phase 22d Report — Fish API (`FishRuntime`) + JAMM ModKit

## Summary of Changes

### 1. Fish Runtime (`src/Jamm.Core/Building/FishRuntime.cs`, `ArchiveBuilder.cs`)
* Implemented `FishRuntime.PreparePlan` and `FishRuntime.Inject`, wired into `ArchiveBuilder.Build` and `ArchiveBuilder.Apply`.
* Activates whenever any code entry in the build calls or defines `jamm_fish_register`.
* Injects `jamm_fish_register(key, object, size_tab, name, desc, x1, x2, y1, y2, count [, desc_caught, desc_seen, desc_hidden, desc_n, desc_e, exotic])`:
  * Counts existing `instance_number(object)` and spawns any missing instances inside `[min(x1,x2)..max(x1,x2)] x [min(y1,y2)..max(y1,y2)]`, initializing `start_x`, `start_y`, `TRUE_START_X`, `TRUE_START_Y`, `nothungry_x`, `nothungry_y`.
  * Records `object -> name` in `CatalogData.jamm_fish_names` and registers the fish via `jamm_catalog_register`.
* Patches `gml_Script_fish_object_to_name` (when `CatalogData` exists) to check `CatalogData.jamm_fish_names` before the base string-replacement fallback, so namespaced ModKit fish objects (`jmk_<id>_<key>`) resolve to their display names (`"Goldie"`, `"Glowfin"`) in `fish_was_caught`, `fish_appraise`, `SaveManager_Other_13`, and quests.
* Standalone `jamm_api_init` coordination:
  * If `gml_Script_jamm_api_init` already exists in the plan or baseline (e.g. from Phase 22b's `ItemsRuntime`), `FishRuntime` does nothing extra for init dispatch.
  * Otherwise, `FishRuntime` generates `gml_Script_jamm_api_init` calling all `jamm_init_*` scripts in deterministic load-then-name order and prepends a `jamm_api_done`-guarded call at the top of `gml_Object_SaveManager_Step_0` (before `if (!loaded)`).
* Ensures `CatalogRuntime` scripts (`jamm_catalog_*`) and `jamm_catalog_legacy` markers are present when `CatalogData` exists, even if no mod edited `CatalogData_Create_0` directly.

### 2. ModKit Package Loader, Sprite Importer, and Builder (`src/Jamm.Core/ModKit/`)
* `ModKitPackage.cs`:
  * Parses and validates `jamm-mod.json` (`id`, `name`, `version`, `author`, `description`, `fish`, `boats`, `hats`, `upgrades`), rejecting invalid keys (`^[a-z0-9_-]+$`), duplicate keys per type, path traversal outside the package folder, missing files, invalid PNG headers, and frame counts that do not evenly divide strip width. Error messages include `jamm-mod.json`, the JSON path, and remediation steps.
  * `ModKitPackage.Scaffold(directory, template)` scaffolds `fish`, `upgrade`, `boat`, and `hat` starter folders with original generated PNGs and example `.gml` hooks.
* `ModKitSpriteImporter.cs`:
  * Imports single-frame PNGs and horizontal multi-frame sprite strips via `ImageMagick` + `UndertaleModLib.Util.TextureGroupPacker` onto new texture pages, computing opaque bounding boxes and 1-bit `CollisionMasks` (`NewMaskEntry`) for fish and hats, centered origins for fish/boats/hats (unless overridden by `origin`), and `(0, 0)` origin for upgrade icons.
* `ModKitBuilder.cs`:
  * Opens the legacy base `data.win` read-only, creates namespaced sprites/objects/hook scripts (`jmk_<id>_<key>…`) and `jamm_init_<id>`, adds `return -1;` placeholder stubs only for the `jamm_*_register` functions called by the package, compiles via `CompileGroup`, writes atomically to a new `.win`, and runs `ArchiveVerifier.Verify`.
  * Fish objects inherit `FishSmall`/`FishMedium`/`FishLarge`/`FishHuge`, override `Other_10` for path-free swimming (avoiding `Fish_Other_10`'s `path_thrust_update()` dependency, while calling `fish_fleelarge`/`fish_fleehuge`/`fish_lunge`), and attach `create`, `step` (`Step_2`), `caught` (`Other_19`), and `draw` (`Draw_0`) hooks.
  * Boats inherit `Boat` (`Depth = -20`); hats inherit `Gear` (`Depth = -4`, `effect` hook in `Other_15`).

### 3. CLI & Management Integration (`src/Jamm.Cli/`, `src/Jamm.Core/Management/ModLibrary.cs`)
* Added `cgfmm modkit new <dir> [--template fish|upgrade|boat|hat]`, `cgfmm modkit check <dir>`, and `cgfmm modkit build <dir> [--base <data.win>] [--out <file.win>]` in `ModKitCommand.cs` and wired `modkit` in `Program.cs`.
* Extended `ModLibrary.Add` so `cgfmm add <folder>` validates, builds against `config.base`, and imports a ModKit package folder directly into the mod library.

### 4. Examples & Documentation
* `examples/modkit/make-art.py`: stdlib-only Python generator for original pixel art PNGs.
* `examples/modkit/example-fish/`: **Goldie** (small shallow fish) and **Glowfin** (night-only medium fish pulsing `image_blend` and darting from close lures in `glowfin_step.gml`).
* `examples/modkit/showcase-upgrades/`: **Turbo Reel**, **Deep Diver**, **Iron Line**, **Fish Magnet**, and **Golden Touch** (with idempotent `apply` hooks and once-per-catch `step` scaling documented in each `.gml` header).
* Documentation: `docs/modding/README.md`, `docs/modding/getting-started.md`, and `docs/modding/modkit-reference.md`.

---

## Key Design Decisions

1. **Path-free fish idle swimming (`Other_10`)**: `Fish_Other_10` unconditionally calls `path_thrust_update()`, which reads `PATH_THRUST_PHASE`, `PATH_THRUSTS`, `PATH_MIN_SPEED`, and `PATH_THRUST_SPEED`. ModKit fish override `Other_10` with path-free swimming (retaining `fish_fleelarge`/`fish_fleehuge` and `fish_lunge`) and still initialize `PATH_*` fields in `Create_0` as a safety net.
2. **Display-name mapping for namespaced fish (`fish_object_to_name`)**: Base `fish_was_caught` and `fish_appraise` derive catalog lookup strings from `fish_object_to_name(object_index)`. Using `with (CatalogData)` and `CatalogData.jamm_fish_names` inside `FishRuntime` resolves `jmk_<id>_<key>` to the author's display name (`"Goldie"`, `"Glowfin"`) without altering base fish name behavior.
3. **Underanalyzer/SemanticVerifier compatibility in `FishRuntime` GML**: `variable_instance_exists(CatalogData, ...)` passes an object index to an argument typed as `Id.Instance` in Underanalyzer's `gamemaker.json`, which decompiles back as a raw numeric ID instead of `CatalogData`. Using `with (CatalogData) { if (variable_instance_exists(id, ...)) ... }` preserves the `CatalogData` symbol through compile/decompile round-trips so `ArchiveVerifier` semantic verification passes cleanly.
4. **Upgrade cost scale**: Verified `calculate_upgrade_cost` multiplies `UpgradeData.cost[id]` by `RodData.cost[rod_id]`. `showcase-upgrades` uses realistic multipliers (`0.25`–`0.80`), and `modkit-reference.md` documents the multiplier semantics.
5. **Conflict-free identical placeholders**: Because every ModKit mod emits the same `return -1;` body for unused placeholders (`jamm_fish_register`, etc.), `ConflictAnalyzer` classifies overlapping placeholders as `INFO` rather than `CONFLICT`, allowing multi-mod `cgfmm check` and `cgfmm build` with zero manual resolutions.

---

## Verification & Test Results

* `dotnet build -c Release`: **0 warnings, 0 errors** across all projects (`Jamm.Core`, `Jamm.Cli`, `Jamm.ModMaker`, `Jamm.Gui`, `Jamm.Core.Tests`, `Jamm.Gui.Tests`).
* `dotnet test tests/Jamm.Core.Tests -c Release --no-build`: **316 passed, 0 failed, 8 skipped** (the 8 opt-in real-file tests; 10 new synthetic tests in `FishRuntimeTests` and `ModKitTests` covering `FishRuntime`, `ModKitPackage` validation/scaffolding, `ModKitBuilder`, multi-mod merging, and `cgfmm modkit` / `cgfmm add <dir>` CLI).
* Real-base opt-in verification against `/home/varialbe/cgf-files/legacy/2023-08-16_manifest-9039707068725406600/data.win`:
  * `cgfmm modkit check` and `cgfmm modkit build` built and verified `example-fish.win` and `showcase-upgrades.win` into `/home/varialbe/wt/modkit-out/` (`1837` and `1835` code entries decompiled/recompiled).
  * `cgfmm check` on both built `.win` files reported **0 conflicts, 0 warnings**.
  * `cgfmm build` merged both into `/home/varialbe/wt/modkit-out/merged-both.win` (`1862` code entries verified, `199` catalog fish verified, standalone `jamm_api_init` calling both `jamm_init_*` scripts).
  * `ModKitTests.RealBaseBuildsAndMergesBothExamplePackages` (`JAMM_ORIGINAL` + `JAMM_MODS_DIR` set): **1 passed, 0 failed**.

---

## What Needs In-Game Verification (Once 22b & 22c Are Merged)

* **Fish (`example-fish`)**:
  * Goldie and Glowfin spawn in `NewWorld` at the configured water depth, swim around `start_x/start_y` facing head-first, bite/reel normally, increment Fishalog counts/descriptions on catch, and persist across save/load in `[JAMM_Catalog]`.
  * Glowfin dims and retreats deeper during daytime and pulses cyan/lime at night while darting away when a moving lure approaches within 120 px.
* **Boats & Hats (`ModKitPackage` / `ModKitBuilder`)**:
  * Custom ModKit boats and hats require Phase 22b (`jamm_boat_register` and `jamm_hat_register`) to replace the `return -1;` placeholders at merge time; verify in-game rendering, card preview, boat capacity/speed, Hatalog entry, and `Other_15` hat effect.
* **Upgrades (`showcase-upgrades`)**:
  * Requires Phase 22b + 22c (`jamm_upgrade_register`, `obj_jamm_api` `Begin Step` runner, and `Rod_Other_10` `apply` invocation) to replace the `jamm_upgrade_register` placeholder; verify all 5 icons (`50x50` origin `0,0`), stack caps, idempotent `Rod.upgrades[]` / `Fish.base_lunge_*` effects, and Golden Touch's once-per-catch `value` scaling in-game.
