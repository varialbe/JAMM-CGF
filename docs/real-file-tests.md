# Real-file verification

Set `JAMM_ORIGINAL` to the legacy 2023 `data.win` and `JAMM_MODS_DIR` to a
local directory of legally obtained mod archives, then run `dotnet test -c Release`.
No game data belongs in this repository.

The full release suite requires these filenames in `JAMM_MODS_DIR`:

- `the-abyss-beta.win`
- `cave-expansion.win`
- `colour-expansion.win`
- `jamm-hd-lite.win`
- `jamm-perf.win`
- `jamm-smooth-fps.win`
- `oops-all-chaws.win` (Oops! All Chaws!)

The seven-mod sanitizer test imports these archives into a temporary manager
library. It never uses the live user library or its profile settings. Other
real-file tests also inspect every `*.win` in the fixture directory. Build and
installation tests write only temporary copies and verify source hashes.

Without both environment variables, real-file tests are skipped; synthetic and
headless tests still run. Missing fixtures fail explicitly when opted in.
