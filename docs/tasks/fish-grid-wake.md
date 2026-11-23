# Task: spatial grid + hysteresis wake (exp/fish-grid-wake) — items 10,11 (user ideas: neighbour list / Sv39-like paging hashtable)
Today each sleeping fish checks itself every 8 ticks whether it is in the relevance zone (FishSleep.cs, margin 2000, staggered).
Goal: cost proportional to camera movement, not fish count.
1. Grid: ds_map keyed by cell (e.g. 512 px; cell key = cx*65536+cy or string) → ds_list of fish ids; maintained on sleep/wake/moves
   of awake fish (cheap: only re-bin when cell changes). Optionally evaluate sweep-and-prune (x-sorted list with insertion sort per tick)
   and a 2-level sparse table (Sv39-like, top-level chunk → cell) for Endless Ocean x up to ~1.1M; choose by measurement, keep simplest.
2. Wake: only cells entering the camera+margin rect each tick are scanned and their fish woken; sleeping fish do NO per-tick check.
   Hysteresis: wake at margin W, sleep only beyond W+300.
3. Use the grid also for FishLarge avoidance neighbour queries if it beats current FishGrid (src/Jamm.Core/Builtins/FishGrid.cs; previous
   attempt lost 172→179 ms — learn from docs/reports/fish-perf-grid.md).
4. Must keep: population counters, sonar, save/load, lure collisions, room change/teleport (rebuild grid on room start / big camera jump).
5. Measure LOCAL via ~/jamm-bench/run.sh (flock serialized) vanilla/pub5/all7 × dock/deepwater 2× vs main; crash scenes; endless-ocean
   far-east travel. Never CGF-Personal/cgf-personal/343780, no xdotool. Report docs/reports/fish-grid-wake.md; commit; no push.
