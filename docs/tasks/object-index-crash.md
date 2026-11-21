# Task: Fix "Unable to find any instance for object 'Bo'" (Fish_Other_17) with the Weather Pack

Branch `exp/object-index-guard`, worktree `~/jamm-wt/object-index-guard`.

## Report (user, 2026-11-21, personal build = vanilla + perf, hd-lite, console, smooth, silo, weather-pack, endless-ocean)
```
FATAL ERROR in action number 1 of Other Event: User Defined 7 for object Fish:
Unable to find any instance for object index '102' name 'Bo'
at gml_Object_Fish_Other_17
stack: Fish_Other_17 <- fish_lunge_c <- fish_default_lunge <- Fish_Other_11 <- Fish_Step_2
```
Build: `~/jamm-night/personal-tge/own-mods.win` (inputs in the same folder; order perf, hd-lite, console, smooth,
jamm-silo, weather-pack, endless-ocean).

## Analysis (orchestrator)
* Vanilla `gml_Object_Fish_Other_17` (bite handler) contains `if (Lure.fish.object_index == Bo.object_index)`,
  `Gallina.object_index`, `Skrimp.object_index`. `Obj.object_index` reads a variable from the first instance of
  `Obj` and errors when none is active. Vanilla rarely reaches the "bigger fish attacks the hooked fish" branch;
  the Weather Pack surface schools reach it often, while Bo is absent or deactivated (Perf scheduling/region
  deactivation?).

## Required
1. Confirm the cause (is Bo deactivated by Perf, simply absent in that area, or both?). Reproduce in a bench scene
   if possible (weather school + hooked small fish + no Bo instance).
2. Generic build-time fix for every modded build (vanilla byte-identical): rewrite `<ObjectName>.object_index`
   (an object asset name, not an instance variable) to the object constant `<ObjectName>` in all code entries,
   vanilla and mod code. Decision `[CGF-INSTANCE-004] ...` listing rewritten entries. Make sure it is semantically
   identical (object_index of an instance of X is X; never rewrite when the left side is an instance/variable).
3. Check the Weather Pack schools and Fish API for other places that assume instances exist
   (`instance_nearest`, `with`, `Obj.var` reads) and guard them.
4. Tests (unit + vanilla byte-identical + full suite). In-game: weather scene with hooked fish if the VPS Steam client
   is logged in; otherwise say so.
5. Report `docs/reports/object-index-guard.md`. Commit on the branch; do not merge into main.

## Addendum 2026-11-21 (orchestrator): make it GENERAL – all fish, all mods
The user wants this class of crash gone for every fish and every mod, not just Bo. Scope extends to:

### A. Analyzer (new, reusable)
* `cgfmm audit instances <data.win> [--json <file>]` (Core: `InstanceReferenceAnalyzer`, no console I/O) that scans
  decompiled code of an archive and lists every risky pattern with code entry, line and object:
  1. `<Obj>.object_index` where `<Obj>` is an object asset name → always rewritable to `<Obj>`.
  2. `<Obj>.<var>` reads/writes where `<Obj>` is an object asset that is not guaranteed to exist
     (classify: persistent/controller objects placed in every room = safe; fish, Lure targets, boats, hats,
     mod objects, Maize/Bo/Gallina/Skrimp etc. = risky). Use room placement + persistence + creation sites.
  3. `with (<Obj>)` – safe (no-op when absent), list only for completeness.
  4. `instance_nearest(...)`, `instance_find(...)`, `instance_place(...)`, `collision_*` results that are
     dereferenced (`r.var`) without a `noone`/`instance_exists` check.
  5. Functions whose result can be `-1` (= self in the legacy runner, see the Maize crash
     `nav_get_random_connected_node`) and that get dereferenced.
* Run it on vanilla and on all 7 public/private mods (`~/cgf-files/legacy/...`, `~/cgf-files/mods/*.win`,
  `~/cgf-files/mods/pack-inputs/*.win`, `~/cgf-files/private-vel/*` – private files stay on the VPS, report
  only names/counts) and on our own ModKit packages (`examples/modkit/*` built). Commit the report table to
  `docs/reports/object-index-guard.md` (no game code dumps beyond short one-line snippets).

### B. Build-time guards (every modded build; vanilla-only stays byte-identical)
* Pattern 1: rewrite to the object constant (`[CGF-INSTANCE-004]`).
* Pattern 2 reads in conditions/expressions: wrap with `instance_exists(Obj)` so the expression short-circuits to the
  "absent" outcome (e.g. `(instance_exists(Bo) && Lure.fish.object_index == Bo)`), writes with
  `if (instance_exists(Obj))`. Only for objects the analyzer classifies as risky; `[CGF-INSTANCE-005]` lists entries.
* Pattern 4/5: insert `noone`/`-1`/`instance_exists` checks before the dereference (`[CGF-INSTANCE-006]`).
* Generated GML must compile with the legacy compiler (UTML 0.9.2) and must not change behaviour while the object
  exists. Keep every guard idempotent (base-mod mode runs the builder twice: skip in `intermediate` layers like
  the other runtimes, see `ArchiveBuilder.BuildLayer`).
* Bump `InstanceCompatibility.Revision`.

### C. Root-cause check
* Check whether JAMM Perf fish scheduling / FishThinner / region deactivation (`instance_deactivate_*`) is what
  makes Bo "absent" while code still references it. If Perf deactivates objects that vanilla code dereferences,
  fix that too (exclude them or reactivate), and document it.

### D. Stress test (VPS, `~/jamm-night/bin/run.sh`, shared game lock)
* Bench scene `stress` in tools/Jamm.ModMaker: many fish incl. weather schools, a hooked small fish while bigger
  fish lunge, Perf scheduling on, forced deactivation/reactivation, 3+ minutes.
* Run dock, deepwater, silo, weather, worldgen and stress scenes for: personal build (perf, hd-lite, console,
  smooth, silo, weather-pack, endless-ocean), public-5 and all-7 (`~/jamm-night/builds/`). Result table: no
  `CODE ERROR` anywhere; attach error.txt for any failure and fix it.
* Commit on `exp/object-index-guard`, do not merge into main, do not push main.
