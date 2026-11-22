# Harbor Town v0.2 verification

## Implementation

Harbor Town v0.2 adds a five-stage Research Station made from local sprite recipes and SVG parts. Its sell-price,
bite-rate, and lure-luck modifiers use the documented logarithmic curves and hard caps. Fish sale value is clamped
to $110M after applying the bonus to the $100M base cap.

Donations accrue toward $1M, $1B, $1T, and $1Qa. They deduct money from the wallet, persist in the
`JAMM_Harbor` save section, unlock the four Harbor titles, recolour an equipped hat, and add flags and lanterns
to the dock. Missing v0.1 save keys continue to load as zero. The console command supports Research levels,
money grants, and donations.

The ModMaker Harbor scene exercises the five stages of each of the four buildings, unlocks all donation rewards,
and tests capped bonuses at Research level 200 with $1B and $1T wallets.

## Automated verification

- `dotnet build -c Release`: passed, 0 warnings and 0 errors.
- `dotnet test -c Release` after the crash-hunt merge: passed, 651 tests; 17 opt-in real-file tests skipped.
- ModKit package check: passed; 7 sprite recipes.
- Solo Harbor source build: passed; 7 sprites, 1 object, 12 scripts.
- `REPO=$PWD TAG=harbor2 bash ~/jamm-night/n21/build-personal.sh`: passed after the crash-hunt merge.
- Personal archive merge: passed semantic reference verification; all 20 Harbor assets present.
- ModMaker Harbor scene instrumentation: passed.

## In-game verification and screenshots

Initial attempts exited during `SteamAPI_InitSafe`; neither produced a Harbor Code Error. After restarting Steam,
solo reached the fishing scene and recorded, but the Harbor bench CSV contained only its header. The probe was
non-persistent and was destroyed during the menu-to-fishing-room transition. Commit `f2746e9` makes it persistent.
An interactive solo retry again reached the fishing scene without a Code Error, but recorded only the CSV header.

The latest earlier personal retry used the merged post-merge archive and a window recovery watcher. The game process
stayed alive for the run, but the game window rendered black. Its diagnostic sample was `room=0; menu=0; save=1;
girl=1; harbor=1; loaded=0; harbor_ready=0`, and the CSV remained header-only. `run.sh` reported `RECORDED` because
a header-only CSV was present; this is not counted as successful verification.

The requested screenshots of Research Station stages, titles, and dock decorations were not captured because neither
in-game run reached the Harbor scene. Diagnostic captures and the latest personal CSV/probe are in
`~/jamm-night/harbor-town-v2/`; those images show launch failures, not v0.2 feature evidence.

## Resumed verification

- `dotnet build -c Release`: passed, 0 warnings and 0 errors.
- `dotnet test -c Release`: passed, 651 tests; 17 opt-in real-file tests skipped.
- ModMaker Harbor scene compiled into a throwaway archive successfully.
- `harbor-town-v2-solo-seed2` completed as `RECORDED`, but its CSV contained only the header. The 60 second capture
  showed the fishing room with a Wine `steamwebhelper` error dialog, not Harbor features.
- `harbor-town-v2-personal-seed2` also completed as `RECORDED` with only the CSV header. Its 60 second capture was
  black with the Wine `steamwebhelper` error dialog; it produced no feature screenshots and no Harbor Code Error.
- Updated the bench fixture to run without a loaded save, create the Harbor instance when missing, force its menu
  visible after normal Step events, and log room/player/save/Harbor state while waiting.
- The first solo run with this fixture exited during `SteamAPI_InitSafe`. Its logs were preserved externally, its
  `game` copy was deleted, and a Steam restart plus retry is queued through the shared lock. A personal run using
  the diagnostic fixture is also queued.
- Diagnostic captures and CSVs remain under `~/jamm-night/harbor-town-v2/`; none are game files in the repository.
