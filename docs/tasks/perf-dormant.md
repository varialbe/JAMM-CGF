# Task: perf round 3 — remove per-instance dispatch cost of sleeping fish and idle decor (branch exp/perf-dormant)

## Evidence (local profiler, Ryzen 5700X, 2026-11-22, all7 dock with FishSleep, profiled loop 191 ms)
gml_Object_Fish_Step_2 2204 calls/tick 49.5 ms/tick (26%); jamm_b_fish_step_enter/leave 2980 calls/tick ~10 ms together;
CaveRock_Step_0 3836 calls/tick 18.4 ms (10%); SceneItem_Step_2 2880 calls/tick 15.3 ms (8%); SceneItem_Draw_0 11.8 ms;
Weed_Step_0 1197 calls/tick 5.7 ms; FishLarge_Step_0 6.8 ms. pub5: Fish_Step_2 42%.
Unprofiled FPS (main @56aa26c): vanilla dock 89, pub5 21, all7 20. FishSleep makes sleeping fish return early, but every sleeping
instance still pays GM 1.4 event dispatch + the enter/leave hook script calls every tick. Profile CSVs:
~/.var/app/com.valvesoftware.Steam/data/JAMM-CGF/bench-runs/fsp-*/jamm_profile_dock.csv.

## Goal
Sleeping/far instances must cost ~0 per tick. Candidates (implement, measure, keep what wins):
1. **Dormant fish**: fish asleep and far from camera/player/lure → `instance_deactivate_object(id)` (or region-based
   `instance_deactivate_region` outside a camera-padded rect, re-activated each N ticks / when camera moves), OR swap to a lightweight
   dormant object via `instance_change(obj_jamm_dormant, false)` that keeps state vars and swaps back. Must preserve: fish counts used
   by spawners/population (FishThinner), sonar listings, save/load, catching, collisions with lure, lunges, schooling, weather/night.
   Watch: with/instance_number/instance_find on deactivated instances (they are invisible to with/instance_number) — audit every
   counting site in vanilla + own mods, keep a counter, or choose instance_change variant.
2. Remove the enter/leave hook script calls for sleeping fish (inline the fast path at the event top, no script call).
3. **Decor**: CaveRock_Step_0 / SceneItem_Step_2 / Weed_Step_0 / Rock_Step_2: skip or deactivate when outside camera+margin and
   nothing interacts; extend existing DecorSleep (src/Jamm.Core/Builtins/DecorSleep.cs) accordingly. SceneItem_Draw_0: skip draw when
   off-view if not already culled.
4. Everything behind the existing perf builtin flags (FishSleep/DecorSleep modes), default-on only if measurably better and safe.

## Measurement (LOCAL only, serialized)
Use ~/jamm-bench (see run.sh; it takes a flock so only one game runs; isolated prefix compatdata/jamm-bench; needs Steam running:
`systemctl --user is-active jamm-steam || systemd-run --user --unit=jamm-steam --collect flatpak run com.valvesoftware.Steam`).
Build vanilla, pub5, all7 from your branch the same way ~/jamm-bench/build.sh / builds/cd-now did (inputs in ~/jamm-bench/inputs*),
run unprofiled 2× per (build, scene ∈ dock, deepwater) old vs new, then crash scenes (fs-local/crash.sh pattern) and one profiled run.
NEVER use or touch CGF-Personal, compatdata cgf-personal or 343780. No xdotool. Labels prefix `pd-`.
Report table (FPS, loop ms, p95) in docs/reports/perf-dormant.md; commit on branch; do not push.

## Round 2 (orchestrator review 2026-11-23 03:00) — previous round did NOT meet the goal
Your Old/New compared "no sleep" vs "--fish-sleep --decor-sleep"; both flags are already on main, so that table measures main, not your
change. jamm_b_fish_step_enter/leave are BENCHMARK instrumentation only (not in player builds) — optimizing them is irrelevant to players.
Required now:
1. Implement the real dormant swap: sleeping fish → `instance_change(obj_jamm_fish_dormant, false)` (object with NO Step/Draw/Alarm
   events, keeps all variables; stores original object_index in a var) and back on wake. Audit every `object_index` / `with(Fish...)` /
   instance_number(Fish*) / sonar / save / population / FishThinner / lure-collision site that would miss dormant fish and handle each
   (e.g. dormant object parented so `with(Fish)` still finds it but has no events? — evaluate parent=Fish with empty overriding events,
   which keeps counts/with working while removing the inherited Step: GM 1.4 calls an object's own event or inherits parent's; an EMPTY
   own event stops inheritance cheaply). Measure whether the dispatch saving is real.
2. Decor: CaveRock_Step_0 is still 12–17% — find why it's not covered (decision said "nothing to draw or step"?!) and fix.
3. A/B MUST be: main (git stash/build from main worktree "/run/media/varialbe/Festplatte 2/JAMM-CGF") vs this branch, IDENTICAL flags
   (--fish-sleep --decor-sleep), all7+pub5+vanilla, dock+deepwater, 2× each, unprofiled. Report that table.
