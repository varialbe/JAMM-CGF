# Task C: crash hunt Submarine + Harbor Town (branch `fix/feature-crash-hunt`, Codex Luna)

This branch already contains the shared fish sonar initializer (`fix/fish-sonar`), so the known
`SonarPanel_Draw_0 … s5 not set` crashes are fixed here.
Run long autoplay sessions with the bot `tools/vps/autoplay.sh <win> <label> <minutes> <seed>` (results in
`~/jamm-night/autoplay/<label>-<seed>/`, summary line in `~/jamm-night/autoplay/status.tsv`; set
`JAMM_MODMAKER_DLL` to this worktree's built `tools/Jamm.ModMaker/bin/Release/net10.0/jamm-modmaker.dll`) on:
1. Submarine solo, Harbor Town solo (`cgfmm modkit build examples/modkit/<mod> --base
   ~/cgf-files/legacy/2023-08-16_manifest-9039707068725406600/data.win --out …`),
2. Submarine + Harbor Town together with Endless Ocean, Weather Pack and Silo,
3. the 7-pack profile plus Submarine + Harbor Town (see how `~/jamm-night/n21/build-personal.sh` and earlier
   all-7 builds compose profiles; TGE/PWB stay on the VPS, never commit them or their images).
At least 3 seeds per profile, 20 minutes each. Make sure the bot actually buys/uses the submarine and opens the
Harbor Town menu (extend the bot if it does not). For every Code Error: find the root cause, fix it in JAMM,
add a regression test, rerun the seed. Do not change vanilla behaviour.
The game runner holds a shared lock (other agents test too – it may wait). On STEAM/EXITED run
`~/jamm-night/bin/steam-restart.sh` and retry. Delete `~/jamm-night/runs/*/game` copies after runs; keep >15 GB free.
`dotnet build -c Release` zero warnings, `dotnet test -c Release` green. Report `docs/reports/feature-crash-hunt.md`
(table profile × seed × result, every crash with error text, cause, fix) with screenshots from the runs.
AGENTS.md applies. Commit on this branch; do not push or merge into main.

## Note from the orchestrator (2026-11-22 10:00)
`tools/vps/autoplay.sh` calls `ffmpeg` without `-nostdin`. Started from an agent's tmux pane, ffmpeg can receive SIGTTIN
and stop the whole job while it holds `~/jamm-night/game-run.lock` (happened today: a run blocked all agents for 80 min).
Add `-nostdin` to every ffmpeg call in `tools/vps/autoplay.sh` as part of this branch (`~/jamm-night/bin/run.sh` is
already fixed).
