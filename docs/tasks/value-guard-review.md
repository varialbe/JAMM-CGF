# Task: independent review of ValueGuard, FishRuntime cap and MoneyRuntime (branch `exp/value-guard-review`)

Merged features: Fair-Play debug-stat guard and fish sale cap (`src/Jamm.Core/Building/ValueGuard*.cs`,
`GuardGml.cs`, `ConsoleUpgradeGuard.cs`, `FishRuntime.cs` cap), compact money display and >999M money
(`MoneyRuntime.cs`). Reports: `docs/reports/fish-value-cap.md`, `docs/reports/big-money.md`,
`docs/reports/fair-play-audit.md`.

## Do
1. Review adversarially for wrong rewrites and edge cases: negative/zero/NaN-like values, hex literals, expressions
   instead of literals, arrays vs scalars, values set in scripts vs events, values changed at runtime, profiles with
   both settings off (must be a no-op), intermediate vs final layers, formatter boundaries (9,999 / 10,000 /
   999,950 / 1e15, negative money), INI save round trip of large money, Console money command.
2. Cross-check `MoneyRuntime.Format` (C#) against the generated GML formatter on the same inputs (table test).
3. For each real bug: failing xUnit test (synthetic data, no game files), then the smallest fix. Pin correct behaviour
   with tests where it is fine.
4. Report `docs/reports/value-guard-review.md`. `dotnet build -c Release` zero warnings, `dotnet test` green.

## Rules
AGENTS.md applies. Every command in the foreground (no background tasks). Simple single commands (no `&&`, pipes,
redirections, no `rm`). Do not commit; the orchestrator commits.
