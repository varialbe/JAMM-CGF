# Task: put static decoration to sleep (CaveRock, SceneItem, Weed, lights) – Perf builtin, behaviour-preserving

Branch `exp/decor-sleep`. Read AGENTS.md, docs/benchmarking.md, the Perf builtin (fish scheduling, FishGrid, instance guards)
and docs/reports/fish-perf-grid.md first.

## Measured 2026-11-21 on the user's PC (local profiler, `jamm-modmaker bench --profile --profile-scripts`, dock scene)
all-7 pack (TGE, Antiquity, PWB, Abyss, Colour, Verdant, Cave), 12,400 instances, ~2,200 fish:
| entry | instances/tick | share of loop |
|---|---|---|
| Fish_Step_2 | ~2,200 | 30 % |
| CaveRock_Step_0 | ~3,800 | 9 % |
| SceneItem_Step_2 | ~2,900 | 8 % |
| SceneItem_Draw_0 | ~2,550 | 6 % |
| Weed_Step_0 | ~1,200 | 3 % |
| AbstractLight_Draw_0 | ~890 | 2 % |
Vanilla has far fewer of these. Static decoration runs Step/Draw every tick everywhere in the 1.2 M px room.
Raw CSVs: `~/.var/app/com.valvesoftware.Steam/data/JAMM-CGF/bench-runs/m3-prof-*/jamm_profile_*.csv`.
Private packs are at `~/jamm-bench/inputs/private/{TGE,PWB}.win` (local only, never commit or upload),
public packs in `~/jamm-bench/inputs/`, built test archives in `~/jamm-bench/builds/` (e.g. all7.win, pub5.win, vanilla.win).

## Step 1: analysis (write it into the report first)
Decompile vanilla and the merged all7/pub5 builds. For every decoration-type object with Step/Draw events (CaveRock and children,
SceneItem and children, Weed, AbstractLight/Light/Light2, Bubble, any object with > 300 instances in all7): what does each event
do, does it depend on time (animation counters, alarms), on the player/lure/fish (collision, push, light toggles), on view?
Which are pure functions of state that could be skipped off-screen and caught up on wake (e.g. animation frame = f(time))?
Count instances per object in vanilla/pub5/all7 (NewWorld after room start) and which mod adds them.

## Step 2: implementation (Perf builtin, own switch `[Perf] DecorSleep=1`, default ON with Perf, F8 independent)
* Off-view decoration (outside view + margin ≥ the largest interaction radius found in step 1) skips Step (and Draw, which is
  culled by being off-view anyway – verify) via an early-exit guard, or is deactivated/reactivated in bounded batches around the
  camera with hysteresis – choose by measurement; prefer the method with less engine overhead and fewer side effects.
* Exempt anything that interacts with gameplay (collision with lure/boat/fish where the event matters, lights that affect fish,
  quest/area objects, Silo/Areas/Endless Ocean chunk instances owned by `jamm_chunk_stream`, anything referenced by instance id from
  other code unless guarded). Time-based state must be caught up on wake so nothing visibly jumps.
* Generic: decide per object by analysing its events at build time (pattern-based, like the fish scheduling eligibility), so modded
  decoration objects are covered; emit `[CGF-PERF-DECOR]` decisions (covered/skipped + reason).
* Never break a build; never change saves.

## Step 3: measure and prove
Bench with `--profile` before/after (all7, pub5, vanilla, dock + deepwater) via `~/jamm-bench/run.sh <win> <label> <scene> <sec> [flags]`
(only ONE game at a time: wait while `pgrep -f "Cat Goes Fishing.exe"` finds a process or any `systemctl --user` unit
`jamm-bench-*` is active). Target: clearly lower loop ms on all7/pub5. Add a visual check: screenshots at the same positions with
DecorSleep 0/1 must look identical (grim).
Tests: synthetic GML/.NET tests for eligibility analysis; full suite green, zero warnings. Report docs/reports/decor-sleep.md.
Commit on the branch; no merge, no push.
