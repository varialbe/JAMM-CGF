# Task: independent review of the instance guard (branch `exp/instance-guard-review`)

`src/Jamm.Core/Building/InstanceGuard.cs` and `LegacyInstanceSites.cs` rewrite decompiled GML text with regexes in
every modded build (see `docs/reports/object-index-guard.md`, decisions CGF-INSTANCE-004..007). A wrong rewrite
breaks compilation or changes game behaviour, so review it adversarially.

## Do
1. Read the code and the existing tests in `tests/Jamm.Core.Tests/InstanceGuardTests.cs`.
2. Hunt for inputs where the rewrite is wrong: nested parentheses, `with` blocks, `other.`/`self.`/`global.`,
   chained member access, compound assignments, `++/--`, statements spanning lines, `if` without braces followed by
   `else`, string/comment masking (escaped quotes, `/* */`), variables named like objects, `exit`/`return` in
   guarded statements, `switch`/`case`, script arguments named like objects, multiple statements on one line.
3. For each real bug: add a failing xUnit test (synthetic GML text via `InstanceGuard.Rewrite`, no game files), then
   the smallest fix so it passes. For anything that is fine, add a test that pins the correct behaviour.
4. Write `docs/reports/instance-guard-review.md`: what you tested, bugs found and fixed, residual risks.
5. `dotnet build -c Release` zero warnings, `dotnet test` green.

## Rules
AGENTS.md applies. Run every command in the foreground (no background tasks). Simple single commands (no `&&`,
pipes, redirections, no `rm`). Do not commit; the orchestrator commits.
