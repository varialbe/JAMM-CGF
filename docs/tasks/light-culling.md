# Task: lighting-system culling (branch `exp/light-culling`, Gemini Hydrogen)

BRIEFING section 11 ("then Hydrogen: … light-system culling"). Decor → tiles is handled by Codex on
`exp/engine-build-opt` and DecorSleep already culls static decor – do not duplicate those.
Goal: the vanilla `LightingSystem` (Draw events, `AbstractLight` children, light surfaces) does work for lights far
outside the view every frame, especially in all-7 and Endless Ocean. Make it skip lights that cannot contribute to the
visible frame (view rectangle + light radius + margin), without any visible difference.
1. Measure first: decompile `LightingSystem_*` and `AbstractLight` children in the legacy base
   (`~/cgf-files/legacy/2023-08-16_manifest-9039707068725406600/data.win`) and the packs; count lights per frame and
   their cost with the ModMaker event profiler (`--profile-scripts`) on vanilla, the personal package
   (`~/jamm-night/personal-tge/own-mods-2026-11-22c.win`) and all-7 (`~/cgf-files`, TGE/PWB stay on the VPS – never
   commit them or images of them). Write the numbers into `docs/design/light-culling.md`.
2. Implement as an opt-in Perf profile setting (pattern: DecorSleep in `PerfSettings`); plain Perf + vanilla must stay
   byte-identical (test). Lights that own instances, the lure light, Lighthouse (Harbor Town), Submarine headlights,
   and night/deep-water logic must keep working.
3. Verify: screenshot parity (dock night, deep water, cave, Endless Ocean), autoplay 20 min without Code Error
   (`tools/vps/autoplay.sh`), unprofiled `loop_us` repeated pairs before/after. `dotnet build -c Release` zero
   warnings, `dotnet test -c Release` green. Report `docs/reports/light-culling.md` with numbers + screenshots
   (copy PNGs to `~/jamm-night/light-culling/`).
Game runs serial via `~/jamm-night/bin/run.sh` (shared lock, wait; never kill other runs; on EXITED
`~/jamm-night/bin/steam-restart.sh` and retry; delete `~/jamm-night/runs/*/game` copies). AGENTS.md applies.

## Resume note (orchestrator, 2026-11-22 13:00)
Paused at 04:05 for the Gemini quota; your WIP is commit `b579056`. Note: `~/jamm-night/PAUSE-GAMES` currently blocks
all VPS game starts (the user tests on the PC with the same Steam account) – do not delete it and never start the game
past the lock; `run.sh` simply waits. Continue with code, build and unit tests; queue in-game measurements, they run
once the pause ends. Its ModMaker build output was removed during a disk cleanup, rebuild first.
