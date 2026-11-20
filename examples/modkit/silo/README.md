# The Silo

Fish the flooded launch shaft below the Missile Silo in the deep trench. Bring a rod and boat that can reach the trench, and use a light to find the entrance. Veyl, Radik, Ferrex and Garrusk appear in the Fishalog and use the normal lure, bait and catch rules.

## Build the mod

In a JAMM library configured with the August 2023 legacy archive, run `cgfmm add examples/modkit/silo` and enable **The Silo** in a profile. For a standalone check or build, run these commands from the repo root.

```sh
cgfmm modkit check examples/modkit/silo
cgfmm modkit build examples/modkit/silo --base /path/to/legacy/data.win --out silo-source.win
cgfmm check silo-source.win --base /path/to/legacy/data.win
cgfmm build --base /path/to/legacy/data.win --order silo-source.win --out silo-merged.win
```

## How the shaft was built

1. **Pick a landmark.** In the legacy NewWorld room, `MissileSilo` sits at (33984, 11570). The `launch-shaft` area's bounds start a little to the left and above it. The `follow` anchor translates the layout if another mod moves the silo. Cave Expansion is an example.
2. **Make the opening part of the room.** The `opens` selector targets that one silo. Its creation settings set `canOpen` and select the `open-silo` sprite. That recipe keeps the edges of vanilla `spr_silo` and leaves a channel through the middle. JAMM uses its alpha mask for collision. The vanilla object, Create event and Step event stay intact.
3. **Build around the water.** Place vanilla cave rocks and Geiger lights using coordinates relative to the area. Flip and rotate the rocks to close the walls and let the lure enter from above. Use the light's native Create event, then set `add_blend`, `image_alpha` and scale on each placement to get a faint green glow.
4. **Assemble each fish.** Start with a head and a compatible body from the legacy base. Change the silhouette with different fins and tails. Veyl combines the Cuddlefish head and Underfin tail, then adds original sensory pits. Radik combines Geiger, Powerfin and Garder parts with original scutes. Ferrex uses a Magmer head and body with a longer Noxius tail. Garrusk uses the Maw jaw, Shark body and Bagu tail.
5. **Place each species.** Each fish's `spawn` points to `launch-shaft`. Its `within` rectangle narrows the range. Veyl stays near the upper shaft, while the single Garrusk starts deeper. JAMM chooses free-water points after the profile's edits and the area's solids have been applied. The Create hook opts into deep-water behavior. The Step hook uses the vanilla wall recovery routine and guides roaming fish back toward their home water when they stray.

Use the [Area API](../../../docs/modding/areas.md) to adapt the layout, or the [sprite recipe reference](../../../docs/modding/modkit-reference.md#sprite-recipes) to build a different species. The package ships the manifest, original SVG details and GML hooks. JAMM samples the base pixels from your own archive during the build.
