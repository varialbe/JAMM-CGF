# Task: algorithmic fish-AI optimisation research (branch `exp/fish-ai-batching`, Codex Luna)

User request (BRIEFING section 13). Research + plan only; implementation follows later (Hydrogen) after the
orchestrator's review. Treat the user's ideas as hypotheses to test, not as decisions:
- **Cluster/batch:** compute fish AI once per species/school instead of per instance (e.g. Cuddlefish move alike →
  one computed movement, per fish only direction/phase/offset vary).
- **Reuse with tolerance:** cache results and reuse while inputs (position, target, neighbours) change only slightly;
  recompute past a threshold or every N ticks with interpolation (temporal coherence).
- Other levers: spatial hash instead of `instance_nearest`/`with` loops, LOD by distance to camera/lure, event
  ordering (expensive queries only on state change), hoisting shared per-tick computations.

## Do
1. Analyse `Fish_Step_2`, `FishLarge` and the most frequent species in vanilla
   (`~/cgf-files/legacy/2023-08-16_manifest-9039707068725406600/data.win`) and the packs (decompile with
   UndertaleModLib/ModMaker tooling, `--profile-scripts` benches): which species behave deterministically alike,
   which parts are expensive, which depend on randomness/player interaction.
2. **Gameplay rule per species:** allowed only if visually indistinguishable and without influence on catch/lure/
   sonar/spawn. Fish that see the lure, are hooked, flee or hunt ALWAYS compute individually and exactly. Off-screen
   may be more generous. Deliver a table species → allowed / not allowed / reason.
3. Gain estimate (Amdahl) from our profiler data (`docs/reports/*`, `site/cgf-mods/progress.html`) plus a small
   prototype for 1–2 species, measured serially (shared game lock via `~/jamm-night/bin/run.sh`, other agents test
   too; on EXITED `~/jamm-night/bin/steam-restart.sh` and retry; delete `~/jamm-night/runs/*/game` copies). Parity:
   catch-rate/sonar CSVs (autoplay `tools/vps/autoplay.sh`) equal before/after within noise.
4. Plan in `docs/design/fish-ai-batching.md`: reconcile with Fish Sleep (`origin/exp/fish-sleep`,
   `docs/tasks/fish-sleep.md` there – no duplicate work, combinable?) and with the native DLL offload research
   (`exp/native-offload`, `docs/design/native-offload.md` once committed). Concrete steps with a measurement point
   each; which parts are for a GML patch (Hydrogen) and which could go native.
Commit on this branch; do not push. AGENTS.md applies. Prototype code stays on this branch, off by default.
