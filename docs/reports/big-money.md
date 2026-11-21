# Money above $999,999,999 (big money)

Branch `exp/big-money`, implemented by the orchestrator on 2026-11-21.

## Findings in the 2023-08-16 legacy base
* Only one place limits money: `gml_Object_Wallet_Draw_0` draws `number_to_dollar_string(min(floor(dollars), 999999999))`.
  `Girl.dollars` itself is never clamped; it is floored every step in `gml_Object_Girl_Step_0`.
* Every money display goes through `gml_Script_number_to_dollar_string` (digits with commas): wallet, rod/boat/lure/tool
  shop cards, upgrade prices, gear values, sell dialog (`Total Value`, base value, hooked fish, full boat, trawler),
  catalog values and fish base values, quest rewards and the boat value panel (17 call sites).
* Saves store money with `ini_write_real` (`Main/dollars`, `save<N>/Dollars`). The in-game test wrote and read back
  3,400,000,000,123 exactly.
* The JAMM Console clamped `money` to 0..999,999,999.

## Change (modded builds only, final build layer, decision `[CGF-MONEY-001]`)
* `number_to_dollar_string` gets a compact body: below 10,000 unchanged (`$9,999`), otherwise one truncated decimal with
  K/M/B/T/Qa (`$25.0K`, `$3.4M`, `$9.5B`, `$3.4T`, `$1.0Qa`). A mod that already changed the script keeps its version.
* The wallet clamp is removed; the wallet keeps money at or below 1e15 (exact in doubles). Large literals are written as
  products of 32-bit numbers.
* Console `money` accepts up to 1e15 and reports the amount in the new format.
* Per-profile toggle `bigMoney` (default on): `cgfmm profile bigmoney <name> on|off`, GUI *Performance settings → Money
  above $999,999,999*, `cgfmm build --no-big-money` for `--base` builds. Part of the install signature.
* Vanilla-only builds stay byte-identical. Fish sale values are not touched (separate 100M fish value cap).

## Verification
* Unit tests: 18 formatter cases (C# reference = GML rules), synthetic build with/without the toggle, vanilla byte copy.
  Full suite 557 core + 129 GUI tests green, zero warnings.
* In-game (VPS, console + weather-pack build, bench scene `money`): rod shop open, wallet `$1.2B`, then `$3.3T` while
  counting up to 3.4T; shop prices `$750`, `$10.0K`; INI round trip of 3,400,000,000,123 exact. `RECORDED`.
