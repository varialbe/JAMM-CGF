# Fish sale cap and Fair-Play

Implemented on `exp/fish-value-cap` after merging `main` into the experiment. The two settings are stored per profile and included in the install signature. The sale cap defaults to 100,000,000. Fair-Play defaults to on. Both run in final modded builds, including base-mod profiles with addons. Fair-Play derives its limits from the vanilla archive in either build mode.

Fair-Play uses ten times the highest comparable vanilla value as headroom. It distinguishes known placeholders, gameplay-trivial reel and sink speeds, and extreme values isolated from the mod's other items. Inconclusive outliers generate `CGF-FAIR-002` informational decisions. Player money, console grants and quest rewards remain under the existing game and Big Money behavior.

## TGE pack

Build order was JAMM Perf, HD Lite, Console, Smooth, TGE, Silo, Weather Pack, and Endless Ocean, using the August 2023 legacy archive. The build was saved as `~/jamm-night/personal-tge/tge-jamm-capped.win` on the VPS.

| Mod | Value | Build decision |
| --- | --- | --- |
| TGE | Cuddlefish sale value | 5,000,000,000 → 100,000,000. The fixed fish sale cap applies. |
| TGE | Cuddlefish XP | 5,000,000,000 → 5,000,000. It is an isolated debug magnitude. The vanilla maximum for this stat is 500,000. |
| TGE | Squiddle computed sale value | The assignment receives the 100,000,000 runtime ceiling. |
| TGE | Lure sinker velocity | The computed assignment gets a debug guard with a 50 fallback. |
| TGE | Lure rise speed | The computed assignment gets a debug guard with a 10 fallback. |
| Endless Ocean | Final boat's computed range | The assignment gets a debug guard with a 650,000 fallback. The normal map-dependent range is preserved. |
| TGE | Boat 7 movement speed | Kept at 100, ten times vanilla's maximum of 10. The value is at the headroom boundary and lacks a clear debug signature. |
| TGE | Rod table changes | Kept the maximum reel setting of 20 (vanilla 15) and the additional rod's cast speed of 18 and power of 2.15. These are within the observed progression. |

The canonical appraisal also applies the sale ceiling after gear modifiers. Shop prices produced no clamps in this pack.

## Console and Big Money

TGE's Rod recompute event initializes effect slots 0–21. The Rod step and camera can read slot 22 before it has been populated. `unlockall` marks catalog fish caught, which can expose that Rod step condition. The repaired reads use the existing effect when its slot exists and effect zero when it is absent. Both Console gear commands bound their reads to the actual unlock arrays. The final build also updates the older Console archive's money command to honor Big Money.

## Verification

- Release build completed with zero warnings.
- The full default suite passed with 565 Core and 130 GUI tests. The 17 opt-in tests were skipped. New coverage includes sale literals, computed fish values in events and scripts, catalog and API fish, ModKit manifests, the Fair-Play categories, Console effect bounds, and per-profile GUI, CLI and install-signature behavior.
- The final-layer integration test verifies that intermediate builds leave values alone and that a profile with an overhaul and addon still uses vanilla XP limits.
- A real vanilla-only build with a deliberately low configured cap of 1 was byte-identical to the original by `cmp` and SHA-256.
- The rebuilt pack passed semantic verification for 2,238 code entries and catalog verification for 200 fish. The ModMaker dump confirmed Cuddlefish at 100,000,000 sale value and 5,000,000 XP. The pack's default conflict order superseded two Perf and two Smooth hunks. Its acceptance check also reported that the Gear event still iterates 18 entries while the upgrade table contains 19.

## VPS playtest

A disposable instrumented copy of the exact-order build ran on the authenticated VPS Steam client with an isolated test save. Cuddlefish was worth 100,000,000 at creation and appraised at 100,000,000 with a 2× gear multiplier. A Fish API registration passed 150,000,000 at runtime and assigned 100,000,000. The Console command `money 3400000000123` set the exact balance and the formatter displayed `$3.4T`.

A second live probe submitted `unlockgear` and `unlockall` to the Console's command handler. Both commands ran and the game exited normally. The probe cleared the relevant catalog count and unlock flag before the commands. When `unlockall` marked the fish caught, TGE's Rod step completed the unlock. The missing effect slot 22 read as zero. The playtest also reproduced the fish sale and `$3.4T` wallet results.
