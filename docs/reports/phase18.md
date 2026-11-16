# Phase 18 Report — Mod Sanitizer: Strip Third-Party Performance Hacks, Keep Content

## Summary

Implemented deterministic hunk-level mod sanitization (`ModSanitizer`) in `Jamm.Core` to strip unpredictable third-party performance hacks from imported mods while keeping all gameplay, fish, item, catalog, room, and upgrade content intact:

1. **`ModSanitizer` (`src/Jamm.Core/Patching/ModSanitizer.cs`)**:
   - Works strictly on portable `ModPatch` objects (never modifies input `.win` archives).
   - Classifies changed-code hunks deterministically into `{timing, tick-throttle, distance-cull, fps-adaptive, debug-overlay, content}` and `unclassified` (kept without guessing when ambiguous).
   - Reverts non-content hunks to the base version at hunk granularity (and unwraps outer `tick-throttle`/`distance-cull` gates if content statements are nested inside them) so content in the same entry always survives.
   - Removes helper objects (`fpsController`), helper event code (`gml_Object_fpsController_*`, `gml_Object_CaveRock_Step_0`), room instances of removed helper objects, room `/speed` timing overrides (`NewWorld /speed: 30 -> 1`), and unreferenced helper variables/functions (`check_timer`, `checkTimer`, `target_fps`, `target_frame_time`, `frame_duration`, `now`, `game_set_speed`, `game_get_speed`, `get_timer`) when nothing in kept content references them.
   - Identifies JAMM's own ModMaker mods (`Smooth`, `HD` / `HD Lite`, `Perf`) by their `obj_jamm_*` objects (`obj_jamm_smooth`, `obj_jamm_hd`, `obj_jamm_perf`) and never sanitizes them (`IsJammMod = true`).
2. **Library Storage & Per-Mod Advanced Switch (`src/Jamm.Core/Management/ModLibrary.cs`, `ManagerModels.cs`)**:
   - Every imported mod (both online downloads and local `Add` imports) stores `<id>.sanitized.cgfmm-patch.json` and `<id>.sanitizer.json` next to `<id>.cgfmm-patch.json` in `patches/`. Existing library entries lazily generate/refresh their sanitized patch and report when `ModSanitizer.Version` changes.
   - Added `KeepAuthorPerformanceChanges` (default `false`) to `ManagedMod` and `ModLibrary.SetKeepAuthorPerformanceChanges(query, keep)`, wired to the Advanced mode per-mod checkbox `"Keep the author's performance changes"`.
3. **UI (`src/Jamm.Gui/ModListRow.axaml`, `MainWindow.axaml`, `ModRow.cs`, `MainViewModel.cs`, `BrowseViewModel.cs`)**:
   - Displays `"JAMM removed N performance changes"` on mod rows/cards when `RemovedPerformanceCount > 0`, paired with an `info` button opening a `Flyout` listing every removed change (`[category] entry: summary`).
   - Shows the per-mod `"Keep the author's performance changes"` switch in Advanced mode (disabled for JAMM's own `obj_jamm_*` mods).
4. **Compatibility DB & Cache Versioning (Note for Orchestrator)**:
   - Archive SHA-256 and `ModOrigin` provenance remain unchanged; sanitizing is a deterministic function of `(patch, ModSanitizer.Version)`.
   - `ModSanitizer.Version` is `1`, and `ModFactAnalyzer.Version` has been bumped from `4` to `5` (`src/Jamm.Core/Browsing/ModFacts.cs`).
   - **What the compat DB key needs**: The SQLite compatibility database (`CompatibilityScan.cs`) already uses primary keys `checks(base_sha256, mod_sha256, analyzer_version)` and `source_files(base_sha256, provider, file_id, analyzer_version)` where `analyzer_version = ModFactAnalyzer.Version` (`5`), and `SharedCompatibility` validates `AnalyzerVersion == 5`. Re-running `cgfmm compat-scan` will compute sanitized facts under `analyzer_version = 5` and export `v1/<baseHash>/*.json` with `AnalyzerVersion: 5` without any schema migration needed (or if you add a separate column/key outside `analyzer_version`, key on `(base_sha256, mod_sha256, analyzer_version, sanitizer_version)` with `analyzer_version = 5, sanitizer_version = 1`).

---

## Explicit Classification Rules

| Category | Pattern over Decompiled GML / Structured Patch Fields | Action |
|---|---|---|
| `timing` | Writes to `delta`, `delta_fixed`, `delta_previous`, `time_scalar`, `time_scalar_last`, `DELTA_INSTANT`, `room_speed`; calls to `game_set_speed` / `game_get_speed`; `get_timer()` / `target_fps` / `target_frame_time` busy-wait frame limiters; room `/speed` field changes | Revert hunk / field to base; remove unreferenced frame-limiter objects (`fpsController`) |
| `tick-throttle` | Counter increments (`x++`, `x += 1`), modulo/counter gates (`if (x >= N)`, `if (x == N)`, `if ((x mod N) == 0)` with `N >= 2`), and counter resets (`x = 0`) around logic; helper counter initializations in Create events | Revert hunk (or unwrap gate if content is nested inside) to base |
| `distance-cull` | `point_distance` checks to `Camera` (`Camera.x`, `Camera.y`) or `view_xview`/`view_yview` gating `visible = false/true`, `exit`, `state = 12/0`, or draw/step calls; `do_zoning = 1` in `FishThinner` | Revert hunk (or unwrap gate) to base; remove standalone cull event handlers (`gml_Object_CaveRock_Step_0`) |
| `fps-adaptive` | Reads of `fps` or `fps_real` in conditions/expressions driving `cull_trigger` or adaptive culling behaviour | Revert hunk to base |
| `debug-overlay` | `draw_text*` calls formatting `"FPS:"`, `"X:"`, `"Y:"`, `string(fps)`, `string(fps_real)`, `string(Camera.x)`, `string(Camera.y)`, plus dedicated `draw_set_*`/`draw_x`/`draw_y` setup lines in the same hunk | Revert hunk to base |
| `content` | All fish, boat, rod, lure, upgrade, catalog, room placement/dimension, sound, sprite, save, and UI changes with no performance-hack primitives | Keep |
| `unclassified` | Any hunk mixing performance primitives (`fps`, `game_set_speed`, `instance_deactivate_*`, `cull_trigger`, `timing` writes, `Camera` distance checks) with non-base statements that cannot be cleanly separated | Keep and report in `Report.Unclassified` (never guess) |

---

## Real-File Verification: 7-Mod User Set

Built and verified the full 7-mod set (`Abyss`, `Cave`, `Colour`, `HDLite`, `Perf`, `Smooth`, `Oops! All Chaws!`) against the legacy 2023 base (`/run/media/varialbe/Festplatte 2/Games/CatGoesFishing-legacy/2023-08-16_manifest-9039707068725406600/data.win`):

### Per-Mod Sanitizer Report Summary

| Mod | SHA-256 Prefix | `IsJammMod` | Removed Count | Unclassified Count | Summary |
|---|---|---|---:|---:|---|
| **Abyss** (`the-abyss-beta.win`) | `44f4c296edde` | `false` | **0** | 0 | Pure content mod; nothing removed |
| **Cave** (`cave-expansion.win`) | `ef9241b4d341` | `false` | **0** | 0 | Pure content mod; nothing removed |
| **Colour** (`colour-expansion.win`) | `49c7bc3226c1` | `false` | **1** | 0 | Removed `[distance-cull]` `gml_Object_FishThinner_Step_0`: `do_zoning = 0; -> do_zoning = 1;` |
| **HDLite** (`jamm-hd-lite.win`) | `dfb06ad2865f` | `true` (`obj_jamm_hd`) | **0** | 0 | JAMM own mod; never sanitized |
| **Perf** (`jamm-perf.win`) | `8d3dbb5f3a51` | `true` (`obj_jamm_perf`) | **0** | 0 | JAMM own mod; never sanitized |
| **Smooth** (`jamm-smooth-fps.win`) | `ea70d00b09ba` | `true` (`obj_jamm_smooth`) | **0** | 0 | JAMM own mod; never sanitized |
| **Oops! All Chaws!** (`d73e967c….win`) | `d73e967c87fd` | `false` | **22** | 0 | All timing, 6-tick throttle, 5000/8000-px distance culls, fps-adaptive cull, debug overlay, `fpsController`, and room speed=1 removed; all content kept |

### Detailed Removals for Oops! All Chaws! (`d73e967c87fd…`)

1. `[timing]` `gml_Object_Girl_Create_0` (line 10): `time_scalar_last = 0.00003; -> time_scalar_last = 0.00006;`
2. `[timing]` `gml_Object_Girl_Create_0` (line 15): `time_scalar = 0.00003; -> time_scalar = 0.00006;`
3. `[timing]` `gml_Object_Girl_Step_0` (line 29): `delta = delta_time; -> delta = room_speed / 30;`
4. `[timing]` `gml_Object_Girl_Step_0` (line 31): `delta_fixed = clamp(delta_time / 16000, 1, 50); -> delta_fixed = room_speed / 30;`
5. `[timing]` `objects/fpsController`: removed added busy-wait frame limiter object (`gml_Object_fpsController_Create_0`, `gml_Object_fpsController_Step_0`, `gml_Object_fpsController_Step_2`, and its `NewWorld` room instance)
6. `[timing]` `rooms/NewWorld`: `room speed 30 -> 1 reverted to 30`
7. `[tick-throttle]` `gml_Object_Fish_Create_0` (line 1): `check_timer = 0;`
8. `[tick-throttle]` `gml_Object_Fish_Step_2` (line 59): `check_timer++; if (check_timer >= 6)` gate + 5000-px `Camera` distance cut-off reverted to base lines 59..89
9. `[tick-throttle]` `gml_Object_CaveRock_Create_0` (line 2): `checkTimer = 0;`
10. `[tick-throttle]` `gml_Object_CaveRock_Step_0`: `checkTimer += 1; if (checkTimer == 5)` + 5000-px `Camera` distance cut-off removed (and `/events/3:0` on `CaveRock` reverted)
11. `[distance-cull]` `gml_Object_Fish_Draw_0` (line 3): `if (point_distance(x, y, Camera.x, Camera.y) < 5000)`
12. `[distance-cull]` `gml_Script_fish_basicdraw` (line 1): `if (instance_exists(Girl)) if (point_distance(x, y, Camera.x, Camera.y) < 5000) ... else exit;`
13. `[distance-cull]` `gml_Script_fish_predraw_switchcase` (line 14): `if (point_distance(x, y, Camera.x, Camera.y) < 5000)`
14. `[distance-cull]` `gml_Object_SonarPanel_Draw_0` (hunk 1, line 65): `if (point_distance(x, y, Camera.x, Camera.y) < 5000)` + `visible = false;`
15. `[distance-cull]` `gml_Object_SonarPanel_Draw_0` (hunk 2, line 73): `if (point_distance(x, y, Camera.x, Camera.y) < 5000)`
16. `[distance-cull]` `gml_Object_SonarPanel_Draw_0` (hunk 3, line 78): `if (point_distance(x, y, Camera.x, Camera.y) < 8000)` + `visible = false;`
17. `[distance-cull]` `gml_Object_SonarPanel_Draw_0` (hunk 4, line 82): `if (point_distance(x, y, Camera.x, Camera.y) < 8000)`
18. `[distance-cull]` `gml_Object_SonarPanel_Draw_0` (hunk 5, line 86): `if (point_distance(x, y, Camera.x, Camera.y) < 5000)`
19. `[distance-cull]` `gml_Object_Weed_Step_0` (line 2): `if (point_distance(x, y, Camera.x, Camera.y) > 5000) visible = false; exit;`
20. `[distance-cull]` `gml_Object_Dangler_Step_0` (line 2): `if (point_distance(x, y, Camera.x, Camera.y) > 5000) visible = false; exit;`
21. `[fps-adaptive]` `gml_Object_FishThinner_Step_0` (line 216): `if (fps < 60) cull_trigger += 65;` reverted to base 30/45/60 thresholds
22. `[debug-overlay]` `gml_Object_ExperienceBar_Draw_0` (line 12): `"FPS:"`, `"X:"`, `"Y:"` debug text overlay removed
- Plus unreferenced helper variables (`check_timer`, `checkTimer`, `target_fps`, `target_frame_time`, `frame_duration`, `now`) and functions (`game_get_speed`, `game_set_speed`, `get_timer`) pruned from OAC's patch.
- Kept content verified in merged `data.win`: all new fish objects (`Elder_Chaw`, `Bassoli`, `Goldi`, `Kuito`, `Luce`, `Minau`, `Mulite`, `Pieraruchu`, `Obliterate`, `rootum`), mangrove roots/tree (`Root1`, `Root2`, `StartRoot`, `EndRoot`, `tree`), catalog entries, Bulbguard upgrade (`UpgradeData_Create_0`, `UpgradeMenu_Create_0`, `UpgradeMenu_Alarm_0`, `UpgradeCard_Other_10`, `Rod_Create_0`, `Rod_Other_10`, `Rod_Step_0`, `Girl_Other_10`, `Lightbulb_Create_0`, `Bulbeater_Other_10`, `calculate_upgrade_cost`), `Boat7_Create_0` (`MAX_X = 82430`), `SaveManager_Other_13`, and `fish_was_just_bombed`.

---

## Build & Test Results

- `dotnet build -c Release`: **0 warnings, 0 errors**
- `dotnet test -c Release`:
  - `Jamm.Core.Tests`: **282 passed, 0 failed** (including `ModSanitizerTests` and `RealSevenModSetSanitizesBuildsAndVerifiesWithContentIntactAndHacksRemoved` with `cgfmm verify`)
  - `Jamm.Gui.Tests`: **102 passed, 0 failed** (including `SanitizedModRowShowsRemovedPerformanceCountInfoFlyoutAndAdvancedKeepSwitch`)
