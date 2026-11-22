# The Silo

Lower a lure to the Missile Silo in the deep trench to open its hatch. Bring a rod and boat that can reach the trench, and use a light to find the entrance. Veyl, Radik, Ferrex and Garrusk appear in the Fishalog and use the normal lure, bait and catch rules.

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
2. **Animate the door.** Set the existing MissileSilo instance's static `silo-body` sprite and closed `spr_silomask` frame through `opens`. A controller at the same coordinates slides one plate into its guide along the hatch's local X axis. The controller assigns the `shaft-mask` collision sprite when the slide finishes. The native save slot remembers the opening under `JAMM_Areas`.
3. **Give the mouth depth.** The controller draws the near rim at depth −19.25, between the fish and the native lighting pass. Its recipe clips the original rock pixels and aligns them to the static body, so the line, lure and fish pass behind the lip and come back into view below it. Vanilla cave rocks form the walls farther down. Four Geiger lights fade in as bubbles rise from the opening. Each light stores its target brightness in `silo_glow_alpha`.
4. **Assemble each fish.** Use `tones` and SVG parts to keep each sprite's palette compact. Veyl combines a plum body and a taller Garder dorsal sail. Radik keeps its green plates and softens the Sizzer fin edges through `edgeSoftness`. Ferrex uses layered SVG parts for an armored copper-bronze body, a sloped brow with an underslung jaw, and spiny iron-slate fins with a forked crescent tail. Garrusk keeps the continuous Shark tail and adds three tapered bands in its petrol-green body palette.
5. **Place each species.** Each fish's `spawn` points to `launch-shaft`. Its `within` rectangle narrows the range. Veyl stays near the upper shaft, while the single Garrusk starts deeper. JAMM chooses free-water points after the profile's edits and the area's solids have been applied. The Create hook opts into deep-water behavior. The Step hook uses the vanilla wall recovery routine and guides roaming fish back toward their home water when they stray.

Use the [Area API](../../../docs/modding/areas.md) to adapt the layout, or the [sprite recipe reference](../../../docs/modding/modkit-reference.md#sprite-recipes) to build a different species. The package ships the manifest, original SVG details and GML hooks. JAMM samples the base pixels from your own archive during the build.
