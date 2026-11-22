# Showcase scenes implementation report

## Implemented

- Added `--scene showcase-harbor` and `--scene showcase-submarine` to Jamm.ModMaker.
- Harbor Town stages a funded tour through the real Harbor Town menu: open with E, buy three levels of each card, walk the camera past the landmarks, then raise Lighthouse, Aquarium, and Statue through levels 1, 5, 20, and 100 with pauses. It writes action and level telemetry to `jamm_showcase_harbor.csv`.
- Submarine presents the Boat Shop card, buys the submarine, sails east, dives, toggles the lamp, casts and reels, and waits for full surfacing and restored oxygen. Its 70-second per-stage watchdog allows slow ascents to finish. It writes state and oxygen telemetry to `jamm_showcase_submarine.csv`.
- Added `tools/bench/record-showcase.sh`. It accepts a prebuilt archive or builds one from the four JAMM built-ins plus the Submarine and Harbor Town packages; it does not include TGE or PWB. It records through `~/jamm-bench/run.sh`, detects and crops black borders, encodes silent H.264 at 1280 px wide, and creates per-scene and combined contact sheets.
- Added synthetic-archive tests for both scene compilers and their key gameplay flow.

## Recording review

The reviewed outputs are outside the repository, under `~/jamm-bench/videos/`:

- `showcase-harbor.mp4` — 74.1 seconds. The contact sheet shows the menu, cat movement with the camera following, and Harbor Town landmarks. Harbor telemetry recorded all 12 requested level stages, ending with all three buildings at level 100.
- `showcase-submarine.mp4` — 57.7 seconds. The contact sheet shows the shop card, underwater travel, fish, and a Red Skitter catch banner, followed by the surfaced submarine. Telemetry records full surfacing and oxygen restoration. Its cast checkpoint found no bite within 12 seconds; the visible catch occurs during the later reel.
- `showcase-contact-sheet.png` — combined Harbor Town and Submarine review sheet.

The older Submarine bench scene’s 30-second ascent timeout was a scene timeout, not evidence of a stuck submarine: the showcase reached the surface and restored oxygen. One recorded ascent completed about 15 seconds after surfacing began; an earlier capture took about 30 seconds, so the showcase waits on the actual surfaced state rather than a short fixed delay.

## Verification

- `dotnet build tools/Jamm.ModMaker/Jamm.ModMaker.csproj -c Release -p:CustomAfterMicrosoftCommonTargets=/tmp/jamm-showcase-dependency.targets` — passed with zero warnings in project code.
- `dotnet test tests/Jamm.Core.Tests/Jamm.Core.Tests.csproj -c Release -p:CustomAfterMicrosoftCommonTargets=/tmp/jamm-showcase-dependency.targets` — 645 passed, 17 opt-in real-file tests skipped, 662 total.
- `bash -n tools/bench/record-showcase.sh` and `git diff --check` — passed.

The worktree’s UndertaleModTool submodule is empty, so these project commands used a copy of the supplied dependency targets with its worktree path adjusted. Generated game archives and recordings remain outside the repository.
