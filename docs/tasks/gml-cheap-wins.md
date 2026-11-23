# Task: GML cheap wins (exp/gml-cheap-wins) — items 3,4,6,7,8,9 of the idea list
Profile evidence: ~/.var/app/com.valvesoftware.Steam/data/JAMM-CGF/bench-runs/fsp-*/jamm_profile_dock.csv (all7 profiled loop 191 ms:
Fish_Step_2 26%, CaveRock_Step_0 10% (3836 calls/tick), SceneItem_Step_2 8%, SceneItem_Draw_0 6%, Weed_Step_0 3%, AbstractLight_Draw_0,
fish_lunge, fish_avoid_wall, Cuddlefish/Kingfish/Cusper Other_10, SonarPanel_Step_1 1.8 ms/tick, Buoy/BuoyAbstract ~1 ms each).
NOTE: exp/perf-dormant (other agent) does dormant fish + decor sleep extension; do NOT duplicate that. Your scope:
1. Analysis first (commit docs/reports/gml-cheap-wins-analysis.md): code search for quadratic loops (`with (Fish`, instance_nearest,
   collision_* inside per-fish events, per-tick with-loops in controllers incl. mod controllers silo/weather/endless-ocean/harbor),
   list sprites with precise (per-pixel) collision masks used by fish/lure/decor, step events that only animate.
2. Build-time transforms as perf builtin options (default on only if measured better and behavior-identical):
   a. precise→rectangle/ellipse masks for fish & decor sprites where gameplay-neutral;
   b. animation-only Step events → image_speed (strip event);
   c. bubble/effect objects created+destroyed often → part_system particles (only where purely visual);
   d. caching: per-tick player/lure distance, instance_number counters, CaveRock/SonarPanel/Buoy hot paths;
   e. LOD tiers for awake fish: near every tick, mid every 2nd/4th tick with dt-scaled movement, far = FishSleep;
   f. debug option: freeze stack trace (global current-event marker written to a ring, dumped on hang) à la UTMT Profiler.csx.
3. Measure LOCAL, serialized via ~/jamm-bench/run.sh (flock), vanilla/pub5/all7 × dock/deepwater, 2 runs old vs new, plus crash scenes.
   Never CGF-Personal/cgf-personal/343780, no xdotool. Report docs/reports/gml-cheap-wins.md. Commit on branch, no push.
