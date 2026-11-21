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
