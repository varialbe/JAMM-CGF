# Instance Guard Independent Review

Independent adversarial review of `src/Jamm.Core/Building/InstanceGuard.cs` and `src/Jamm.Core/Building/LegacyInstanceSites.cs` (CGF-INSTANCE-004..007) at `2f86e1e` on branch `exp/instance-guard-review`.

## What Was Tested

Reviewed all rewriting, masking, shadowing, existence-guard, and lookup-tracking paths against synthetic GML inputs in `tests/Jamm.Core.Tests/InstanceGuardTests.cs` (via `InstanceGuard.Rewrite` and `ArchiveBuilder.Build`, no game files required):

- **Nested parentheses**: `if ((a && instance_exists(Bo)) || b)`, `if (!(a && instance_exists(Bo)))`, `if ((a || b) && instance_exists(Bo))`, `var x = foo(1, 2), Sizzer;`, multi-line `for (...)` headers, and nested call arguments.
- **`with` blocks**: `with (Bo)`, `with (S("Bo"))`, shadowed `var Bo` in `with (Bo)`, and `other.`/`self.` inside `with`.
- **`other.` / `self.` / `global.`**: `other.Bo.x`, `self.Bo.y`, `global.Bo.z`, `other . Lure = 1`, `global.near = instance_nearest(...)`, `other.near.x`, and `other.near = 1`.
- **Chained member access**: `Bo.target.x`, `Bo.target.x = 1`, `Lure.fish.x`, `Lure.fish.object_index`, and `near.target.x`.
- **Compound assignments and `++/--`**: `+=`, `-=`, `*=`, `/=`, `%=`, `&=`, `|=`, `^=`, `<<=`, `>>=`, postfix `++`/`--`, prefix `++`/`--`, and `Bo += 1` / `++Bo.x`.
- **Statements spanning lines**: multi-line RHS calls (`Bo.x = clamp(\n ... \n);`), multi-line member dot (`Bo\n .y = 2;`, `d = Bo\n .z;`), and multi-line `for (\n Bo.i = 0;\n ...)` headers.
- **`if` without braces followed by `else`**: single-line `if (b) Bo.x = 4; else y = 1;` and multi-line `if (b) y = 1; else Bo.x = 4;`.
- **String and comment masking**: escaped quotes (`"escaped \"Bo.x\""`, `'escaped \'Bo.y\''`), trailing escaped backslash (`"slash \\"; Bo.z = 4;`), semicolon inside escaped strings (`Bo.name = "a; \"b\"";`), `/* ... */` multi-line/quote-containing comments, and `//` comments.
- **Variables and script arguments named like objects**: mixed `var x = foo(1, 2), Sizzer;`, compound/increment shadowing (`Baby_Sizzer += 1;`), GMS2-style `function move_fish(a, Bo, Worm = 0)`, and lookup variables named like objects (`var Bo = instance_nearest(x, y, Fish);`).
- **`exit` / `return` and control-flow scoping**: `if (near == noone) exit;`, non-exiting `if (near == noone) log();`, scoped early exit inside `if (cond) { if (scoped == noone) exit; ... }`, semicolon-less `if (instance_exists(Bo)) exit\nBo.w = 4;`, and `if (!instance_exists(Bo)) exit;`.
- **`switch` / `case`**: `switch (Bo.state)`, `case 0: Bo.p = 1; break;`, `case Bo.object_index:`, and `default: return Bo.q;`.
- **Multiple statements on one line**: `a = 1; Bo.x = 2; Worm.y = 3;` and `if (b) Bo.x = 4; else y = 1;`.
- **Legacy instance sites (`LegacyInstanceSites`)**: all six targeted code entries (`gml_Object_Chook_Other_10`, `gml_Object_Thermo_Other_10`, `gml_Object_SaveManager_Other_13`, `gml_Object_LightingSystem_Draw_0`, `gml_Object_Snobfish_Other_10`, `gml_Object_Flash_Other_10`) and rewrite idempotency.

## Bugs Found and Fixed

All fixes were made in `src/Jamm.Core/Building/InstanceGuard.cs`, backed by new regression tests in `tests/Jamm.Core.Tests/InstanceGuardTests.cs`:

1. **Compound assignments and prefix `++`/`--` writes left unguarded** (`WrapsCompoundAssignmentsAndPrefixIncrements`):
   `IsWrite` accepted `%=`, `&=`, `|=`, `^=`, `<<=`, `>>=`, and prefix `++`/`--`, but `SimpleStatement` only matched `=`, `+=`, `-=`, `*=`, `/=`, and postfix `++`/`--`, leaving writes such as `Bo.flags |= 1;`, `Bo.x <<= 1;`, `++Bo.x;`, and `++near.x;` unguarded (`Guarded = false`).
   *Fix*: Replaced line-regex `SimpleStatement` with token-based `SimpleWriteStatement` handling all assignment operators and prefix/postfix `++`/`--`.

2. **Multi-line write statements left unguarded, and multi-line `for` headers corrupted** (`GuardsMultilineStatementsAndMultipleStatementsPerLineWithoutBreakingForHeaders`):
   `SimpleStatement` inspected only a single line, so `Bo.x = clamp(\n ...\n);` and `Bo\n .y = 2;` were left unguarded, while `for (\n Bo.i = 0;\n ...)` matched line 2 and injected `{ if (...) { Bo.i = 0; } }` inside the `for (...)` header (a syntax error).
   *Fix*: `TokenInfo.Analyze` tracks `ParenDepth` and statement boundaries across lines; `SimpleWriteStatement` only wraps writes at `ParenDepth == 0` starting a statement and scans balanced parentheses across lines to `;`.

3. **Multiple statements on one line left writes unguarded** (`GuardsMultilineStatementsAndMultipleStatementsPerLineWithoutBreakingForHeaders`, `PinsNestedParensQualifiedNamesChainingMaskingSwitchAndElse`):
   Lines such as `a = 1; Bo.x = 2; Worm.y = 3;`, `if (b) Bo.x = 4; else y = 1;`, `case 0: Bo.p = 1; break;`, and `"slash \\"; Bo.z = 4;` were rejected by `SimpleStatement`'s whole-line regex.
   *Fix*: `SimpleWriteStatement` wraps individual statements keyed by `statement.Start` (replacing per-line `guardedLines`), preserving enclosing `if`/`else` and `case` structure.

4. **Mixed `var` initializers, `function(...)` parameters, compound ops, and spaced `.` in `ShadowedObjects`** (`DetectsShadowedVariablesInMixedVarFunctionArgsAndCompoundOps`):
   - `var x = foo(1, 2), Sizzer;` stopped matching at `=`, missing `Sizzer`.
   - `function move_fish(a, Bo, Worm = 0)` parameters were never scanned, causing parameter dereferences `Bo.x` and `Worm.y` to be rewritten to `jamm_iv_*(Symbol("Bo"))`.
   - `Baby_Sizzer += 1;` was not recognized as shadowing `Baby_Sizzer`.
   - `other . Lure = 1;` falsely shadowed `Lure` because `(?<![\w.])` did not ignore whitespace before `Lure`.
   *Fix*: Replaced `ShadowedObjects` regexes with token-based scanning of `var`/`globalvar` declarators, `function(...)` parameters, and un-dotted target mutations (excluding `++obj.field` where `.` follows the identifier).

5. **Lookup variables named like objects were skipped by both `Member` and `Lookup`** (`GuardsLookupVariablesEvenWhenNamedLikeObjectsAndRespectsIfScope`):
   In `var Bo = instance_nearest(x, y, Fish); d = Bo.x;`, `ShadowedObjects` excluded `Bo` from `Member` while `Lookup` skipped `facts.Names.Contains(variable)`, leaving `Bo.x` completely unguarded.
   *Fix*: Removed the `facts.Names.Contains(variable)` skip in `Lookup` (typed members such as `Bo.object_index` remain unrewritten via `CgfAssetTypes.Family`).

6. **`Lookup` check heuristic stopped guarding outside `if` blocks, on `||` conditions, on non-exiting `if` checks, and on reassignment RHS** (`GuardsLookupVariablesEvenWhenNamedLikeObjectsAndRespectsIfScope`):
   - `if (near != noone) { a = near.x; } b = near.y;` left `b = near.y` unguarded after the `if`.
   - `if (near != noone || fallback) c = near.z;` left `c = near.z` unguarded despite `||`.
   - `if (near == noone) show_debug_message("missing"); e = near.speed;` stopped guarding even though the `if` body did not exit/return.
   - `if (cond) { if (scoped == noone) exit; g = scoped.x; } h = scoped.y;` leaked the early-exit protection out of the enclosing `{ ... }` block to `h = scoped.y`.
   - `near = near.parent;` stopped `limit` before `near.parent`, leaving `near.parent` unguarded on the RHS.
   *Fix*: `LookupLimit` now extends to the end of non-lookup reassignment statements, and `SafeRanges` scopes positive `if` guards to their condition remainder + then-branch and negative early-exit guards (`exit`/`return`/`break`/`continue`) to their `else` branch and enclosing `{ ... }` block.

7. **`InExistenceGuard` false positives on `or`/`xor`/`^^`/`?:`, unbraced `{}` blocks, and semicolon-less statements** (`ExistenceGuardHandlesBooleanOperatorsUnbracedBlocksAndNoSemicolons`):
   - `if (a or b && instance_exists(Bo))` and `if (a ^^ b && instance_exists(Bo))` bypassed `Prefix.Contains("||")`, leaving `Bo.x = 1;` unguarded.
   - `if (instance_exists(Bo)) if (c) {} Bo.z = 3;` and semicolon-less `if (instance_exists(Bo)) exit\nBo.w = 4;` lacked a `;` before `Bo.*` and were treated as still inside the `if`.
   - Also unified `InExistenceGuard` with `SafeRanges`, which now recognizes `if ((a || b) && instance_exists(Bo))` and direct `if (!instance_exists(Bo)) exit;` guards, and fixed `IsPrefixIncrement` not to treat a trailing `++` on the previous line (`a++\nBo.x`) as prefix `++Bo.x`.

## Behaviours Pinned (Already Correct)

- Leaf `Obj.object_index` constant replacement in `==`/`!=` comparisons while preserving parent `Sizzer.object_index` and non-comparison positions.
- Safe `with (Obj)` selector logging without rewriting, and skipping `with (Bo)` when `Bo` is shadowed.
- Qualified `other.`/`self.`/`global.` member access and chained reads (`Bo.target.x`, `near.target.x`, `Lure.fish.x`) left unrewritten.
- Escaped quotes (`\"`, `\'`), `\\` before closing quotes, `/* */` comments, and `//` comments properly masked.
- `switch`/`case`, `if`/`else` without braces, and all six `LegacyInstanceSites` repairs plus rewrite idempotency.

## Residual Risks

- **Inter-procedural and cross-event dynamic IDs**: `Lookup` only tracks assignments from `instance_*` within a single code entry; instance handles stored on fields (`self.target_node`, `global.*`), containers, or returned from helper scripts cannot be proven live across events.
- **Branch-sensitive `Lookup` reassignments**: `LookupLimit` stops linearly at the next `variable = ...` in the code entry; conditional reassignment (`if (cond) near = id;`) or conditional lookup initialization is heuristic.
- **Neutral `0` fallback on untyped handle fields**: `AccessorGml` returns `noone` (`-4`) for `object_index` and `id`, and `0` for all other fields. In GML, `0` is also the first object asset index, so passing an untyped handle field from an absent risky object (e.g., `with (Bo.partner)`) would pass `0` rather than `noone`.
- **Unrewritten expression/array/chained/typed sites**: Expression-level writes (`a = Bo.x++;`), `for (...)` header writes, array writes/reads (`Bo.arr[i]`), chained dereferences (`Bo.target.x`), and non-comparison typed reads (`switch (Bo.object_index)`) remain reported (`Guarded = false`) rather than rewritten.
- **GM1.4 trailing backslash in string literals**: `GmlReferences.Tokens` treats `\` as an escape inside `"..."`/`'...'` to support escaped quotes in synthetic/GMS2 GML; in GM1.4, a literal string ending in `\` (e.g., `"path\"`) would consume its closing quote in `Tokens`/`Mask` (Underanalyzer does not emit `\"` for GM1.4).
- **Multi-statement `LegacyInstanceSites` replacements**: Four legacy site replacements (`Thermo_Other_10`, `SaveManager_Other_13`, `LightingSystem_Draw_0`, `Snobfish_Other_10`) expand one matched line into multiple statements without an outer block; if a mod moved those exact lines directly under an unbraced `if`/`else`, control flow would break (`Flash_Other_10` guards against this by requiring the line to start the entry).
- **Function-wide `ShadowedObjects` scope**: `ShadowedObjects` applies `function(...)` parameter shadowing across the entire code entry rather than only inside the nested function body, which is safe (never corrupts a parameter) but conservative if an outer block in the same entry uses an object with the same name.

## Verification

- `dotnet build -c Release`: 0 warnings, 0 errors.
- `dotnet test -c Release --no-build`:
  - `Jamm.Core.Tests`: 581 passed, 0 failed, 16 skipped (opt-in real-file tests skipped without `JAMM_ORIGINAL` / `JAMM_MODS_DIR`).
  - `Jamm.Gui.Tests`: 130 passed, 0 failed, 1 skipped.
  - All 13 tests in `InstanceGuardTests` passed (6 existing + 7 added regression/pinning tests).
