# Submarine 0.2.0

Enable this optional ModKit package in a JAMM profile built against the August 2023 legacy VM version of Cat Goes Fishing. The Boat shop sells the Submarine for $125M (12 fish slots). Out of the box it is exploration only; buy the **Bow Winch** in the Tools shop for **$250M** to unlock casting from the Submarine.

| Action | Keyboard or mouse | Gamepad |
| --- | --- | --- |
| Dive or surface | V | Y / Triangle |
| Steer underwater | Arrows or WASD | D-pad or left stick |
| Toggle the headlight | L | X / Square |
| Rotate the headlight | I / K or E / C | Right stick or LT / RT |
| Peek in light direction | F | LB / L1 |
| Cast (after Bow Winch) | Hold and release Space or left mouse | Hold and release RB / R1 |
| Reel | Hold Space or left mouse | Hold RB / R1 |

Take the sub east of the dock, then dive in open water. Underwater, the camera smoothly zooms out with extra look-ahead in the facing direction, and restores the vanilla view when surfacing. The oxygen meter gives you 110 seconds and refills at the surface. The starter hull stops at 4,800 units below the sea line.

The game saves ownership of both the Submarine and the Bow Winch by stable keys under `[JAMM_Items]`. Submarine 0.1 saves load safely with the Bow Winch unowned until purchased.

The source package contains SVG details and sprite recipes. JAMM composes the hull, shop card, directional headlight beam, and Bow Winch icon from local game assets at build time.
