# Task: Fish_Step micro-optimisations + bytecode/VM optimiser (branch `exp/fish-step-micro`, Codex Luna)

BRIEFING sections 11 and 15 combined. Plan + measurement first, then implement.
1. Profile `Fish_Step_2` line by line (`--profile-scripts` benches, serial; shared game lock via
   `~/jamm-night/bin/run.sh`) and implement safe micro-optimisations (cache `point_distance`, `instance_nearest`,
   `with` loops). Every change with gameplay parity (catch/sonar CSVs from autoplay `tools/vps/autoplay.sh`).
2. Evaluate an automatic bytecode optimiser over ALL mods (JAMM rewrites bytecode anyway): constant folding,
   hoisting repeated instance-variable lookups into `var`, replacing `variable_instance_exists`/string lookups in hot
   paths, removing unnecessary `with` context switches, instance pooling instead of instance_create/destroy churn.
   Count VM instructions before/after; measure tick time.
3. Memory: the GM 1.4 runner is 32-bit → check whether Large-Address-Aware makes sense (big packs, out-of-memory
   crashes; never modify the original exe in place, only copies in test folders), measure texture-page switches per
   frame.
Write `docs/design/vm-optimizer.md` (measurements, what is worth it, plan) and commit it BEFORE larger code changes.
Optimiser off by default (profile setting) until proven; plain Perf + vanilla byte-identical (test).
`dotnet build -c Release` zero warnings, `dotnet test -c Release` green. On EXITED `~/jamm-night/bin/steam-restart.sh`
and retry; delete `~/jamm-night/runs/*/game` copies. Commit on this branch; do not push. AGENTS.md applies.
