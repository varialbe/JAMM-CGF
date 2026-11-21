# Task: profile builds are ~3× slower at runtime than CLI builds (CGF-PERF-REGRESS-002)

## Evidence
- Same 7 packs (TGE, Antiquity, PWB, Abyss, Colour, Verdant, cave-expansion), measured with `~/jamm-bench/run.sh`, dock scene:
  - `cgfmm build --base <vanilla> --order <7 packs>` → ~62 000 µs/tick loop.
  - profile-native build of the same order (see `docs/tasks/perf-without-smooth-report.md`, Weak-PC section) → ~199 000 µs/tick.
  - The old VPS all-7 profile.win (`~/jamm-bench/builds/all7-vps.win`) measured ~4 FPS vs ~19 FPS for a fresh CLI build.
- This is very likely the "9 FPS" the user sees in normal play (GUI uses profile builds).

## Job
1. Reproduce: build the same order once via plain `cgfmm build` and once via the profile/GUI build path (ProfileLibrary/ManagementCommand; find how the Weak-PC report built "profile-native"). Bench both (dock, 30 s, 2 runs) — numbers must reproduce the gap.
2. Diff what the profile path adds (builtins it auto-includes, object guards, InstanceCompatibility, extra runtime controllers, intermediate/addon passes, duplicated injections…). Use `--profile --profile-scripts` bench runs to find the hot events/scripts.
3. Fix the root cause so a profile build with the same mods is within 5 % of the CLI build. Do not drop features silently; if something must be opt-in, make the default the fast path and document it.
4. Tests for the fix. `dotnet test -c Release` green.
5. Report in docs/tasks/profile-build-slow-report.md with a before/after table. Commit on fix/profile-build-slow. Do not merge or push.

## Rules
- Bench only via `~/jamm-bench/run.sh` (global lock, other agents share the machine). Tools: copy bin outputs to ~/jamm-bench/tools/cli-pp and mm-pp (repo path contains a space). If external/UndertaleModTool is empty in this worktree, use a CustomAfterMicrosoftCommonTargets file pointing at the main checkout like /tmp/jamm-perffix-dependency.targets.
- TGE/PWB inputs in ~/jamm-bench/inputs/private stay local: never commit them, never put them or their media into the repo or site.
- Disk is limited: delete your builds in ~/jamm-bench/builds when done.
- Never touch ~/.var/app/com.valvesoftware.Steam/data/CGF-Personal or compatdata cgf-personal.
