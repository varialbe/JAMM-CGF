# Task: Fair-Play audit and false-positive tests (branch `exp/fair-play-audit`)

Fair-Play (`src/Jamm.Core/Building/ValueGuard*.cs`, decisions `CGF-FAIR-*`) and the fish sale cap were merged into
main (see `docs/reports/fish-value-cap.md`). Fair-Play must only touch obvious developer leftovers (5e9, 999999999,
0 prices, infinite sink speed), never a mod's intended balancing.

## Do
1. Build each mod alone on top of the legacy base and record every `CGF-FAIR-*` and fish-cap decision:
   `dotnet src/Jamm.Cli/bin/Release/net10.0/cgfmm.dll build --base <legacy> --order <mod.win> --out <tmp>/x.win`
   Legacy base: `~/cgf-files/legacy/2023-08-16_manifest-9039707068725406600/data.win`.
   Public mods: `~/cgf-files/mods/*.win` and `~/cgf-files/mods/pack-inputs/*.win`.
   Private mods (results must NOT go into the repo): `~/cgf-files/private-vel/*.win`.
   Write outputs only under `~/jamm-night/n21/fair-play-audit/`.
2. For each decision judge: obvious debug value (correct) or intended balancing (false positive). Public results go
   into `docs/reports/fair-play-audit.md` (table: mod, value, decision, verdict). Private-mod results go into
   `~/jamm-night/n21/fair-play-audit/private.md` only.
3. For every false positive: add a failing unit test in `tests/Jamm.Core.Tests/ValueGuardTests.cs` (synthetic data,
   no game files), then the smallest fix in ValueGuard so it passes. Also add tests for borderline values that must
   stay untouched (e.g. 10x vanilla headroom boundary, boat speed 100).
4. Investigate the acceptance warning "Gear event iterates 18 entries while the upgrade table has 19" and document
   cause and options in the report (no fix unless covered by a test).
5. `dotnet build -c Release` with zero warnings, `dotnet test` green. Vanilla-only build stays byte-identical.

## Rules
AGENTS.md applies. Never modify input game files, never copy game data into the repo. Use simple single commands
(no `&&`, pipes or redirection chains). Do not commit; the orchestrator commits.
