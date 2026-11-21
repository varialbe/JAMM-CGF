# Task: Fish Sleep – stop simulating ordinary fish far from the player (Perf builtin), behaviour-safe

Branch `exp/fish-sleep` (already contains exp/bench-fish-experiments tooling). Read AGENTS.md, docs/benchmarking.md,
the Perf builtin (existing distant-fish scheduling every 8th tick, FishGrid, instance guards CGF-INSTANCE-00x),
docs/reports/fish-perf-grid.md, docs/reports/fish-experiments-2026-11-21.md first.

## Evidence (user's PC, Ryzen 7 5700X, dock scene, see fish-experiments-2026-11-21.md)
Fish logic off (`--fish-mode nostep`, fish still drawn): vanilla 46→164 FPS, pub5 10→38, all7 16→59.
Loop ms: all7 61.6→14.7. Fish logic ≈ 75 % of the loop; cost ≈ linear in fish count (~20 µs/fish).
The existing "every 8th tick for distant fish" scheduling barely helps on big packs. Goal: approach the nostep numbers in normal play.

## Design
1. **Relevance zone** around the player: union of view (+ margin), lure, boat, Girl, sonar range (normal + advanced), and any
   active quest/area target. Fish inside: full simulation. Fish outside the zone + hysteresis band: asleep.
2. **Asleep** = no Step/Begin/End Step logic at all (early exit, like `--fish-mode nostep`), no Draw (off-view anyway).
   Choose between early-exit guard and `instance_deactivate` by measurement (deactivate removes engine overhead too, but breaks
   `with`, collisions and id lookups by other objects – only if guards cover all of those). Keep the instance, its variables
   and its id stable either way.
3. **Wake** when the fish re-enters the zone: catch up cheaply and plausibly (advance position along its path/heading by elapsed
   time clamped to the zone border, keep inside water/collision-free, reset transient AI state like lunge/flee timers). No visible
   pops at the view edge: the zone margin must be larger than one view width of camera travel per wake batch.
4. **Exempt** (always simulated): fish that are hooked, in the net, aboard, being reeled, legendary/boss/rare species with
   custom AI (detect by overridden events / non-standard parents, like the existing Perf eligibility), quest fish, Silo/Area
   fish, Endless Ocean chunk fish only if `jamm_chunk_stream` already releases them, weather fish while their weather is active
   (Weather API parks vanilla fish itself), any fish with a pending alarm that changes global state.
5. **Population invariants**: FishThinner, spawners and respawn must see the same population counts as before (sleeping fish
   still count); catalog/Fishalog, sonar fish dots (the sonar range is inside the zone), saves (`[Fishes]` save/load must write
   the same data; sleeping fish save their current state).
6. Builtin switch `jamm_perf.ini [Perf] FishSleep=1` (default ON with Perf), `FishSleepMargin=<px>`; decisions
   `[CGF-PERF-SLEEP]` per fish object (covered/exempt + reason) at build time, generic for modded fish (all7: TGE, PWB etc.).

## Prove
* Bench: `~/jamm-bench/run.sh <win> <label> dock|deepwater 30 [flags]` with `MM=` pointing at this branch's jamm-modmaker build,
  for builds in `~/jamm-bench/builds/` (vanilla, pub5, all7, all7-vpsorder). Before/after table. Only ONE game at a time:
  wait while `pgrep -f "Cat Goes Fishing.exe"` finds a process or any `systemctl --user` unit named `jamm-bench-*` is active.
* Behaviour: the autoplay scene (`--scene autoplay`) 2 seeds × 10 min on all7 with FishSleep on, no crash; fish counts per
  region stay comparable over 10 min (no depopulation, no pile-ups); catching, selling, Fishalog work; save → reload works.
* Tests for eligibility analysis and zone math; full suite green, zero warnings. Report docs/reports/fish-sleep.md.
* Also fix the bench `--fish-cap` CODE ERROR (pub5 cap 2000/1000/500 and all7 cap 1000/500/200 crash; cap 200 on pub5 works):
  destroying fish leaves dangling references – find them and guard or choose fish to remove safely.
Private packs (TGE/PWB) are local only: never commit or upload them. Commit on the branch; no merge, no push.
