# ModKit & API Cookbook

Short, copy-paste-ready recipes you can mix and match in your own mods. Most recipes use **JAMM ModKit** (`jamm-mod.json` + PNG + `.gml`); Recipe 9 shows a **GML API** example for rods and lures.

---

## 1. A fast boat

Boats don't need any GML hooks—set `stats` directly in `jamm-mod.json`. Remember that the in-world `sprite` is drawn at `0.5x` scale (so `240x80` becomes `120x40` in-game).

```json
{
  "id": "speed-skiff",
  "name": "Speed Skiff",
  "version": "1.0.0",
  "boats": [
    {
      "key": "hydro-skiff",
      "name": "Hydro Skiff",
      "cost": 45000,
      "card": "art/skiff_card.png",
      "sprite": "art/skiff.png",
      "description": "Cuts across the waves at high speed.#Holds 6 fish.",
      "unlocked": false,
      "stats": {
        "maxSpeed": 9.5,
        "capacity": 6,
        "maxDistance": 7000,
        "moveBonus": 1.0
      }
    }
  ]
}
```

---

## 2. A hat that boosts fish value

A hat's `effect` hook runs in `Gear` `Other_15` whenever the hat is equipped. Base `Gear_Other_15` resets `value_scalar[0..3]` (`0` small, `1` medium, `2` large, `3` huge) to `1` before your hook runs.

**`jamm-mod.json`:**

```json
{
  "id": "merchant-hat",
  "name": "Merchant Hat",
  "hats": [
    {
      "key": "gold-cap",
      "name": "Merchant Cap",
      "sprite": "art/gold_cap.png",
      "value": 3000,
      "weight": 5,
      "rangeMin": 0.1,
      "rangeMax": 0.8,
      "description": "Small and medium fish sell for 35% more.",
      "hooks": { "effect": "hooks/gold_cap_effect.gml" }
    }
  ]
}
```

**`hooks/gold_cap_effect.gml`:**

```gml
value_scalar[0] = 1.35;
value_scalar[1] = 1.35;
```

---

## 3. An upgrade with a `step` hook

`step` hooks are called every `Begin Step` by `obj_jamm_api` with `argument0 = stacks` while at least one copy is installed on the active rod. Use a marker variable on the target so the effect applies once instead of compounding every frame.

This is the exact pattern used in [`examples/modkit/showcase-upgrades/hooks/golden_step.gml`](../../examples/modkit/showcase-upgrades/hooks/golden_step.gml) to add `+25%` sale value per stack right when a fish is caught:

```gml
// argument0 = installed stack count on the active rod.
var jamm_stacks;
jamm_stacks = argument0;
if (instance_exists(Lure) && Lure.hookedFishSize > 0 && Lure.fish > 0 && instance_exists(Lure.fish))
{
    with (Lure.fish)
    {
        if (!hasGivenExperience)
        {
            jmk_golden_touch_scaled = 0;
        }
        else if (!variable_instance_exists(id, "jmk_golden_touch_scaled") || !jmk_golden_touch_scaled)
        {
            jmk_golden_touch_scaled = 1;
            value = round(value * (1 + (0.25 * jamm_stacks)));
        }
    }
}
```

---

## 4. A night-only fish

Set `"night": true` so `stayOutAtNight = 1` (preventing the base game's automatic `state = 10` night-retreat), then use a `step` hook to hide deep during daylight and glow at night.

**`jamm-mod.json` snippet:**

```json
{
  "key": "moon-tetra",
  "name": "Moon Tetra",
  "size": "small",
  "sprite": "art/moon_tetra.png",
  "spawn": { "x": [1300, 2600], "y": [1500, 2100], "count": 4 },
  "night": true,
  "hooks": {
    "create": "hooks/moon_create.gml",
    "step": "hooks/moon_step.gml"
  }
}
```

**`hooks/moon_create.gml`:**

```gml
glow_phase = random(6.283);
```

**`hooks/moon_step.gml`:**

```gml
if (is_it_night())
{
    var pulse;
    pulse = 0.5 + (0.5 * sin((current_time * 0.005) + glow_phase));
    image_blend = merge_color(c_aqua, c_white, pulse);
    image_alpha = 1;
}
else
{
    image_blend = c_dkgray;
    image_alpha = 0.45;
    if ((state == 0 || state == 2) && y < 4000)
    {
        vspeed += 0.6;
    }
}
```

*(See [`examples/modkit/example-fish/`](../../examples/modkit/example-fish/) for the full working Glowfin example.)*

---

## 5. A fish that flees

In a fish `step` hook (`Step_2`), check `state == 0` (idle), `lure_distance_squared`, and a cooldown timer so the fish dashes away from fast or close lures without flying across the map on every frame.

**`hooks/shy_create.gml`:**

```gml
flee_cd = 0;
```

**`hooks/shy_step.gml`:**

```gml
if (flee_cd > 0)
{
    flee_cd -= 1;
}
// Flee when any active lure (Lure.state == 2) gets within 140 px (140*140 = 19600)
if (state == 0 && instance_exists(Lure) && Lure.state == 2 && lure_distance_squared < 19600 && flee_cd <= 0)
{
    var away;
    away = point_direction(Lure.x, Lure.y, x, y);
    hspeed += lengthdir_x(5.0, away);
    vspeed += lengthdir_y(5.0, away);
    flee_cd = 40;
}
```

---

## 6. Reading upgrade stacks from a fish hook

Any fish hook (or hat/lure script) can call `jamm_upgrade_stacks("<modid>:<upgrade-key>")` to react to upgrades installed on the player's current rod—even upgrades from **another** mod!

For example, make a skittish fish calm down and award bonus cash when the player has **Fish Magnet** from `showcase-upgrades` installed:

**`hooks/magnet_fish_step.gml`:**

```gml
// Check if "showcase-upgrades:magnet" is installed on the active rod
var mag;
mag = jamm_upgrade_stacks("showcase-upgrades:magnet");
if (mag > 0 && state == 0 && lure_distance_squared < 25600)
{
    // Drift gently toward the lure when Fish Magnet is installed
    var toward;
    toward = point_direction(x, y, Lure.x, Lure.y);
    hspeed += lengthdir_x(0.08 * mag, toward);
    vspeed += lengthdir_y(0.08 * mag, toward);
}
```

And in a `caught` hook (`hooks/magnet_fish_caught.gml`):

```gml
var mag;
mag = jamm_upgrade_stacks("showcase-upgrades:magnet");
if (mag > 0)
{
    value += 100 * mag;
}
```

*(Note: when compiling a ModKit package standalone, `ModKitBuilder` only emits placeholders for `jamm_*_register` calls it generates. If a hook calls `jamm_upgrade_stacks` directly, compile via JAMM profile build or add a `jamm_upgrade_stacks` placeholder if building standalone.)*

---

## 7. Multi-item mod (fish + boat + hat + upgrade in one package)

One `jamm-mod.json` can define any combination of `fish`, `boats`, `hats`, and `upgrades` together:

```json
{
  "id": "coral-bundle",
  "name": "Coral Bundle",
  "version": "1.0.0",
  "author": "Your Name",
  "description": "Adds a coral fish, boat, hat, and upgrade in one package.",
  "fish": [
    {
      "key": "coral-tang",
      "name": "Coral Tang",
      "size": "small",
      "sprite": "art/tang.png",
      "spawn": { "x": [1100, 2200], "y": [1400, 1900], "count": 4 }
    }
  ],
  "boats": [
    {
      "key": "coral-dinghy",
      "name": "Coral Dinghy",
      "cost": 18000,
      "card": "art/dinghy_card.png",
      "sprite": "art/dinghy.png",
      "stats": { "maxSpeed": 5.5, "capacity": 5 }
    }
  ],
  "hats": [
    {
      "key": "coral-crown",
      "name": "Coral Crown",
      "sprite": "art/crown.png",
      "value": 2500
    }
  ],
  "upgrades": [
    {
      "key": "coral-crank",
      "name": "Coral Crank",
      "description": "Reel speed +1.5 per stack.",
      "description2": "Faster reeling.",
      "cost": 0.25,
      "icon": "art/crank.png",
      "maxStacks": 3,
      "hooks": { "apply": "hooks/crank_apply.gml" }
    }
  ]
}
```

All four items compile into a single `jamm_init_coral_bundle` script and share the `"coral-bundle:<key>"` namespace.

---

## 8. Fast edit-check-build-test loop

Keep a terminal open and iterate quickly without manually clicking through menus every time:

```sh
# 1. Validate JSON, PNGs, and hook paths in <1s
cgfmm modkit check my-mod

# 2. Optional: test standalone compile + archive verification
cgfmm modkit build my-mod --out /tmp/my-mod.win

# 3. Import into JAMM library, create a dev profile (with Console), and launch
# (If re-importing after edits, delete the old profile and `cgfmm remove "My Mod"` first, or pass `--name`)
cgfmm add my-mod
cgfmm profile create dev --mods "JAMM Console","My Mod"
cgfmm apply --profile dev --yes
cgfmm play
```

Once in-game, press **`F12`** (or `` ` ``) to open **JAMM Console** and run `money +500000` or `unlockgear` to immediately buy and test your boats, hats, and upgrades.

---

## 9. Bonus: Custom rod or lure with the GML API (UndertaleModTool)

ModKit focuses on fish, boats, hats, and upgrades; to add **rods**, **lures**, or **tools**, create a `.win` mod in UndertaleModTool with your new sprite/object assets, placeholder scripts (`return -1;`), and a `jamm_init_<id>` script:

```gml
// Script: jamm_init_angler_gear
jamm_rod_register(
    "angler-gear:carbon-rod",
    "Carbon Rod",
    85000,      // cost
    15,         // cast_speed
    1.9,        // rod_power
    4,          // rod_power_flat
    11.5,       // reel_speed
    5200,       // sinker_distance
    spr_carbon_rod,
    7,          // upgrade_slots (0..8)
    0           // unlocked (0 = in shop)
);

jamm_lure_register(
    "angler-gear:flare-lure",
    "Flare Lure",
    2000,
    obj_flare_lure,     // child of PowerUp
    spr_flare_lure_card,
    "Glows brightly in deep caves.",
    1           // unlocked (1 = available in shop)
);
return 0;
```

Add that `.win` with `cgfmm add angler-gear.win` (or in the GUI) alongside any ModKit or legacy mods!
