# Task: boat shop reveal fixes (submarine hides Luxury progression, Tech Boat early reveal)

Legacy build vanilla facts (BoatData_Create_0): base boats 0..7, unlocked[] = 1,0,0,-1,-1,-1,-1,-1
(-1 hidden, 0 visible/buyable, 1 owned). Slot 5 Luxury, 6 Collier, 7 Tech Boat. Tech Boat is revealed by a
catalog quest (unlocked[7] = 0 + Message_TechBoat), NOT by purchases. Vanilla BoatCard_Other_10 reveals
`item_id + 1..2` only if `(item_id + i) < (BoatData.nItems - 1)` -> with vanilla nItems=8 slot 7 is excluded.
Mod boats appended by `jamm_boat_register` raise nItems, so buying Luxury/Collier now reveals Tech Boat early.
The submarine (examples/modkit/submarine) registers visible (unlocked 0) immediately, so its card sits where
the Luxury Boat later appears; players think it "stole" the Luxury slot.

## Fix 1 – Tech Boat early reveal
In `src/Jamm.Core/Building/ItemsRuntimePatches.cs` `PatchBoatCardClick`, also rewrite the reveal bound
`(item_id + i) < (BoatData.nItems - 1)` to use the base count (`BoatData.jamm_base_count`, populated by
`jamm_items_ensure_base`; fall back to nItems if the variable is missing). Keep it idempotent and tolerant of
the decompiled text form (check MenuRuntime.cs / LegacyItemUnlocks.cs which also touch this entry; make sure
all paths that emit BoatCard_Other_10 get the fix and that existing patches still apply). Mod items must never
be auto-revealed by this loop either.

## Fix 2 – generic "revealAfter" for ModKit items (boats at minimum)
- ModKit boat manifest gets optional `"revealAfter": "<item key>"` (e.g. `"base:boat6"`). Validate in
  `cgfmm modkit check` (must be a string; key format kind-consistent). Document in docs/modding/modkit-reference.md.
- When set, the boat registers hidden (-1) and `BoatData.jamm_reveal_after[slot] = key` ("" for others).
- Runtime rule (apply when the boat menu builds its card list in CardHandler_Alarm_0 init, and after load in
  jamm_items_load): for every BoatData slot with a reveal key: if unlocked == 1 leave it; else unlocked = 0 if
  the referenced item's unlocked >= 0, otherwise -1. This must also re-hide a not-owned sub in old saves that
  stored `u:boat:submarine:submarine=0`. Owned items are never hidden.
- Submarine manifest: `"revealAfter": "base:boat6"` (appears once Collier is visible, i.e. after the Luxury/
  Dark chain). Bump submarine to 0.1.1, update its README/design doc line "available to buy immediately",
  and site/cgf-mods catalog/content version if they pin it (keep tests green).

## Tests
Add/adjust unit tests (tests/Jamm.Core.Tests) for: reveal-bound patch text, revealAfter parsing/validation,
generated register call + runtime GML. Run `dotnet test` for Jamm.Core.Tests from this REAL path (not the
~/JAMM-CGF symlink). Do not touch ~/.var/app/com.valvesoftware.Steam/data/CGF-Personal or compatdata
cgf-personal. Commit on branch fix/boat-reveal (no push).
