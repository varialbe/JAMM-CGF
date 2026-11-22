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

## VPS continuation (orchestrator, 2026-11-22 02:00) – open items, in this order
1. all7 dock saves only ~2.4 ms fish_step although ~1909 fish sleep. Check whether sleeping fish still run their
   Step (guard placement, inherited events, other Step subtypes) – compare against the `--fish-mode nostep` baseline.
2. Crash `Bishop_Other_10` (`escort_target` does not exist) with `--fish-cap`: find the cause, fix without changing
   vanilla behaviour.
3. `git merge main` (big conflicts with the Perf rework, main has `PerfSettings`/DecorSleep): install Fish Sleep ONLY
   when its profile setting is on (pattern: DecorSleep in PerfSettings). Plain Perf + vanilla must stay byte-identical
   (add a test).
4. Save/reload verification in-game (no sleeping state leaks into saves).
Benchmarks strictly serial (shared game lock `~/jamm-night/game-run.lock` via `~/jamm-night/bin/run.sh`; other
agents test too). On EXITED run `~/jamm-night/bin/steam-restart.sh` and retry. Delete `~/jamm-night/runs/*/game`
copies after your runs (disk is tight). Before finishing: autoplay 20 min on the personal package with Fish Sleep
on (`tools/vps/autoplay.sh`), `dotnet build -c Release` zero warnings, `dotnet test -c Release` green, report
`docs/reports/fish-sleep.md` with numbers (fish_step ms before/after, sleeping count, all7 + personal) and screenshots.

## Local round on the PC (orchestrator, 2026-11-22 13:05) — replaces the VPS continuation

The VPS is paused for game runs. Work ONLY on this PC. Base: `3fc3b67`.

### Local measurements (unprofiled, 30 s, 2 rounds, `~/jamm-bench/run.sh`, mm built from 3fc3b67)
| Build | Scene | Off FPS / loop ms | On FPS / loop ms | Fish count off → on |
| --- | --- | --- | --- | --- |
| vanilla | dock | 48.1 / 20.2 | 63.8 / 14.8 | 956 → **1459** |
| pub5 | dock | 11.9 / 82.2 | 24.8 / 38.8 | 2157 → 2034 |
| all7 | dock | 16.6 / 58.4 | 18.4 / 52.3 | 2194 → 2199 |
| all7+perf (DecorSleep on) | dock | 17.2 / 55.6 | 20.4 / 46.3 | 2198 → 2204 |
Code installed but `FishSleep=0` in INI: no measurable overhead (vanilla 48.6 vs 47.5, all7 16.4 vs 16.1), fish count normal (932).
Profile all7 dock with sleep on (`--profile-scripts`): `Fish_Step_2` is still called for all ~2200 fish and costs 49 ms/tick
profiled (80 ms off) although ~85 % sleep (`Fish_Other_22` calls 1649 → 239 per tick). `FishLarge_Step_0` unchanged
(6.8 ms, 474 calls; FishLarge mostly exempt as huge/boss). `jamm_sleep_should_sleep` 376 calls/tick. The end-of-run counter
`asleep=` (3000) is larger than the fish count (2200) → the counter drifts.

### Part A — Gemini Hydrogen (worktree `JAMM-CGF-fishsleep`, branch `exp/fish-sleep`): cheap sleeping path
Goal: a sleeping fish must cost almost nothing per tick (target: close to the `--fish-mode nostep` exit).
1. Asleep fast path as the FIRST statements of the Fish_Step_2 prefix: one instance read + one global compare, e.g.
   `if (jamm_asleep) { if (global.jamm_sleep_tick < jamm_sleep_next) exit; ...full wake checks...; jamm_sleep_next = global.jamm_sleep_tick + N; }`
   with staggering (N≈8, first slot offset by id) so wake checks spread over ticks. Wake latency must stay invisible: the
   margin (≥ view width + 200) covers N ticks of camera/boat/lure travel; hooked/lure-targeted fish wake immediately
   (the Lure/catch code paths must wake a fish directly, not wait for the next slot).
2. Same staggering for awake fish outside the view: call `jamm_sleep_should_sleep()` only every N ticks per fish.
3. Guards in subclass Step/Draw/Collision events: replace `variable_instance_exists(id, "jamm_asleep") && jamm_asleep`
   with a plain `jamm_asleep` read where Fish Create guarantees the variable (keep the safe form only where a fish can run
   before Fish_Create_0).
4. Check whether huge/boss FishLarge species can be covered safely (at least those whose custom events are pure movement);
   otherwise document why not.
Prove: unit tests, then local bench (vanilla, pub5, all7 from `~/jamm-bench/builds/decor/` and the clean vanilla
`~/.var/app/com.valvesoftware.Steam/data/JAMM-CGF/CGF-legacy-clean/data.win`), dock + deepwater, off/on, 2 rounds,
plus one `--profile-scripts` all7 run. Report FPS/loop table in `docs/reports/fish-sleep.md`.

### Part B — Codex Luna (worktree `JAMM-CGF-fishpop`, branch `exp/fish-sleep-pop`): population + counter
1. Vanilla dock has 50 % MORE fish with sleep on (956 → 1459, already constant at capture start, seed 1337). Find the
   cause (candidates: FishThinner `Other_10` culls via `instance_deactivate_object` and `kill_extras`/`fish_die`;
   spawners/`fish_respawn`; predation; sleeping fish skipping their own death/despawn logic; `visible = false` on sleep)
   and fix it so the population matches sleep-off within noise, without changing vanilla behaviour when sleep is off.
2. Fix the `global.jamm_sleep_asleep_count` drift (fish destroyed or deactivated while asleep must decrement it, or count
   it differently), so the bench line `asleep=` is the real number of sleeping fish.
3. Tests for both. Prove with the local bench: vanilla and pub5 dock, off/on, compare `fish_count` and `asleep=`.
Keep changes small and in separate functions where possible: Part A edits the same file (`FishSleep.cs`); the
orchestrator merges both branches.

### Rules for both
- Models: Codex only `gpt-6-luna` (high), Gemini only `gemini-4-hydrogen-high`. Never Sol.
- Games ONLY through `~/jamm-bench/run.sh <win> <label> <scene> [sec] [bench flags]` with `MM=<your worktree's
  published jamm-modmaker.dll>` (publish to `~/jamm-bench/tools/mm-<yourname>/`). run.sh holds a global lock: only one
  game at a time, wait for it. Never start the game any other way. Labels: Part A `fsa-*`, Part B `fsb-*`.
- NO xdotool key/mouse input, NO desktop portals / screen sharing (they open GNOME remote-desktop popups).
- Never touch `~/.var/app/com.valvesoftware.Steam/data/CGF-Personal`, compatdata `cgf-personal` or `343780`.
- Disk is tight (16 GB free): run.sh deletes data.win after each run; do not keep extra .win copies.
- TGE/PWB stay local: never commit them or images of them.
- Run dotnet from the REAL path (`/run/media/varialbe/Festplatte 2/...`). `dotnet build -c Release` zero warnings,
  `dotnet test -c Release` green. Commit on your branch; no merge, no push.
