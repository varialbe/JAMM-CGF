# Task: review and expand the Silo mod into a proper, collision-free "Silo area"

Current state: `src/Jamm.Core/Builtins/SiloMod.cs` (recipe `silo`, ModMaker `silo`), docs `docs/silo-and-cgf-mods.md`,
tests `tests/Jamm.Core.Tests/SiloAndSiteTests.cs`, package `site/cgf-mods/sources/jamm-silo`. The user wants it to become a
polished mod with its own silo area, without causing collisions with other mods, and with extensive tests.

## Phase 1 – honest review (write it to docs/reports/silo-review.md first)
Check in the real game data (VPS: `~/cgf-files/legacy/.../data.win`, packs in `~/cgf-files/mods/pack-inputs`, `~/cgf-files/private-vel`,
cave-expansion under `~/cgf-files/mods`) and in game:
* What vanilla `MissileSilo` does (all its events, where it is placed, how the vanilla game uses `spr_silo`/`spr_silomask`).
  Known problem: SiloMod REPLACES MissileSilo's Create/Step/Draw events → collisions with any mod editing MissileSilo and loss of
  vanilla behavior. Check whether any of the 7 real mods touch MissileSilo, its sprites, or the area around (33984, 11570) in NewWorld
  (instances within ±1500 px, room edits) — list them.
* Does it build on top of modded bases / merge in JAMM together with the all-7 pack (`check` findings, build decisions)?
* Does entering/leaving actually work in game (state preservation: money, fish, quests, save, boat, rod; pause menu / Esc conflicts;
  what happens on save+quit inside the area; repeated entry; dying/breath).
* Visual quality of hatch + area (screenshots).

## Phase 2 – redesign & implement
Goals: (a) zero collisions by construction, (b) a nice area worth visiting, (c) works alone and in big packs.
* **Never modify vanilla objects/events.** Use a new object (`obj_jamm_silo_hatch`, child of nothing or of MissileSilo only if inheritance is safe)
  placed on top of / next to the vanilla silo, or leave MissileSilo untouched and only add instances. All assets prefixed `jamm_silo`.
  Room edits in NewWorld: only additions of own instances; none where other mods place instances (use the Phase-1 scan).
* The area: a separate room (keeps NewWorld untouched). Make it a real place: hand-crafted layout (not just gradients), proper
  lighting matching CGF's dark-water style, landmarks inside the silo (missile, rusty ladders, control room, cables), ambient sound if a
  vanilla sound fits. Give a reason to come: e.g. 3–6 silo-only fish/creatures registered via the JAMM fish/catalog API so they count in
  the fishalog and can be caught/collected, or collectibles that pay money — choose what fits the game mechanics with the least invasive hooks,
  and document the choice. Prefer the player's real cat/boat mechanics if feasible without touching vanilla code; otherwise keep the diver but make
  it feel like CGF (art scale, controls shown on screen, breath meter).
* Robust state handling: no permanent `room_persistent` side effects, SaveManager never loses a save; Esc must not fight the vanilla pause
  menu; save/quit in the area must load back safely into NewWorld at the hatch.
* Must work as a ModKit/recipe mod via JAMM alongside other mods (build it as a patch against vanilla that merges cleanly), and as a builtin
  option if that is how it is offered today.

## Phase 3 – extensive tests
* Unit/synthetic: assets created, no vanilla code entry modified (assert byte-identical vanilla code entries except none), idempotency errors,
  build on modded base, merge with each of the 7 real mods (opt-in real-file tests) with zero conflicts involving Silo assets.
* Add a `silo` scene to the bench/auto-scene tooling (`~/jamm-wt/bench/tools/Jamm.ModMaker`, see `~/jamm-night/bin/run.sh`) that teleports the cat to
  the hatch, enters the area, waits, exits, and reports state (money/fish count before vs after) + screenshots (hatch closed/open, area, back in NewWorld).
* In-game on the VPS (`~/jamm-night/bin/run.sh <win> <label> <scene> 600`, ONE run at a time – memory; big packs need ~600 s):
  Silo alone, Silo + public 5-pack, Silo + all-7 (TGE, Antiquity, PWB, Abyss, Colour, Verdant, Cave). Report outcome per run.
* Save round-trip: enter, save, quit, reload → in NewWorld at hatch, nothing lost.

## Rules
AGENTS.md. Work on branch `exp/silo-area` in `~/jamm-wt/silo-area`. Zero warnings, `dotnet test -c Release` green. Commit on the branch is OK, no push,
no merge to main. Never copy TGE/PWB outside the VPS. Original art only (no copied third-party art). Final report: `docs/reports/silo-area.md`
with screenshots paths, run table, what is still open.
