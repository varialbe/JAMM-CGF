# Task: engine/build optimisations for ALL packs (branch `exp/engine-build-opt`, Codex Luna)

User request (BRIEFING section 16). Order: **measure → plan → orchestrator review → build.** Everything must apply
automatically during a JAMM build for every pack and must not change gameplay or visuals (screenshot parity +
autoplay `tools/vps/autoplay.sh`).

## Phase 1 now: measure + plan (commit `docs/design/engine-build-opt.md`, then STOP for review)
1. **Empty/dead events:** GM calls events per instance even when empty (e.g. `Rock_Step_2` = 0 instructions,
   472×/tick). Count first: how many empty event calls per tick in vanilla / public-5 / all-7 (static analysis of
   archives × room instance counts, confirmed by `--profile-scripts`/event profiles). Candidates: empty events,
   events that only call `action_inherited()` when the parent has no code, unreachable code, duplicated mod
   controllers. Mind that removing an event can change inheritance (a child's empty event overrides a parent event!).
2. **Decor without logic → tiles:** objects/instances without Step/Draw logic (or only trivial Create values) turned into
   room tiles at build time (no events, batched drawing). Check parallax (`home_x`/`para_scale`!), depth, light
   interaction, sonar, `instance_nearest`/collisions, chunk streaming, save. Candidate list per object: allowed /
   not allowed / reason.
3. **Depth sorting:** GM 1.4 re-sorts the draw list when `depth` changes. `SceneItem` (and others) set depth every
   tick → write only on real change (`if (depth != new)` guard). Measure runner time outside events
   (`loop_us` minus the sum of event times).
Measurements serial via `~/jamm-night/bin/run.sh` (shared game lock: queue and WAIT, can take up to an hour; never kill
other runs; on EXITED `~/jamm-night/bin/steam-restart.sh` and retry; delete `~/jamm-night/runs/*/game` copies).
Archives: legacy base `~/cgf-files/legacy/2023-08-16_manifest-9039707068725406600/data.win`, packs in
`~/cgf-files` (TGE/PWB stay on the VPS – never commit them or images of them), personal package
`~/jamm-night/personal-tge/own-mods-2026-11-22c.win`. Coordinate with the parallel jobs: Fish Sleep / DecorSleep
(`PerfSettings`), `exp/fish-step-micro` (bytecode optimiser) – no duplicate passes, name overlaps in the plan.
The plan lists per item: measured cost, expected gain, risk, test strategy, and where in the build pipeline it hooks.
Later items if time: pixel→bbox collision masks where irrelevant, half-resolution light surfaces, p99 stutter
(autosave, CreateTexturesOnDemand, instance spawns), a "JAMM Lint" report per pack for mod authors.
AGENTS.md applies. Commit on this branch; do not push.
