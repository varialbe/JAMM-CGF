# Autoplay crash hunter (VPS)

A bot plays a modded build on the VPS test machine (Xvfb + Wine + the real Steam client) and records crashes,
so players do not have to find them.

## Pieces
| Piece | What it does |
|---|---|
| `jamm-modmaker bench <in.win> <out.win> --scene autoplay` | Adds a persistent probe that closes the new-game menu and appends the state to `jamm_autoplay.csv` every two seconds (time, room, FPS, cat position, money, level, lure state and depth, instance count). |
| `tools/vps/autoplay.sh <win> <label> <minutes> [seed]` | Instruments the build, starts the game under the shared game lock, and plays with real key presses from a seeded generator: cast and reel (Space), wait, boat left/right (A/D), dive (Q + arrows), buy lures (B/H/L/R), switch rods (Z/X), open/close the save menu (Esc), and JAMM Console commands (F12): `money`, `money +`, `unlockgear`, `unlockall`, `level`, `catalog all`, `weather <id>`, `help`. On a `Code Error` window it saves `error.txt`, `error.png`, `actions.tsv`, the state CSV and screenshots to `~/jamm-night/autoplay/<label>-<seed>/` and appends a line to `~/jamm-night/autoplay/status.tsv`. |
| `tools/vps/autoplay-bisect.sh <label> <seed> <minutes> <a.win,b.win,...>` | Rebuilds without one mod at a time and replays the seed until only the mods needed for the crash remain. |
| `tools/vps/autoplay-matrix.sh <minutes> <seeds> <label=win>...` | Runs several builds and seeds (RAM guard) and writes `summary-<date>.md`. |

## Notes
* The same seed gives the same action sequence. Game timing still varies (the VPS renders in software at
  1–20 FPS), so a crash is not guaranteed to repeat; bisect results are strong hints.
* Each run starts from the empty test save; the player's saves are never touched.
* Real-time minutes ≠ game minutes: all-7 runs at about 1–4 FPS on the VPS.
