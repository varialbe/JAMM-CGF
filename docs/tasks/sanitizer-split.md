# Task round 2: split the sanitizer instead of flipping its default (CGF-PERF-REGRESS-002)

Orchestrator review of a71b728: the root cause is right, but flipping "Keep the author's performance changes"
to ON by default reverses a deliberate Phase 18 decision (docs/reports/phase18.md). The sanitizer exists to
remove HARMFUL hacks: fpsController busy-wait, timing/room_speed changes, NewWorld speed 1, fps-adaptive logic,
debug overlays, and culls that change gameplay (e.g. 5000-px culls that hide fish from sonar).

The benign perf edits must stay — they are why the CLI build is 3× faster: fish tick throttling for
off-screen fish, FishThinner, off-screen draw culling, scene-object step throttles that don't change gameplay.

## Job
1. Re-classify sanitizer hunk categories into **harmful** (always stripped by default) and **benign perf**
   (kept by default). Per category decide using phase18.md + the real TGE/PWB hunks
   (~/jamm-bench/inputs/private — local only). Anything that affects sonar, fishing results, catch rates,
   spawn counts visible to the player, timing/room_speed, or busy-waits = harmful.
2. Revert the global default flip from a71b728 (keep the legacy-marker plumbing only if still useful).
   Default = sanitize harmful only. The Advanced checkbox keeps meaning "keep ALL author perf changes".
3. Bench (`~/jamm-bench/run.sh`, dock + deepwater, 30 s, 2 runs): profile build must be within 10 % of CLI.
   If not, report which harmful category costs the time and why it must stay stripped.
4. Sonar/gameplay sanity: run the `sonar` scene on the profile build and confirm fish still show up
   (compare `active_fish_mean` and the sonar CSV vs CLI build).
5. Tests updated, `dotnet test -c Release` green. Append "Round 2" to profile-build-slow-report.md, commit.
   Do not merge or push.
Same rules as profile-build-slow.md.
