# Task: Money above 999,999,999 + compact money display (K/M/B/T/Qa)

Branch `exp/big-money`, worktree `~/jamm-wt/big-money`. Read AGENTS.md first.

## User request (2026-11-21)
Legacy CGF caps money and its display at 999,999,999. Money should grow without that cap (sensible upper bound
1e15, GML reals are doubles: integers are exact up to 2^53 ≈ 9.007e15) and be shown compactly:
`3.4K`, `3.4M`, `9.5B`, `9.5T`, `1.2Qa` (units K, M, B, T, Qa; one decimal; below 10,000 show the full number
the way vanilla does, e.g. `9,999`). The user wrote "3.4m, 9.5t" – use upper-case units like above.

## Required
1. **Find every place** in the 2023-08-16 legacy base (`~/cgf-files/legacy/2023-08-16_manifest-9039707068725406600/data.win`)
   that clamps money or formats money for display: Girl/money variables, `min(…, 999999999)`-style clamps, HUD
   money draw, shop prices (rods, boats, upgrades, hats, lures), sell dialog / fish value popups, catalog values,
   quest rewards, JAMM Console `money` command, save/load of money. Put the full list (code entry, line, what it
   does) in the report. Also check the public mods and `~/cgf-files/private-vel/*` for their own clamps/formatters
   (names/counts only in the report, TGE/PWB never leave the VPS).
2. **Save/load:** verify how money is stored (ini real format? string? `ini_write_real` precision) and make sure
   values up to 1e15 round-trip exactly. If the vanilla format loses precision, store a JAMM side value and keep
   the vanilla key compatible (a save opened in vanilla must not break – clamp the vanilla key to 999,999,999).
3. **Build-time JAMM feature** for every build with at least one mod (vanilla-only stays byte-identical):
   remove/raise the money clamp to 1e15, add `jamm_money_format(value)` (script, documented in the modding API so
   ModKit/API mods can use it) and route all money displays found in (1) through it. Fish sale values are NOT
   affected (they keep the separate 100M fish value cap from exp/fish-value-cap; do not touch that branch).
   Per-profile toggle `bigMoney` (default on) in profile JSON, CLI `cgfmm profile bigmoney <name> on|off`, GUI
   checkbox next to the other profile build settings; part of the install signature. Decision line
   `[CGF-MONEY-001] ...` listing patched entries. Run it only in the final build layer (`!intermediate` in
   `ArchiveBuilder.BuildLayer`, like the other runtimes). Mods that already reformat money: detect and report,
   do not double-format.
4. Formatting rules: `< 10000` → vanilla formatting; otherwise one decimal, truncated (not rounded up across a
   unit: 999,950 → `999.9K`, never `1000.0K`), drop `.0` is NOT wanted (`3.0M` stays `3.0M`), units K=1e3,
   M=1e6, B=1e9, T=1e12, Qa=1e15. Negative values (debts, if any) keep the sign. Unit tests for the formatter
   logic (C# reference implementation + the GML text must agree on a table of cases).
5. Tests: unit tests (patch applied, clamp removed, display routed, save round-trip logic), vanilla byte-identical,
   full suite green, zero warnings.
6. In-game (VPS, `~/jamm-night/bin/run.sh`, shared game lock; add a bench scene `money` to tools/Jamm.ModMaker
   that sets money via the console code path to 1,234,567,890 and 3,400,000,000,000, opens the shop and the
   sell dialog and takes screenshots, then saves, reloads and checks the value). Personal build (perf, hd-lite,
   console, smooth, silo, weather-pack, endless-ocean) and all-7 (`~/jamm-night/builds/all7-repack.win` inputs).
   Screenshots to `~/jamm-night/big-money/` (HUD 1.2B, 3.4T, shop, sell dialog).
7. Report `docs/reports/big-money.md`. Commit on `exp/big-money`; do not merge into main or push main.
