# The flooded Silo

The Silo recipe places one maintenance hatch at (34136, 11496) in NewWorld and appends a dedicated flooded room. The seven-input placement scan and the original mod's runtime review are in [silo-review.md](silo-review.md).

The hatch and its lamp have their own objects. The diver moves through a launch shaft, instruments gallery, control room, pump bay and stores. Three air bells refill breath. Four salvage pickups pay $12,000 in total, with claims stored per save slot. Using the existing money balance avoids adding or relocating fishalog, fish, or quest entries. The room uses original drawing code and sprite art, plus the legacy game's underwater loop.

The persistent Silo controller checkpoints through the vanilla SaveManager before entry. The live NewWorld, including SaveManager, stays in its own room between visits. The controller restores the prior room and manager persistence settings on return. Save and quit from the dive menu banks any recovered salvage. Reopening that snapshot loads the vanilla save in NewWorld and starts the swimmer at the hatch. Air exhaustion returns the swimmer and leaves the current dive's unbanked salvage available for another visit.

## Reproduce the runs

Use the optional recipe ZIP from `site/cgf-mods/packages/jamm-silo.zip` or generate an archive with ModMaker's `silo` command. The real-file test reads the inputs locally when `JAMM_ORIGINAL` points to legacy `data.win` and `JAMM_SILO_INPUTS` points to the `cgf-files` directory.

The VPS runner allows 600 seconds per archive. Each invocation runs alone on the shared Wine display. It stores the scene CSV and a separate test save directory under `~/jamm-night/runs/<label>-silo/`.

```sh
~/jamm-night/bin/run.sh <archive.win> <label> silo 600
JAMM_SILO_MODE=cycle-save ~/jamm-night/bin/run.sh <archive.win> <save-label> silo 600
JAMM_SILO_MODE=reload JAMM_SILO_SAVE_SEED=~/jamm-night/runs/<save-label>-silo/save \
  ~/jamm-night/bin/run.sh <archive.win> <reload-label> silo 600
```

The cycle uses the game's E and Esc controls. It records the state before entry and after return, checks repeat entry and claim persistence, and forces breath depletion to exercise the emergency ascent. The save scenario follows that cycle with another visit and a different pickup before Save and quit. The reload scene reads the prior sandbox without reseeding the player's state.

## Standalone save round-trip

The standalone run started with $123,456, one fish aboard, one active quest at 0.75 progress and two daily quest accepts. The first visit paid $3,200 and returned to NewWorld with the same fish, quests, boat and rod. The repeated visit retained the salvage claim. The emergency ascent kept the same world state, including the unlocked persistent SaveManager and all 90 navigation edges.

A third visit recovered $2,400 of additional salvage. Save and quit left a slot with $129,056 and a hatch marker matching the vanilla save timestamp. Reloading that slot in a separate game process returned the swimmer to (34136, 11433.24) in NewWorld with fresh breath. The fish, both quest counts, quest progress, boat and rod matched. The reload consumed the hatch marker and kept both salvage claims.

## Archive and test results

- The recipe patch adds four objects, one room and three sprites. Its thirteen new event entries belong to Silo objects. The only change to an existing room is the maintenance hatch placement. Synthetic tests compare the original instruction bytes using symbol IDs in place of file-wide reference links.
- Direct ModMaker generation succeeded on Cave Expansion and the previously merged all-seven base. The opt-in suite verified recipe merges with each input individually and the combined first-party download packages.
- The matched all-seven check reported zero conflicts involving The Silo. The two original false reports came from references elsewhere in NewWorld, where other mods add Garder or Underfin placements that Cave Expansion removes. The checker now tests the actual added placements of an additive room patch before attributing a reference conflict.
- The public five-pack and all-seven build accepted all ten tracked Silo assets and changes. The all-seven build still has pack-level conflicts and existing table and instance warnings. Its unrelated rod-slot decision drops Cave's Mystic Rod.
- The Release build emitted zero warnings. The Release suite passed 430 core and 127 GUI tests, with 15 opt-in tests skipped by default. The Settings GUI test now waits for the async click handler to finish updating the field as well as for the view model to finish saving.

The pack check, public-five build and all-seven build logs are in `~/jamm-night/silo-area/`. The corrected pack logs use the `silo-release-` prefix.

## VPS runs

All runs below used the 600-second budget and a single game process at a time. The CSVs and screenshots are under `~/jamm-night/runs/`. Each run directory has `jamm_bench_silo.csv` and a `save/` subdirectory containing `jamm_silo_*.png`.

| Setup | Run directory | Outcome |
| --- | --- | --- |
| Silo alone | `silo-area-solo-retry-silo` | Entered, recovered salvage, paused, returned, reentered and exercised emergency ascent. Money went from $123,456 to $126,656. The subsequent visit saved and quit with $129,056. |
| Silo + public five-pack (Antiquity, PWB, Abyss, Colour, Verdant) | `silo-area-public5-retry-silo` | Swam to the control room, recovered salvage for $3,200, returned and passed repeat entry and emergency ascent. NewWorld persistence stayed enabled and the navigation edge count stayed at 28. |
| Silo + all seven (TGE, Antiquity, PWB, Abyss, Colour, Verdant, Cave) | `silo-area-all7-retry-silo` | Swam to the control room, recovered salvage for $3,200, returned and passed repeat entry and emergency ascent. NewWorld persistence stayed enabled and the navigation edge count stayed at 2. |
| Standalone reload | `silo-area-reload-retry-silo` | Loaded the saved $129,056, fish and quest progress in NewWorld at the hatch. |
| Final standalone save | `silo-area-final-save-retry-silo` | Swam to the control room, recovered salvage and saved through the dive menu. The slot and save-card preview both show $126,656, with a matching hatch checkpoint timestamp. |

The public and all-seven runs returned with the same one fish aboard, one active quest at 0.75 progress, two daily quest accepts, boat 1 and rod 0. SaveManager was active, persistent and unlocked when the diver returned. The scene used the keyboard to swim through the area and collect salvage in both packs.

The cycle screenshots are `save/jamm_silo_hatch-closed.png`, `save/jamm_silo_hatch-open.png`, `save/jamm_silo_area.png`, `save/jamm_silo_control-room.png` and `save/jamm_silo_newworld.png`. The standalone run also captured the dive pause and save menus. The reload image is `silo-area-reload-retry-silo/save/jamm_silo_reloaded.png`.

Steam sometimes failed to initialize on the first launch after another game exited. The runner restarted the existing Steam client and retried. The pack runs predate the final save-card balance sync. The all-seven check was repeated with the final patch, and the final standalone save verified the balance sync. The original and seven inputs still match their phase-one SHA-256 hashes.

## Open checks

The scene starts the swimmer near the hatch. The full descent from the surface with an unmodified player save remains a manual playtest. Wine used dummy audio output, so the underwater loop's audibility and mix still need an audio-enabled run. The all-seven pack's unrelated merge warnings remain in its build report.
