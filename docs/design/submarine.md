# Submarine – design notes

Status: **Phase 0 findings by the orchestrator (2026-11-21)**. Codex: verify these against the legacy base,
then complete the design (mechanics, upgrade tiers, API) and build v0.1.

## Phase 0 – what the legacy engine already offers
* **Vanilla already has diving: `CatSwim`.** On a boat at level ≥ 25, `Girl_KeyPress_81` (Q) fills
  `BreathBar.breath = BreathBar.MAX_BREATH` and creates `CatSwim` at the cat. `CatSwim_Create_0` spawns a
  `CatSwimCollider` and a `CatSwimCamTracker` (`tracker`), stores `home_x/home_y`, swim speed, grabbing state.
  `CatSwim_Destroy_0` removes both helpers and refills breath. Keyboard events 37–40 steer, 17 (Ctrl) grabs.
* **Camera** (`Camera_Step_0`): if `instance_exists(CatSwim) && BreathBar.breath > 0` it follows
  `CatSwim.tracker.x/y`; otherwise the lure while cast, otherwise the cat (boat: fixed sea line y=1294).
* **Breath** (`BreathBar_*`): `MAX_BREATH = 30 + 10 * floor((max(25, level) - 25) / 3)`, visible only while a
  CatSwim exists, drawn in view 0. A yellow potion refills it.
* **Boat** (`Boat_*`): the boat is the cat's surface vehicle: `Boat_Step_1` drives `Girl.hspeed` (speed from
  `base_speed + gear_boat_speed_mod`), `Boat_Step_0` sets `Girl.y = home_y + wave offset` every step, so the
  cat/rod cannot leave the surface while a boat exists. `MAX_X = 2000` clamps east travel in the boat step.
* **Lure** (`Lure_Step_0`): tether from `Rod.x/Rod.y` along `ROD_LENGTH/ROD_ANGLE`; `deep = 1` below y 4500;
  `x < 500` is clamped. The rod follows the cat, so “fishing from the sub” needs the rod/tether origin moved.
* **Light:** vanilla `Light` instances (e.g. Bulbfish `light2 = instance_create(x, y, Light)`) light up the
  darkness surface; a headlight can be a `Light` instance that follows the sub.

## Proposed approach (to verify)
1. **Sub object as a child of `CatSwim`** (`jamm_sub`, parent `CatSwim`): vanilla camera, breath bar and
   message logic then work unchanged (`instance_exists(CatSwim)` is true, `CatSwim.tracker` exists). Override only
   what differs: Step (slower, heavier movement, depth limit from hull level, no grabbing or optional grabbing
   with a claw), Draw (sub sprite + headlight cone), keyboard (same arrows, no swim stroke animation).
   Oxygen = breath: set `BreathBar.MAX_BREATH`/drain per sub tier each step after the vanilla BreathBar step.
2. **Buying:** register the sub with the Items API as a tool-shop item (`jamm_tool_register`) or a boat
   (`jamm_boat_register`) – choose what keeps the cat on the boat; price tiers in the compact money format.
3. **Activation:** owning the sub and pressing the dive key on the boat creates `jamm_sub` instead of the
   swimming cat (hook into `Girl_KeyPress_81` or a separate key that vanilla does not use – verify).
4. **Headlight:** a `Light` instance owned by the sub, radius/alpha by upgrade level; turned off at the surface.
5. **Fishing from the sub (v0.2):** while the sub is active, offset the rod tether origin (`tether_x/y` in
   `Lure_Step_0`) to the sub position, or spawn the lure at the sub; needs careful testing with hooked fish,
   `Lure.x < 500` clamp and the deep flag.
6. **Hull/pressure:** maximum safe depth per hull tier; below it the oxygen drains faster and the screen tints
   (no instant death; surfacing restores).

## v0.1 scope
Buy, dive/surface with the sub, headlight, oxygen bar, depth limit by tier, vanilla-style sub art from boat parts.
Fishing from the sub, claw, sonar integration, Endless Ocean travel → v0.2+.
