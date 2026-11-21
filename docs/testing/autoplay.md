# Autoplay crash hunter (VPS)

A bot plays a modded build on the VPS test machine (Xvfb + Wine + the real Steam client) and records crashes,
so players do not have to find them.

## Pieces
| Piece | What it does |
|---|---|
| `jamm-modmaker bench <in.win> <out.win> --scene autoplay` | Adds a persistent probe (`obj_jamm_autoplay`) that closes the new-game menu and appends the state to `jamm_autoplay.csv` every two seconds (or every 0.5 seconds while `BoatMenu` is open): `ms`, `room`, `fps`, `x`, `y`, `depth`, `dollars`, `level`, `lure_state`, `lure_y`, `instances`, `boat`, `chunks`, `weather`, capability flags (`has_sub`, `has_weather`, `has_ocean`, `has_silo`, `has_console`), viewport-normalized shop coordinates (`boat_icon_xy`, `boat7_card_xy`, `sub_card_xy`, `boat_menu`, `cancel_icon_xy`), `silo_x`, and `ocean_start`. All optional lookups are guarded with `asset_get_index` / `object_exists` / `instance_exists` / `variable_instance_exists` so the probe runs cleanly on vanilla, `public-5`, and `all-7` builds. |
| `tools/vps/autoplay.sh <win> <label> <minutes> [seed]` | Instruments the build, starts the game under the shared game lock, and plays with real key presses and window-relative clicks from a seeded generator: cast and reel (Space), sell (S), boat left/right (A/D), dive (Q + arrows), buy lures (B/H/L/R), switch rods (Z/X), open/close the save menu (Esc), JAMM Console commands (F12: `money`, `money +`, `unlockgear`, `unlockall`, `level`, `catalog all`, `help`), **far-east trip** (unlock/equip `Boat7` via console + Boat shop and hold `D` in long stretches for Endless Ocean chunk streaming, occasionally turning back west with `A`), **Submarine** (if `jmk_submarine_submarine` is reported by the probe: unlock/equip it, clear the dock, dive with `V`, steer with arrows, toggle `L`, cast/reel with Space, surface with `V`), **Silo** (fish near the Silo hatch if nearby, or skip cleanly since no console teleport command exists), and **Weather** (`weather <id>` and `weather auto`). On a `Code Error` window it saves `error.txt`, `error.png`, `actions.tsv`, the state CSV and screenshots to `~/jamm-night/autoplay/<label>-<seed>/` and appends a line to `~/jamm-night/autoplay/status.tsv`. |
| `tools/vps/autoplay-bisect.sh <label> <seed> <minutes> <a.win,b.win,...>` | Rebuilds without one mod at a time and replays the seed until only the mods needed for the crash remain. |
| `tools/vps/autoplay-matrix.sh <minutes> <seeds> <label=win>...` | Runs several builds and seeds (RAM guard) and writes `summary-<date>.md`. |

## Notes
* The same seed gives the same action sequence. Game timing still varies (the VPS renders in software at
  1–20 FPS), so a crash is not guaranteed to repeat; bisect results are strong hints.
* Each run starts from the empty test save; the player's saves are never touched.
* Real-time minutes ≠ game minutes: all-7 runs at about 1–4 FPS on the VPS.
