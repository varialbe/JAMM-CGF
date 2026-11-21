# Submarine

The Submarine is an optional ModKit package for the August 2023 legacy VM build. Version 0.1.0 adds a late-game boat that carries the cat underwater and can fish from depth. The package builds its art from the player's own archive and original vector details.

## Legacy findings

| System | Behavior in the August 16 archive |
| --- | --- |
| Boat and rider | The boat shop selects an object from `BoatData.object[]`. `Boat` Begin Step drives `Girl.hspeed` and enforces the boat's east limit. Normal Step sets `Girl.y` to the boat's starting height plus wave motion. Draw Begin positions the hull and its background from the rider. |
| Swimming and camera | Q enters vanilla `CatSwim` at level 25 or higher. Its tracker drives the main camera while it has breath. Its presence also hides the cat and blocks both rod swing events. Outside swimming, the camera follows a cast lure, or the cat. When an idle boat has `wasJustMoving` cleared, the camera uses a fixed sea-line Y. |
| Lure and rod | The room creates a persistent `Lure` instance. `Girl` sets the rod anchor and the lure computes its tether from the rod position, angle and velocity. Lure state 0 is held, 1 is traveling and 2 fishes and reels. A cast enters state 2 as soon as it reaches the water and the vanilla reel automatically brings in a line shorter than 100 world units. The deep flag is set at world Y greater than 4,500. |
| Lighting | `LightingSystem` draws the depth/night darkness surface and subtracts visible `AbstractLight` sprites from it. `Light` contributes a small additive highlight and a larger subtraction footprint. `GeigerLight` inherits that system and adjusts its parameters. The lure's bulb creates its own light. |
| Item registration | `jamm_boat_register` takes a stable key, shop price, card sprite, boat object and description. JAMM adds the card to the paged shop and saves ownership and equipment by key. The factory fills in the runtime boat slot when the boat enters play. |

A child of `Boat` keeps a single rider and boat instance, shop integration, fish storage and surface controls. Its own events manage underwater movement and a short line deployment. This avoids coupling the vehicle to `CatSwim`'s grab state, breath progression and cast restrictions. Normal boat, swim, camera and lure code can run without a submarine-specific patch.

## v0.1.0

- Buy the **Submarine** for **$125M** on the Boat shop's additional page. It is available to buy immediately and has 12 fish slots. Equip it and take it east of the dock before diving. Use any normal rod and bait.
- Press **V** or **Y / Triangle** to dive or surface. Use arrows or **WASD**, the D-pad or the left stick to steer underwater. Press **L** or **X / Square** for the headlight. Use the normal fishing controls (hold and release **Space**, left mouse or **RB / R1** to cast, then hold to reel). Use Q for vanilla swimming from the surface.
- The starter hull reaches **4,800 world units below the surface**. It checks the terrain in short increments and stops at solid obstacles. It starts with **110 seconds of oxygen** and refills at the surface. The HUD reports remaining time, depth and lamp status. A light uses the vanilla darkness surface and moves with the bow.
- An underwater cast starts from the bow. The short deployment pays out enough line for the vanilla fishing and reeling state to take over. Movement pauses while the line is out. Reel in to resume navigation or surface. A held catch can travel with the sub and use the standard boat storage and sell controls.
- On empty oxygen the sub releases a deployed line and ascends. A blocked ascent returns the sub to the point where it entered the water. Normal surfacing can be requested at any remaining oxygen.
- The main camera follows the rider when navigating and follows the lure during fishing. Ordinary sonar still follows the lure. Weather continues to use the world clock, with no change to the depth limit or oxygen. The Silo's launch shaft is below the starter depth limit and requires its own package. Endless Ocean terrain stays owned by its package and v0.1 works within the sub's legacy travel limits.
- The boat key is `submarine:submarine`. Saves keep the purchase and equipped slot through JAMM Items. Diving, oxygen and lamp state reset at load, with the sub at the surface. An equipped sub can be removed and later re-added through the normal item migration path.

## Vehicle API

v0.1 uses opt-in ModKit vehicle hooks on a registered boat. A boat can reference sprite recipe keys for its world and card art. Its lifecycle hooks can initialize fields after standard Boat creation and handle Begin Step, Step, End Step, Draw GUI and Destroy. A Begin Step or Step hook can call `event_inherited()` for vanilla behavior where needed. The Destroy hook runs before inherited cleanup. Other mods can use these hooks to make another vehicle and inherit the same shop and save path. Boat state stays on the boat instance. See the ModKit reference for the event contract.

A shared, read-only dive query becomes worthwhile when another mod needs to react to a submerged player. v0.2 can expose the active vehicle instance, submerged flag, depth and remaining oxygen through a qualified lookup. Any future cast or route callbacks should have a single owner per vehicle, with saves continuing to use the registered item key.

## Proposed upgrades

Present prices in compact form and store full integer amounts. These tiers are design targets for v0.2 and later.

| Upgrade | Price | Target effect |
| --- | ---: | --- |
| Starter submarine | $125M | 4,800 depth, 110 s oxygen |
| Twin Air Tanks | $240M | 180 s oxygen |
| Long-Range Battery | $360M | Separate lamp battery, 150 s of continuous light |
| Reinforced Hull | $650M | 8,500 depth and a warning near rated depth |
| Pressure Hull | $1.2B | 12,500 depth and access to the Silo approach |
| Survey Sonar | $1.6B | Vehicle-centered terrain and waypoint scan |

A later pressure model can raise oxygen drain and lower thrust below rated depth, with a firm limit beyond the warning range. Battery drains only with the lamp on and recharges at the surface independently of oxygen. Upgrade ownership should be per save slot under stable keys. Purchases above $999M need a verified economy and formatter that support the full amount.

## Roadmap and risks

| Version | Work |
| --- | --- |
| 0.1 | Buy, steer, light, oxygen, safe ascent, short underwater cast, source-only art and two in-game build checks |
| 0.2 | Hull, battery, tank tiers, explicit save migration, terrain and recovery tests around the Silo |
| 0.3 | Survey sonar, Endless Ocean travel and return with streamed terrain, weather effects on the surface leg |
| 1.0 | Balance upgrade costs and unlocks, test long sessions, profile changes, controller play and optional pack combinations |

The highest-risk paths are releasing or landing a fish while the boat changes depth, event order around the rod anchor, surfacing beneath a ceiling, gamepad polling alongside menu input and light behavior when the scene is dark. The bench should exercise the real boat shop purchase and stock reel behavior. It should record the actual hull, camera, lure and tether positions at depth and capture a side-by-side review of locally composed hull art against vanilla boats.
