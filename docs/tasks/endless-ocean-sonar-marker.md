# Task: Endless Ocean 0.3.0 — sonar to any depth, no noise in the ocean, "Endless Ocean" marker

Branch `feat/endless-ocean-sonar` (worktree `/run/media/varialbe/Festplatte 2/JAMM-wt-ocean-sonar`). Work only in this
worktree, use the REAL path (never the `~/JAMM-CGF` symlink). Commit on the branch, do not push, do not merge.
Never touch `~/.var/app/com.valvesoftware.Steam/data/CGF-Personal` or compatdata `cgf-personal`.

Generator: `src/Jamm.Core/Builtins/EndlessOceanMod.cs` + `src/Jamm.Core/Builtins/Worldgen/*.gml`
(embedded resources `Jamm.Worldgen.<name>`), recipe `examples/modkit/endless-ocean` (bump to 0.3.0, generatorVersion
as needed), tests `tests/Jamm.Core.Tests/EndlessOceanTests.cs`, `tests/worldgen/chunks.test.mjs`.

## Vanilla facts (August 2023 legacy, decompiled)

- `Lure_Step_0`: `deep = (y > 4500)`.
- `SonarPanel_Step_0`: shallow → `view_yview[3]=1255; view_wview[3]=view_hview[3]=3700` (window y 1255..4955).
  Deep: if `CaveSonarUnlocked && SonarPanelAdvanced.active` → `cave_sonar=1`, 2000x2000 window following the lure
  (`fixed_y=min(Lure.y, room_height-1000)`, `view_yview>=4000`). Otherwise `if (Lure.y > 4955) shouldDraw = 0;`
  → without Deep Sonar the sonar switches off below ~4955.
- `SonarPanel_Draw_0`: `noise_alpha = (((min(Girl.x, Lure.x) - 10000)/2.6) + (max(Lure.y,1294) - 4000) - 4000)/1000;`
  then `if (instance_exists(Boat) && Boat.boost_radar) noise_alpha = 0;` (only `Boat7` = "Tech Boat — Penetrates
  sonar jammers" has `boost_radar = 1`). Blips are drawn only while `noise_alpha < 1`; the noise sprite is drawn
  with `min(1, noise_alpha)`.
- Reach: only `Boat7` gets `MAX_X = ocean_end - 400` (step.gml). User decision: KEEP THIS. Do not extend any other
  boat (incl. the submarine mod). Tech Boat behaviour (boost_radar, MAX_X, stats) must stay byte-for-byte the same
  outside what is listed here.

## Required behaviour (only inside the Endless Ocean; vanilla world unchanged)

"Inside" = `Lure.x >= ocean_start` while the lure is out, else `Girl.x >= ocean_start` (use the controller's
`ocean_start`; guard `instance_exists(obj_jmk_endless_ocean)` and every object you read; if the controller is
missing, vanilla code runs unchanged).

1. **No noise**: inside → `noise_alpha = 0` for any boat (so blips always draw). Outside → vanilla formula and the
   Tech Boat `boost_radar` rule exactly as before. Do NOT set/modify `Boat.boost_radar` anywhere.
2. **Sonar to any depth**: inside and `Lure.deep` and NOT `cave_sonar` (no Deep Sonar) → do not switch the sonar
   off; use a lure-following window like the cave branch (2000x2000, `fixed_y = min(Lure.y, room_height-1000)`,
   `view_yview>=4000`, `view_xview = Lure.x-1000`) with the regular (basic) blip drawing. Do NOT set `cave_sonar`
   (it switches fish silhouettes to advanced mode). With Deep Sonar the vanilla cave branch already follows the
   lure to the room bottom — keep it. Shallow branch inside: if the lure is below the 1255..4955 window it must
   still be visible (window follows the lure vertically, clamped to room bottom).
   Check `draw_offset/noise_scalar` so the panel/noise background sprites still cover the moved window (the
   panel is drawn at `view_xview+draw_offset` scaled 20x, noise scaled 12/20 — verify coverage for 2000x2000).
3. **Marker "Endless Ocean"**:
   - On crossing into the ocean (Girl.x >= ocean_start, with ~300 px hysteresis) show a vanilla-style message
     (create an object parented to the vanilla `Message` like `Message_DeepSonar`: `action_inherited(); text=...;
     font=1;`) — "Endless Ocean" on entry, "Leaving the Endless Ocean" on return. Not on game load if the save
     starts inside (or show once, your call, but never spam).
   - While inside and `!Girl.hide_ui`: a small Draw GUI label top-centre, e.g. `ENDLESS OCEAN · 12.4 km` (distance
     = (Girl.x - ocean_start)/1000 rounded to 0.1, "km" in-game units are fine) plus current biome name if cheap
     (`jmk_endless_ocean_biome`). Readable on 1080p + HD-lite, with a dark outline/shadow, no new sprites needed.
   - A visible world marker at `ocean_start`: e.g. the controller draws a tall glowing buoy/beam/sign (vanilla
     sprites only, e.g. reuse BuoyAbstract's sprite) at x=ocean_start on the sea surface (y from x_pos_to_y_off
     like BuoyAbstract_Step_0). Must not collide/affect physics.
   - Optional hint: when a non-Tech boat sits at its own MAX_X and `MAX_X < ocean_start`, show once per session a
     message "Endless Ocean lies further east — Tech Boat needed". Keep cheap.

## Implementation constraints

- Patching vanilla `gml_Object_SonarPanel_Step_0` / `_Draw_0`: do it robustly by symbol/regex on the decompiled
  source (see how `fish_respawn` is patched in EndlessOceanMod.cs) and fail with a clear InvalidDataException if
  the anchor is not found. Prefer a tiny call into a new script (e.g. `jmk_endless_ocean_sonar_view()` appended to
  the end of Step_0, and `if (jmk_endless_ocean_sonar_clear()) noise_alpha = 0;` inserted right after the vanilla
  boost_radar block) over large rewrites.
- Merged builds: other mods / `ModSanitizer.Sonar.cs` / `DecorSleepAnalysis.cs` (isSonarDraw) / Smooth / FishSleep
  touch or analyse these entries. Make sure the patch survives `RefreshMergedLayout` and a full profile merge
  (perf, hd-lite, console, smooth, silo, weather-pack, endless-ocean, submarine, harbor-town). If merging recompiles
  from a different base, re-apply idempotently (never double-insert).
- No hardcoded asset indices; `asset_get_index` / symbols only (see round 2 of docs/reports/ocean-sonar-crash.md).
- Perf: per-frame work O(1); no `with (all)`; no allocations per frame.
- Save: write nothing new to saves (hysteresis state is runtime only).

## Verification you must do

- `dotnet build -c Release` 0 warnings; `dotnet test -c Release` green; `node --test tests/worldgen/chunks.test.mjs`.
- New unit tests: patched Step_0/Draw_0 contain exactly one call each, vanilla text outside the patch unchanged,
  Boat7 create untouched (boost_radar=1, MAX_X 65000), no other boat gets MAX_X changes, idempotent re-apply,
  shifted asset indices still OK.
- Build the real archive: base `~/.var/app/com.valvesoftware.Steam/data/JAMM-CGF/CGF-legacy-clean/data.win`
  (sha dbb2e5c4…), `cgfmm modkit build examples/modkit/endless-ocean --base <that> --out <scratch>.win`; decompile
  with `tools/Jamm.ModMaker` (`dump`) and paste the patched SonarPanel_Step_0/Draw_0 + new scripts into the report.
  Do NOT launch the game (the user is at the PC); in-game tests are done by the orchestrator.
- Extend `tools/Jamm.ModMaker/WorldgenScene.cs` so the worldgen route records at each checkpoint: `sonar_visible`
  (view_visible[3] with sonar active, lure deep, Deep Sonar OFF), `noise_alpha`, `inside`, marker label shown.
- Report: `docs/reports/endless-ocean-sonar-marker.md` (what, why, test output). Update the recipe README.
