# Getting Started: Your First Fish in 10 Minutes

This walkthrough creates, validates, builds, and installs a custom fish mod for *Cat Goes Fishing* using **JAMM ModKit**. You only need a text editor, a PNG image editor, and `cgfmm`—no UndertaleModTool or manual room editing required.

## Prerequisites

1. A copy of the **legacy 2023 VM build** of *Cat Goes Fishing* (`data.win` + executable). See the [legacy version guide](../legacy-version.md) if you haven't downloaded it yet.
2. `cgfmm` configured with your untouched legacy base `data.win` and your separate modded game folder:
   ```sh
   cgfmm config set base /path/to/legacy-original/data.win
   cgfmm config set gameDir /path/to/legacy-modded
   ```
   (If you already completed first-run setup in the desktop GUI, these are already set!)

## 1. Scaffold a new package

Run `cgfmm modkit new` with the `fish` template:

```sh
cgfmm modkit new my-reef-fish --template fish
```

This creates a ready-to-edit package folder:

```text
my-reef-fish/
  jamm-mod.json
  art/sample_fish.png
  hooks/sample_step.gml
```

## 2. Draw your fish sprite

Replace `my-reef-fish/art/sample_fish.png` with a transparent PNG (for example `64x48` pixels; the generated placeholder is `48x32`).

Keep two rules in mind:

* **Face left**: *Cat Goes Fishing* fish sprites face **left** (head on the left, tail on the right). The game flips and rotates the sprite automatically when the fish swims right.
* **Animated strips (optional)**: To animate your fish, place frames side by side horizontally in one PNG and set `"frames"` to the number of frames. The total image width must divide evenly by `"frames"`.

## 3. Configure `jamm-mod.json`

Open `my-reef-fish/jamm-mod.json` and customize it for your fish:

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

Helpful notes:

* **Where is the dock?** In `NewWorld`, the player starts on the dock around `x = 815` and the water surface is at `y = 1244`. Spawning at `x: [1100, 2400]`, `y: [1400, 1950]` puts your fish right in shallow water a short cast from the dock.
* **Identifier rules**: `id` and `key` may only use lowercase letters, numbers, underscores, and hyphens (`a-z`, `0-9`, `_`, `-`).
* **Strict JSON**: Standard JSON only—no trailing commas after the last item and no `//` comments inside `jamm-mod.json`.

## 4. Check and build

Validate the JSON manifest, PNG headers/dimensions, and paths without building yet:

```sh
cgfmm modkit check my-reef-fish
```

Expected output:

```text
ModKit package 'My Reef Fish' (my-reef-fish v1.0.0) is valid: 1 fish, 0 boat(s), 0 hat(s), 0 upgrade(s).
```

To compile a standalone `.win` file you can share or test directly:

```sh
cgfmm modkit build my-reef-fish --out my-reef-fish.win
```

Your base `data.win` is opened read-only and never modified.

## 5. Import, enable in a profile, and play

You don't even need to build a `.win` first if you just want to play—`cgfmm add` validates, builds, and imports the package folder straight into your JAMM library:

```sh
cgfmm add my-reef-fish
cgfmm profile create reef --mods "My Reef Fish"
cgfmm profile use reef
cgfmm apply --profile reef
```

*(Or, if you already have a profile you like—for example with **JAMM Console** enabled—include `"My Reef Fish"` in that profile's `--mods` list or toggle it on in the JAMM-CGF desktop window.)*

Now launch the game:

```sh
cgfmm play
```

*(On Windows, run `Cat Goes Fishing.exe` from your configured modded game folder after `cgfmm apply` or GUI **Install**.)*

## 6. Test in-game

1. Cast a short distance to the right of the dock—within a few seconds you should see four **Sunfin** swimming around.
2. Catch one and open the **Fishalog** (Small tab) to confirm its name, count, and `caughtDescription`.
3. **Speed up testing with JAMM Console**: Enable the built-in **JAMM Console** (`cgfmm builtin add console`) in your profile, press `F12` (or `` ` ``) in-game, and run `money +50000` or `unlockgear` when testing boats, hats, or upgrades.

## Next steps

* Browse the **[Cookbook](cookbook.md)** for fast boats, value-boosting hats, custom upgrades, night fish, and fleeing behaviors.
* Check the **[ModKit Reference](modkit-reference.md)** for every JSON field, hook variable, and validation message.
* Learn how JAMM keeps your progress safe across mod changes in **[How Saves Work](saves.md)**.
