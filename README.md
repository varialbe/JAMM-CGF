# JAMM-CGF

Just Another Mod Manager for Cat Goes Fishing investigates combining mods that each replace the game's `data.win`. Phase 0 provides read-only inspection and fingerprint comparisons to check whether mods share the original game's base. It does not merge or install anything yet. Licensed under GPL-3.0; UndertaleModLib is referenced from the unmodified UndertaleModTool 0.9.2.0 submodule.

Requires the .NET 10 SDK. Initialize dependencies and build:

```sh
git submodule update --init --recursive
dotnet build -c Release
dotnet test
```

Run the feasibility check (the default report is `docs/reports/phase0.md`):

```sh
dotnet run --project src/Jamm.Cli -- phase0 --original "/path/to/original/data.win" --mods "/path/to/mods"
dotnet run --project src/Jamm.Cli -- inspect "/path/to/data.win"
dotnet run --project src/Jamm.Cli -- compare "/path/to/original/data.win" "/path/to/mod.win" --json out/comparison.json
```

`--mods` accepts one file or a directory of `.win` files; `--out report.md` changes the report path. The CLI assembly/executable is named `cgfmm`. Exit codes are 0 for completed analysis (including different-base verdicts), 1 for a read/write or usage failure, and 2 for YYC/empty CODE or unsupported bytecode. Add `--verbose` for exception details. Each file failure is isolated and recorded; remaining mods are still analyzed. Reports contain metadata, asset counts and hashes, without extracted game content. Fingerprint definitions and feasibility limits are documented in the generated report and `ContentFingerprints.cs`.

Tests use synthetic data by default. Set both `JAMM_ORIGINAL` and `JAMM_MODS_DIR` to opt into the real-file test. Keep legal game files outside the repository; inputs are always opened read-only and never committed.
