# Building an area with ModKit

An area places instances in an existing room. Use the room editor on a local copy of the legacy 2023 archive to find the room, instance names and positions. In `NewWorld`, X increases eastward and Y increases with depth. [The Silo tutorial](../../examples/modkit/silo/README.md) shows a shaft linked to an existing landmark.

```json
{
  "id": "trench-demo",
  "name": "Trench Demo",
  "areas": [
    {
      "id": "lower-pocket",
      "bounds": { "x": 33500, "y": 11400, "w": 1600, "h": 1400 },
      "anchor": { "mode": "follow", "object": "MissileSilo", "x": 33984, "y": 11570 },
      "pieces": [
        { "object": "CaveRockLeg", "x": 220, "y": 800, "scaleX": -0.7, "scaleY": 0.8, "rotation": 12 }
      ],
      "lights": [
        { "object": "GeigerLight", "x": 570, "y": 750, "set": { "image_alpha": 0.5 } }
      ],
      "opens": { "object": "MissileSilo", "x": 33984, "y": 11570, "set": { "canOpen": true } }
    }
  ]
}
```

| Field | Meaning |
| --- | --- |
| `id` | Unique area identifier within the package. |
| `room` | Existing legacy room name. Defaults to `NewWorld`. |
| `bounds` | Integer room rectangle. Width and height are positive. The right and bottom edges are exclusive. |
| `pieces`, `lights` | Lists of existing object assets to instantiate. See below for coordinates and transforms. |
| `removes` | Optional list of exact `{object,x,y}` instance selectors in room coordinates. JAMM validates the starting coordinates and only removes matching instances inside the resolved area. |
| `anchor` | `{"mode":"fixed"}` keeps the authored position. This is the default. `follow` needs an object and its exact coordinates in the base. When the original location is empty, JAMM follows the sole remaining instance of that object. If multiple mods moved the same original singleton, JAMM keeps the latest surviving move in load order. |
| `opens` | Optional exact `{object,x,y}` selector in room coordinates with `set`, `sprite`, or `maskFrame`. If the selector matches the follow anchor, JAMM patches that instance at its resolved location. |

Piece and light coordinates are offsets from the bounds' top-left corner. A piece's origin must be inside the bounds. The sprite can extend outside. Supply a uniform `scale` or individual `scaleX` and `scaleY`. Negative scales mirror the sprite. `rotation` is counterclockwise in degrees. The defaults are scale 1 and rotation 0. Both lists support `set` and use the same placement rules. Light objects still run their own Create event, which can initialise fields such as `image_xscale`. Set those fields explicitly to override their initial values.

## Instance creation settings

`set` assigns numbers and booleans to named instance variables after object creation. For `opens`, JAMM retains prior creation code and appends the assignments in a separate code entry attached to the selected instance. Choose a target whose prior creation code reaches the end. Other instances of that object keep their original code. A variable like `canOpen` takes effect only if the base game or another installed mod uses it.

`opens.sprite` selects a base sprite by name or a [sprite recipe](modkit-reference.md#sprite-recipes) by key. JAMM sets the placed instance's `sprite_index` and `mask_index` to that sprite. Use matching coordinates and origin if replacing the appearance of an existing instance. `opens.maskFrame` selects a zero-based collision mask from the original object's mask sprite. If both fields are present, `maskFrame` supplies the collision shape and `sprite` supplies the appearance. An opening that changes a sprite's collision must leave enough room for the lure to enter and exit.

## Fill the area with fish

Add a regular [ModKit fish](modkit-reference.md#fish-entries) with `"spawn": {"area": "lower-pocket", "count": 4}`. To limit a species to part of the area, add `"within": {"x":200,"y":600,"w":450,"h":380}`, relative to the same bounds. JAMM chooses distinct initial positions inside the resolved area and inside the room. In `NewWorld` it starts at Y = 1294. It checks legacy room collision masks, scales and rotations for solid objects and descendants of `Collidable`. An area piece can override this classification with `solid: true` or `solid: false`.

The test is for each fish's initial position. For runtime geometry that changes under GML, check the result in-game. Fish still swim and respond to hooks, lures and collision behavior after spawn. Their normal size classes, prices and Fishalog records come from the fish API.

## Build and check with other mods

Run `cgfmm modkit check <folder>` for manifest validation. Import with `cgfmm add <folder>` and enable it in a profile, or use `cgfmm modkit build <folder>` followed by `cgfmm check <source.win> [...] --base <data.win>` and `cgfmm build`. ModKit's source archive carries the area declarations so the final profile build can place them after selecting other room edits.

Check and build report `[CGF-AREA-001]` when other mods add, move or remove instance positions inside the authored or resolved bounds, or when ModKit areas overlap. The build also reports when a follow anchor moves. Follow shifts coordinates by the anchor's movement and leaves the room axes unchanged. Review reported overlaps in-game, especially if the relocated anchor also rotates. Use `fixed` when the area belongs at a specific coordinate.
