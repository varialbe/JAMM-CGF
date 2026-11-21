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
