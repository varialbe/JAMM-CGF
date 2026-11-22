# Task: multicore via external native DLL (branch `exp/native-offload`, Codex Luna)

User request (BRIEFING section 12). The GM 1.4 runner executes GML single-threaded (fish ~40–75 % of a tick,
decor ~30 %). The only multicore path is `external_define`/`external_call` into our own native Windows DLL
(running under Wine/Proton), with data exchanged via buffers (`buffer_get_address`).

## Phase 1 (now): research + plan – NO feature implementation yet
Write `docs/design/native-offload.md` and commit it. **Measure, do not estimate**, wherever possible:
- What the GM 1.4 legacy VM (Aug 2023 build, `~/cgf-files/legacy/2023-08-16_manifest-9039707068725406600/data.win`)
  actually allows: `external_define` signatures (real/string only, arg limits), per-call overhead (micro-benchmark
  in-game: 1 / 100 / 10,000 calls per tick), whether `buffer_get_address` exists in this runner, buffer lifetime and
  thread safety.
- Toolchain: build a Windows DLL on this VPS with mingw-w64 (install if missing, sudo allowed). Check the runner's
  bitness (x86 vs x64) first. Prove loading under the VPS Wine game setup with a tiny test DLL (ModMaker/bench
  instrumentation or a minimal test archive; shared game lock via `~/jamm-night/bin/run.sh`, other agents test too;
  on EXITED run `~/jamm-night/bin/steam-restart.sh` and retry; delete `~/jamm-night/runs/*/game` copies).
- Async pattern: Begin Step writes fish/decor positions into a buffer, DLL computes on worker threads in parallel to
  GML, End Step / next tick collects results (neighbour lists, visibility/sleep decisions, distances, avoidance
  forces). Which parts of `Fish_Step*`, FishLarge avoidance (`src/Jamm.Core/Builtins/FishGrid.cs`) and decor culling
  are pure math and offloadable?
- Expected gain with an Amdahl calculation from our profiler data (`docs/reports/*`, `site/cgf-mods/progress.html`);
  risks (determinism, save/load, missing DLL → clean GML fallback, Steam/Proton sandbox, antivirus for players).
- Packaging: DLL shipped by a JAMM builtin/ModKit package next to the exe; source in the repo (own code, MIT).
- Concrete implementation plan in steps with a measurement point after each step; first hotspot proposal.
Stop after Phase 1 and commit. The orchestrator reviews the plan (Phase 2) before Phase 3 starts.
Do not push. AGENTS.md applies (never modify input game files, never commit *.win or game data).
