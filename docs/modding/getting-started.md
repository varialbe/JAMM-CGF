# Getting Started: Your First Fish in 10 Minutes

This walkthrough builds a custom fish mod for *Cat Goes Fishing* using JAMM ModKit — no manual room editing or binary patching required.

## Prerequisites

1. The legacy 2023 VM build of *Cat Goes Fishing* (`data.win`).
2. `cgfmm` configured with your base archive:
   ```bash
   cgfmm config set base /path/to/legacy/data.win
   cgfmm config set gameDir /path/to/legacy/game
   ```

## 1. Scaffold a new package

```bash
cgfmm modkit new my-reef-fish --template fish
```

This creates:

```text
my-reef-fish/
  jamm-mod.json
  art/sample_fish.png
  hooks/sample_step.gml
```

## 2. Draw your fish sprite

Replace `art/sample_fish.png` with any transparent PNG (for example `64x48` pixels).

* **Orientation**: *Cat Goes Fishing* fish sprites face **left** (head on the left, tail on the right). The game flips `image_yscale` and rotates `image_angle` automatically when swimming right.
* **Animation strips**: For animated sprites, lay frames out horizontally in one PNG and set `"frames"` to the frame count (`width` must be divisible by `frames`).

## 3. Configure `jamm-mod.json`

Open `my-reef-fish/jamm-mod.json`:

```json
{
  "id": "my-reef-fish",
  "name": "My Reef Fish",
  "version": "1.0.0",
  "author": "Your Name",
  "description": "Adds a bright reef fish near the dock.",
  "fish": [
    {
      "key": "sunfin",
      "name": "Sunfin",
      "size": "small",
      "sprite": "art/sample_fish.png",
      "frames": 1,
      "value": 350,
      "xp": 75,
      "stamina": 45,
      "lungeSpeed": 8.5,
      "lungeRange": 65,
      "speed": 1.6,
      "spawn": { "x": [1100, 2400], "y": [1400, 1950], "count": 4 },
      "night": false,
      "description": "A sunny reef fish that loves shallow water.",
      "caughtDescription": "Caught Sunfin! Its warm scales glow near the surface."
    }
  ]
}
```

Tips:
* In the game's `NewWorld` room, the player starts on the dock at `x = 815`, and the water surface is at `y = 1244`. Spawning fish at `x: [1100, 2400], y: [1400, 1950]` places them right in shallow water near the dock.
* Keep `id` and `key` lowercase (`a-z`, `0-9`, `_`, `-`).

## 4. Validate and build

Validate the manifest, PNGs, and hooks:

```bash
cgfmm modkit check my-reef-fish
```

Build a standalone mod `.win` (never modifies your base `data.win`):

```bash
cgfmm modkit build my-reef-fish --out my-reef-fish.win
```

Or add the folder directly to JAMM's mod library and enable it in a profile:

```bash
cgfmm add my-reef-fish
cgfmm profile create reef --mods "My Reef Fish"
cgfmm apply --profile reef
```

## 5. How JAMM merges ModKit mods

Every ModKit mod only adds new sprites, objects, hook scripts (`jmk_<id>_<key>_*`), and one `jamm_init_<id>` script — it never touches `SaveManager`, `CatalogData`, or shared rooms. When you build or apply a profile with multiple ModKit mods, JAMM wires all `jamm_init_*` scripts together through `jamm_api_init` and replaces the `jamm_*_register` placeholders with the real runtime implementations.
