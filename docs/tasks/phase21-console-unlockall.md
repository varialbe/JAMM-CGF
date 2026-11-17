# Phase 21 — Console: complete `unlockall`, new commands, slot-safe unlocks

Branch: `exp/console-unlockall` (off `experimental`). Files: `src/Jamm.Core/Builtins/ConsoleMod.cs`,
`src/Jamm.Core/Management/BuiltinMods.cs`, `tests/Jamm.Core.Tests/SmoothGeneratorTests.cs` (console test) or a new
`ConsoleModTests.cs`, `docs/example-mods.md`, `README.md`.

## Background (audit of the legacy 2023 base, done by the orchestrator)

User report: after `unlockall`, switching to a new save slot and running `unlockall` again, the catalog was empty and the HUD
showed "Lvl. 0". Equipment unlocks worked. Diagnosis:

* The empty catalog / Lvl 0 is a **fresh slot**: the old `unlockall` deliberately did not touch level/XP or catalog.
  The user expects `unlockall` to give **everything**: equipment + max level + every fish in the catalog (all mods' fish too).
* **Real bug:** `obj_jamm_console` is persistent. `jamm_console_unlocked_all` stays `true` after switching slots, so the
  End Step keeps forcing `LureData.unlocked[5] = 1` and `AmbianceController.lockDay = 0` in a slot where `unlockall` was never
  typed — unlocks leak across slots. The flag must be reset whenever a different save is loaded.

Relevant game facts (GML of the base, do not copy game code into the repo, only use these names):

* Level: `Girl.level`, `Girl.experience`; `ExperienceBar.max_level` (= 100), `ExperienceBar.experience`,
  `ExperienceBar.experienceToLevel[level+1]`. ExperienceBar Step calls `levelup()` when its bar overflows — so **do not**
  add huge XP (it would chain-call levelup with dozens of popups). Set the level directly instead:
  `Girl.level = target; Girl.experience = 0; ExperienceBar.experience = 0;` (guard each with `instance_exists`).
  Girl Step then grants level-gated upgrades/quest_max/nightfall on its own. At level >= 50 CatalogData Step 2 reveals all
  unknown fish (`count == -1 -> -2`) once, which is fine.
* Catalog: `CatalogData.object[size, idx]` (jagged 2D: iterate `i` over `array_height_2d(CatalogData.object)` and `j` over
  `array_length_2d(CatalogData.object, i)`), `CatalogData.count[i,j]` (-1 unknown, -2 revealed silhouette, 0 seen, >0 caught
  N times), `CatalogData.life[i,j]` (best record, -1 none), `CatalogData.hide[i,j]`. The Fishalog reads these when it is opened.
  "Discovered" = `count[i,j] >= 1`. Only raise values (`count = max(count, 1)`, `life = max(life, 0)`), never lower them.
  Do **not** touch `hide[]` / special-case entries: the game itself re-hides Torby `[2,37]` unless extinct and Magnalav; that is
  intended. Only touch entries whose `object[i,j]` is a valid object (`object_exists`). Mod fish registered via the JAMM
  catalog API live in the same arrays, so they are covered automatically. Persistence is by the existing SaveManager code
  (`[JAMM_Catalog] f:/li:/pop:` keys in merged builds) — no save code changes.
* Money: `Girl.dollars` (floored every step by the game).
* Save slot: `SaveManager.saveflag` / `SaveManager.loadflag`; SaveManager is re-created when a slot is (re)loaded.
  Detect a slot change in Begin Step: remember `jamm_console_slot_owner = SaveManager.id` and `jamm_console_slot = SaveManager.saveflag`
  (if SaveManager exists); when either differs from the stored value, set `jamm_console_unlocked_all = false` and store the new values.

## Commands (all case-insensitive, extra spaces tolerated, argument after one space)

| command | effect |
|---|---|
| `help` | list all commands, one short line each (must fit the overlay; keep each line < ~90 chars) |
| `unlockall` | equipment unlock exactly as today **plus** `maxlevel` **plus** `catalog all`. Does not change money. |
| `unlockgear` | the old equipment-only behaviour (upgrades, rods, lures, boats, tools, hats, HUD icons, nightfall) |
| `maxlevel` | set level to `ExperienceBar.max_level` (fallback 100) |
| `level <n>` | set level to n, clamped to `[0, max_level]`; invalid number -> friendly error line |
| `catalog all` (alias `unlockfish`) | mark every catalog fish as caught (count>=1, life>=0); report how many entries changed and total |
| `money <n>` | set `Girl.dollars` to n (0 .. 999999999); `money +<n>` adds |
| `clear`, `close`/`exit` | unchanged |

Parse numbers with `real()` only after checking the string is digits (optionally a leading `+`) — `real("abc")` throws in GMS1.
Write a small digit-check loop. Each command prints a one-line result; keep the existing "WARNING: The next autosave ..." line
after any command that changes the save (unlockall/unlockgear/maxlevel/level/catalog/money).

Restructure to stay readable: build the GML from small C# helper methods (e.g. `UnlockGearGml()`, `SetLevelGml(string expr)`,
`CatalogAllGml()`), each guarded with `instance_exists`. Object names used must be checked with `data.GameObjects.ByName`
(skip a section if CatalogData/ExperienceBar is absent, like the existing `hasRod` pattern) — add `CatalogData` and
`ExperienceBar` to the "optional" checks, not the required list. Keep the local-vs-instance variable fix for `with` blocks
(`other.jamm_icon_new` must be an instance variable).

The End Step "keep unlocks" block stays, but only runs while the flag is true **for the current slot** (see slot detection).

## Other changes

* Bump `BuiltinMods.GeneratorVersion` to `"2"`; update the console description text in `BuiltinMods.cs`
  ("... Type help for commands; unlockall unlocks everything (equipment, max level, full catalog).").
* Help text, `docs/example-mods.md` console section and the README hotkey/console line: list the new commands.
* Tests (no game files): extend the console generator test — decompiled Begin Step contains `"maxlevel"`, `"catalog all"`,
  `"unlockfish"`, `"money"`, `"level"`, `"unlockgear"`, `CatalogData.count`, `ExperienceBar.max_level`, `SaveManager`
  slot detection, and the test stub must create `CatalogData`/`ExperienceBar`/`SaveManager` objects for that. Add a test that
  the generator still compiles when CatalogData/ExperienceBar are absent (sections skipped). Remove the old
  `DoesNotContain("CatalogData")` assertion. Keep `DoesNotContain("dollars +=")` false only for unlockall — adjust as needed.
* `dotnet build -c Release` zero warnings, `dotnet test` green.

## Out of scope
No changes to SaveManager / catalog merge code. No hotkeys outside the console.
