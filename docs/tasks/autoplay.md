# Task: Autoplay crash hunter (bench scene `autoplay` + runner + auto-bisect)

Branch `exp/autoplay`, worktree `~/jamm-wt/autoplay`. Read AGENTS.md and the existing bench scenes in
`tools/Jamm.ModMaker` (BenchmarkMod, WeatherScene, SiloScene, WorldgenScene, MaizeScene on exp/maize-nav-fix) first.

## Goal
The user should stop finding crashes himself (recent ones: Maize sonar `Maize.edges`, `unlockall` in `Rod_Step_0`
`upgrades[22]`, Weather Pack `Bo.object_index`). A bot plays the game on the VPS and finds them first.

## Required
1. **Bench scene `autoplay`** (`jamm-modmaker bench <in.win> <out.win> --scene autoplay --seed <n> --minutes <m>`):
   an injected controller that plays randomly but sensibly, seeded and reproducible (own PRNG in GML, not
   `random()`): cast, wait for bites, reel in (also let fish escape sometimes), sell, buy in the shops (rods, boats,
   upgrades, hats if affordable), open/close sonar and advanced sonar, change weather when the Weather API exists
   (`jamm_weather_command`), travel by boat to far regions: deep trench / silo area / Endless Ocean if present,
   night and day (advance time), save and load (save slot of a dedicated test save), JAMM console commands when
   present (`unlockall`, `unlockgear`, `money`, `level`). Dismiss the new-game mode menu (`GameModeMenu`, `GM_*`,
   `Message_Start`) like WeatherScene. Every action is logged to `jamm_autoplay.csv` (step, seed, action, x, y,
   money, room, fps) and flushed regularly so the last action before a crash is known.
2. **Runner** `~/jamm-night/bin/autoplay.sh <win> <label> <minutes> [seed]` (also commit a copy under `tools/vps/`):
   based on `~/jamm-night/bin/run.sh` (shared game lock `~/jamm-night/game-run.lock`, never kill another agent's game,
   Steam restart handling). Detects `Code Error`/FATAL ERROR windows: saves error text (clipboard as run.sh does),
   screenshot, the CSV tail, seed and step to `~/jamm-night/autoplay/<label>-<seed>/`. Exit code + one TSV line in
   `~/jamm-night/autoplay/status.tsv` (label, seed, minutes, outcome, first error line).
3. **Auto-bisect** `tools/vps/autoplay-bisect.sh <profile-or-order> <seed>`: on a crash, rebuild with subsets of the
   mod list (and with/without JAMM builtins perf/hd-lite/console/smooth) using `cgfmm build --order ...`, rerun the
   same seed until the smallest crashing combination is found; write the result to the run folder.
4. **Night matrix** `tools/vps/autoplay-matrix.sh`: user's own mods (perf, hd-lite, console, smooth, silo,
   weather-pack, endless-ocean), public-5, all-7 – N seeds × M minutes, respecting RAM (start only with > 2.5 GB free)
   and the game lock. Summary markdown + Telegram-ready text (playtime, crashes found, screenshots) in
   `~/jamm-night/autoplay/summary-<date>.md`.
5. Run it tonight for at least: personal build 2 seeds × 15 min, public-5 1 × 15 min, all-7 1 × 15 min (the VPS has no
   GPU – all-7 runs at ~1–4 FPS, so game minutes are fewer; report real and game time). For every crash found:
   write a short spec `docs/tasks/crash-<name>.md` (cause, file, repro seed) – fix it only if small and obvious.
6. Tests for the C# parts (scene injection compiles on the legacy base, options parsing), full suite green, zero
   warnings. Docs: `docs/testing/autoplay.md`.
7. Report `docs/reports/autoplay.md`. Commit on `exp/autoplay`; do not merge into main or push main.
