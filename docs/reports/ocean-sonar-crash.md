# Endless Ocean sonar crash

## Cause

In the August 2023 base, `SonarPanel`'s Step subtype 1 initializes fish sonar draw state: `s1` through `s4` select the sprite, frame, and scale, while `s5` is the sonar alpha. The panel's Draw event later reads these instance variables from visible Fish instances. A fish streamed by Endless Ocean is created synchronously in a Begin Step callback. If that creation occurs after the panel's initialization pass, the fish can reach Draw without `s5`. Smooth's extra render frames expose this ordering gap between logic updates and draws. Perf does not initialize or alter these fields.

## Fix

The Endless Ocean generator now initializes `s1`–`s5` for each newly created fish immediately, using the same regular sonar and advanced sonar values as the vanilla panel. This runs before the fish is tagged and exposed to later draw work. Vanilla fish and vanilla sonar events are unchanged.

The ModMaker `worldgen` scene now enables the sonar view during its streaming route and records `sonar_missing` for generated fish at each checkpoint.

## Verification

- `dotnet build -c Release`: passed, 0 warnings and 0 errors.
- `dotnet test -c Release`: passed, 618 core and 130 GUI tests; 17 optional real-file tests skipped.
- Synthetic archive regression test compiles the new sonar initializer and checks regular and advanced draw state plus its call from the chunk loader.
- Personal package rebuilt with `TAG=sonar-fix`; archive verification passed. SHA-256: `745d8937b08904b0c8a9bafa9d0b60c449e765cb321c93c51730b92ddefa2e00`.
- `run.sh ... sonarfix worldgen 900`: completed and recorded all worldgen checkpoints. `sonar_missing` was 0 at every checkpoint, including chunks 19–504.
- Autoplay v2, seed 11002, 15 minutes: `PLAYED`, 145 actions, no error.
- Autoplay v2, seed 11003: startup-only `STEAM` failure (`SteamAPI_InitSafe() failed`), before gameplay.
- Autoplay v2, seed 11004, 15 minutes: `PLAYED`, 96 actions, no error. Reached x=81,704 with five chunks loaded; it did not travel to the original crash coordinate.

The autoplay bot could not select Boat7 on seed 11002 (`boat icon unavailable`) and stayed at the dock. Seed 11004 selected Boat7 and entered Endless Ocean but did not reach x≈152,600; the dedicated worldgen/sonar scene covered the deeper generated checkpoints.
