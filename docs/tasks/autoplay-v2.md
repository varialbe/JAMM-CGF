# Task: autoplay crash hunter v2 (branch `exp/autoplay-v2`)

Read `docs/testing/autoplay.md`, `tools/vps/autoplay.sh` and `tools/Jamm.ModMaker/AutoplayScene.cs`. The bot plays
with random real input (xdotool) and a probe that writes `jamm_autoplay.csv`. It covers dock fishing, shops,
diving, console commands and the save menu, but never reaches the new content.

## Do
1. New weighted action groups in `autoplay.sh` (seeded, reproducible, logged to actions.tsv like the others):
   - **Far east trip:** buy/equip the final vanilla boat via console (`money`, `unlockgear`) and hold `d` for long
     stretches so Endless Ocean chunks stream in and out; occasionally turn back west.
   - **Submarine:** if the Submarine package is installed (probe reports `obj_jmk_submarine` or similar – check the
     package in `examples/modkit/submarine`), buy/equip it, press `v` to dive, steer with arrows, toggle `l`,
     cast/reel with space, surface with `v`.
   - **Silo:** fish near the Silo hatch area (console teleport if one exists, otherwise skip).
   - **Weather:** console `weather <id>` / `weather auto`.
2. Extend the probe CSV with: x, depth, current boat, chunk count/instance count if available, weather id. Guard
   every lookup with `variable_global_exists` / `object_exists` / `instance_exists`, because the bot runs on
   vanilla, public-5 and all-7 builds where these objects may be missing.
3. `bash -n` / shellcheck clean; `dotnet build -c Release` zero warnings; `dotnet test` green. Update
   `docs/testing/autoplay.md`. You cannot run the game; the orchestrator runs it.

## Rules
AGENTS.md applies. Every command in the foreground (no background tasks). Simple single commands (no `&&`, pipes,
redirections, no `rm`). Do not commit; the orchestrator commits.

## Round 2 (orchestrator in-game review, run apv2-own-11001, 15 min, PLAYED, no crash)
Evidence: `~/jamm-night/autoplay/apv2-own-11001/` (actions.tsv, jamm_autoplay.csv, game.log, shots/).
1. `autoplay.sh` line 252 prints `((: 33984: syntax error: invalid arithmetic operator` five times. The probe CSV
   is written by the Windows game with CRLF, so the last field (`silo_x`) carries a trailing `\r`. Strip `\r` in
   `read_probe` (and validate every numeric field as an integer before using it in `(( ))`, default 0).
   Because of this the Silo branch always logged "silo skip (not installed)" although `has_silo=1`.
2. Far-east trips never reached Endless Ocean: `x` maxes at 27419 and `chunks` stays 0 for the whole run
   (`ocean_chunks` exists, the map is just empty). Find out from `src/Jamm.Core/Builtins/EndlessOceanMod.cs`
   (edge / ContentChunk / room width) where the generated region starts and why the boat stops at 27419; then make
   the trip actually reach it (console command if one exists, e.g. a teleport/`tp`, check the console mod sources,
   or a longer hold with the right boat). The probe should then show `chunks > 0` at some point.
3. Keep everything else. Same rules: foreground only, simple single commands, no commit. `bash -n`, shellcheck,
   build with zero warnings, tests green.
