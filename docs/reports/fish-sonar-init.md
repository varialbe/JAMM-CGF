# Fish sonar initialization

JAMM now emits one shared `jamm_fish_sonar_init(inst)` script for weather fish, catalog API fish, and Endless Ocean fish. It matches vanilla regular and advanced sonar draw state using runtime asset lookups and panel existence guards. Each spawn path calls it immediately after creation and only while the returned instance exists. The previous Endless Ocean-only initializer was removed. No other JAMM path creates Fish descendants; the other dynamic creation sites are for boats, gear, navigation, menus, and scenery.

## Verification

- `dotnet build -c Release`: passed with 0 warnings and 0 errors.
- `dotnet test -c Release --no-build`: 643 core and 130 GUI tests passed; 17 core and 1 GUI optional tests skipped.
- `node --test tests/worldgen/chunks.test.mjs`: 8 passed.
- Personal package built and verified with `TAG=fish-sonar`; SHA-256: `485a22442ccd88afb44f9cd75e1c72b9020d04dbd73bdd797b7ace6138f62d7c`.
- Autoplay v2, seed 11002, 20 minutes: `PLAYED`, 176 actions, no error.
- Autoplay v2, seed 11005, 20 minutes: `PLAYED`, 156 actions, no error. Reached x=199,607 with five chunks loaded, past the reported crash region.
- Seed 11002 could not equip Boat7 and stayed near the dock; seed 11005 provided the deep-water coverage.
- Both autoplay temporary `game` copies were removed. Disk free space after the runs: 19 GB.
