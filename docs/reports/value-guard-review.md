# Independent Review: ValueGuard, FishRuntime Cap, and MoneyRuntime

Independent adversarial review on branch `exp/value-guard-review` covering `src/Jamm.Core/Building/ValueGuard.cs`, `ValueGuardContext.cs`, `ValueGuardOptions.cs`, `GuardGml.cs`, `ConsoleUpgradeGuard.cs`, `FishRuntime.cs`, and `MoneyRuntime.cs`.

## Real Bugs Found and Fixed

1. **Negative 3-digit money formatted with a leading comma (`MoneyRuntime.cs`)**
   - **Symptom**: Both `MoneyRuntime.Format` and `MoneyRuntime.FormatterGml` computed `jamm_neg`/`negative` and `jamm_v`/`v = -v`, but the `< 10000` branch still formatted the signed `floor(argument0)`/`Math.Floor(value)` string (`"-500"`, length 4), causing `string_length - 2 = 2 > 1` to insert a comma right after `'-'`: `"$-,100"`, `"$-,500"`, `"$-,999"`.
   - **Fix**: Format the non-negative magnitude `jamm_v`/`v` in the `< 10000` branch, insert grouping commas, and prepend `"-"` afterward when negative (`-500` → `"$-500"`, `-5000` → `"$-5,000"`). Also clamped `Wallet`'s local `dollars` at `1e15` alongside `Girl.dollars` in `WalletReplacement` so the first drawn frame after `Girl.dollars` exceeds `1e15` is clamped too.
   - **Bumped**: `MoneyRuntime.Revision` (`big-money-1` → `big-money-2`).

2. **Compound `+=` price delta treated as total price (`ValueGuard.cs`)**
   - **Symptom**: `ValueGuard.Read` extracted a numeric literal RHS for `+=` on all non-`Sale` stats, including `price.*`. On a paid vanilla item (`cost[0] = 100`), `cost[0] += 0;` or `cost[0] += -50;` saw `number <= 0 && paidPrice > 0` and rewrote the delta to `cost[0] += 100;` (doubling or inflating the price).
   - **Fix**: Exclude `price.*` stats from `+=` literal extraction so `cost[0] += ...` goes through `Wrap` (`cost[0] = jamm_guard_stat(cost[0] + (...), ...)`), checking the resulting price at runtime just like `-=`, `*=`, and `/=`.

3. **Unnormalized constant array indices missed paid-price matching (`ValueGuard.cs`)**
   - **Symptom**: `ValueGuard.Read` built array item keys with raw `gml.Key(index)`, so `cost[$0] = 0;` or `cost[(0)] = 0;` produced `RodData[$0]` instead of `RodData[0]` and missed vanilla's `paidPrice` entry.
   - **Fix**: Normalize constant numeric array indices via `gml.Number(index)` / `GmlNumber(slot)` when forming `item`.

4. **`other.` outside any `with` block resolved to `self` (`GuardGml.cs`)**
   - **Symptom**: In `GuardGml.Resolve`, `qualifier == "other"` returned `owner` when `nesting.Length == 0` (no enclosing `with`). In a collision event on a non-fish object (`Hook` colliding with `Cuddlefish`), `other.BASE_VALUE = 5000000000;` resolved to `"Hook"` (known non-fish) and bypassed the fish sale cap; in a fish's collision event, `other.BASE_VALUE` falsely resolved to the fish.
   - **Fix**: Return `null` when `nesting.Length == 0` so `other.BASE_VALUE` receives `jamm_guard_fish_sale(other, ...)` (capped at runtime only when `other` is a `Fish`) and `other.<stat>` does not misattribute to `owner`.

5. **Multiline braceless `with (...)` with nested control flow broke scope tracking (`GuardGml.cs`)**
   - **Symptom**: Braceless `with (Cuddlefish)\n if (active)\n BASE_VALUE = 5000000000;` used `ExpressionEnd(body)`, which stopped at the newline after `if (active)` and left `BASE_VALUE` outside the `with` scope.
   - **Fix**: Added `StatementEnd(int start)` in `GuardGml` to span braceless `if`/`else`, `while`, `repeat`, `with`, and `for` statements inside `with`.

6. **Clamped debug placeholders inflated peer progression for other items (`ValueGuard.cs`)**
   - **Symptom**: `peers` included known debug placeholders (`9,999,999` and `999,999,999`) above `vanillaMax * 10`, allowing one item's clamped placeholder (e.g. `XP_VALUE = 999,999,999`) to raise `peer`/`progression` (`5 * 999,999,999`) and shield or raise the clamp limit of another extreme item (`5,000,000,000` → `999,999,999` instead of `headroom`).
   - **Fix**: Exclude known placeholders exceeding `vanillaMax * Headroom` from `peers`.

7. **String literal in `jamm_fish_register` sale argument (`ValueGuard.cs`)**
   - **Symptom**: Passing a string literal as `jamm_fish_register` argument 16 (`"bad"`) caused `ValueGuard` to wrap it in `min("bad", cap)`, which would fail at runtime even though `jamm_fish_register` itself guards `is_real(jamm_sale_value)`.
   - **Fix**: Skip `min(...)` wrapping for single string literal tokens, and guard `is_real(jamm_value)` in `jamm_guard_fish_sale`.
   - **Bumped**: `ValueGuardOptions.Revision` (`value-guard-1` → `value-guard-2`).

## Verified Edge Cases (Pinned by Tests)

- **Formatter boundaries & C# vs GML bytecode cross-check** (`MoneyRuntimeTests`): Compiled `MoneyRuntime.FormatterGml` with `CompileGroup` and executed the resulting GM1.4 bytecode across 55 inputs (`0`, `0.9`, `9999`, `9999.9`, `10000`, `999950`, `999999`, `1000000`, `3400000000123`, `999999999999999`, `1e15`, `9.5e15`, and negative values `-0.1` through `-1e15`), confirming exact agreement with `MoneyRuntime.Format`.
- **Console `money` command & 1e15 preservation** (`ConsoleArchiveBuildsWithBigMoneyOnAndOffAndPreservesLargeLiterals`): Verified `ConsoleMod` compiles `1000000 * 1000000000` cleanly (`1000000000000000` in decompiled GML) and `MoneyRuntime.ConfigureConsole` toggles both `0..999999999` and `up to 1e15` modes.
- **Hex, scientific notation, negative/zero, scripts, runtime expressions, and off-profile no-op** (`ValueGuardTests`, `BaseModTests`): Verified `$...`/`0x...` hex literals, `5e+9` parsing in `GuardGml`, negative/zero `BASE_VALUE` and `XP_VALUE`, nested script-to-script and uncalled-script resolution, `ConsoleUpgradeGuard` when both value guards are off, and full base-mod + addon profile builds with `FairPlay = false`, `FishValueCap = 0`, and `NoBigMoney = true`.

## Verification

- `dotnet build -c Release`: 0 warnings, 0 errors.
- `dotnet test -c Release`: full Core and GUI suites green (opt-in real-file tests skipped when `JAMM_ORIGINAL` / `JAMM_MODS_DIR` are unset).
