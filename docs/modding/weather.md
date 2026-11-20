# Weather API

Add `weathers[]` to a ModKit package targeting the August 2023 legacy game. JAMM installs a single weather controller when it builds the profile. Several packages can add weather to the same profile. Every entry has a qualified ID such as `weather-pack:fog`, built from the package ID and the entry ID.

Start with the [Weather Pack](../../examples/modkit/weather-pack/README.md). The manifest below shows a weather that uses the built-in particle emitter and leaves the existing schools in place.

```json
{
  "id": "coast",
  "name": "Coastal Weather",
  "weathers": [
    {
      "id": "sea-mist",
      "name": "Sea Mist",
      "weight": 1,
      "schedule": { "phases": ["dawn", "day"] },
      "duration": [300, 720],
      "transition": 3,
      "sky": "#d5e4df",
      "particles": {
        "kind": "mist",
        "count": 28,
        "speed": 0.7,
        "color": "#e8f2eb",
        "opacity": 0.45
      },
      "water": { "tint": "#cfdbd6", "wave": 0.7 },
      "light": "#f0f2ec",
      "replaceDayFish": false
    }
  ]
}
```

```sh
cgfmm modkit check coastal-weather
cgfmm add coastal-weather
cgfmm profile create coast --mods "Coastal Weather"
cgfmm play --profile coast --yes
```

You can distribute the source folder or build a source archive with `cgfmm modkit build`. In the archive workflow, pass the generated `.win` as a mod to `cgfmm build --base <legacy-data.win> --order <source.win> --out <new.win>`. The source archive carries declarations and assets. The final build resolves the declaration references, installs the controller, and patches the selected legacy code.

## Manifest fields

| Field | Meaning |
| --- | --- |
| `id`, `name` | Required. The ID is unique in the package and uses lowercase letters, numbers, hyphens and underscores. The name appears in the console. |
| `weight` | Integer weight from 0 to 1000. Default 1. Use 0 for weather you only trigger manually. Clear skies carry a built-in weight of 1. |
| `schedule` | Optional `weight` overrides the top-level weight. `phases` accepts a nonempty list from `dawn`, `day`, `dusk`, and `night`. Default `["dawn","day","dusk"]`. |
| `duration` | Ordered whole-second range `[min,max]` within 1–3600. Default `[600,1800]`. |
| `transition` | Presentation fade in seconds, 0–30. Default 3. Fish and hooks change when the new weather becomes active. |
| `sky` | Optional flat RGB colour `#rrggbb` for the sky behind the parallax scenery. |
| `background` | Animated [sprite recipe](modkit-reference.md#sprite-recipes) layers. See below. |
| `particles` | Optional built-in emitter. `kind` is `rain`, `wind`, `glints`, or `mist`. `count` is 0–180 (default 48), `speed` is a 0.1–4 multiplier (default 1), `color` is `#rrggbb` (default white), and `opacity` is 0–1 (default 0.45). Rain strokes become surface ripples. |
| `water` | `tint` blends the Sea sprite toward the chosen multiplicative tint (white by default). `wave` scales the surface drift and native wave height function, 0–3, default 1. |
| `light` | Optional multiplicative `#rrggbb` tint for the game's depth lighting. Values close to white preserve daytime brightness. |
| `music`, `ambience` | Each takes a package-relative `sound` path to an Ogg Vorbis file, `loop` (default true), `crossfade` in seconds from 0–30 (default 2.5), and `volume` from 0–1 (defaults 0.65 and 0.3 respectively). The runner plays the embedded files. Both channels respect the game's music volume and fade with lure depth. |
| `fish` | Keys from the package's `fish[]`. Each listed species is registered in the Fishalog and populated by weather. |
| `replaceDayFish` | Defaults to true. When weather is active in daylight, it parks eligible vanilla surface fish and reactivates them on clear, nightfall or a weather with replacement off. |
| `hooks` | Optional paths to `on_start`, `on_step`, and `on_end` GML files. |

A background layer names a `sprite` from `sprites[]`. `plane` defaults to `sky`, behind the vanilla landscape. `foreground` draws over the world in the main view before GUI. `x` offsets the layer horizontally (default 0). `y` offsets its top edge from the sea at room Y = 1294 (default −390). `parallax` controls how much it follows world movement, 0–1 (default 0.15). `scroll` is horizontal world pixels per second, −600 to 600 (default 0). `fps` is 0–60 (0 holds frame zero). `scale` is 0.05–10 (default 1), `opacity` is 0–1 (default 1), and `repeat` defaults to true. With `repeat: false`, the sprite starts centered in the view plus its X offset. `tintStart` and `tintEnd` take `#rrggbb` and interpolate through the daytime clock. The end tint defaults to the start tint, and the start tint defaults to white.

## Surface fish

Add fish in the usual ModKit format and list their keys in `weathers[].fish`. For these fish, `spawn.x` is an offset from the cat's current shoreline or boat position (within −4000 to 4000), `spawn.y` is absolute world depth (1310–1950), and `spawn.count` is the school size (1–24). The API samples clear water positions east of the beach and uses the normal Fish parents, bite, catch and Fishalog systems. The chosen weather's night opt-in controls whether its school can remain out at night. When weather ends, free fish retire and catches in progress can finish.

Replacement applies to free fish with a vanilla Fish object identity, a home and current position in the surface band, and `stayOutAtNight == 0`. Existing room and path records stay in place, so vanilla fish return to their previous routes when released. Mod-added fish, fish that stay out at night, deep fish and catches in progress use their normal behavior.

## Schedule, saves and console

CGF's legacy clock advances one game day per local-clock hour. Dawn is 00:00–01:29 within that hour, day is 01:30–48:59 and dusk is 49:00–50:59. Vanilla `is_it_night()` determines night, including gear effects and the early-game day lock. The schedule selects at most one weather and chooses its start second from the allowed clock phases. A weather can run only through permitted phases within its scheduled window. Change a phase to `night` only if you want a weather to override that part of the vanilla cycle.

The controller stores its seed and current forecast in `jamm-weather-<slot>.ini` in the game's save directory. The forecast is keyed by qualified ID with start and end seconds. Reopening the same slot in the same clock hour resumes that forecast. The schedule uses the slot's seed, the clock hour and the list of weather IDs in sorted order. Installed weather changes enter the draw on the next hour. Copy the weather INI with a save if you want to preserve its forecast on another machine.

The merged profile includes the JAMM Console. Open it with **F12** or **backtick**.

| Command | Effect |
| --- | --- |
| `weather` | Lists the installed weather IDs. |
| `weather fog` | Forces a weather by short ID when the ID is unique. |
| `weather weather-pack:fog` | Forces the specified package's weather. Default daytime weather waits until daytime if called at night. |
| `weather clear` | Clears weather temporarily. |
| `weather auto` | Resumes the saved forecast. |

Console overrides reset on slot change or game restart. The console is for visual and gameplay testing. Use the manifest schedule for normal gameplay.

## GML functions and hooks

| Function | Result |
| --- | --- |
| `jamm_weather_current()` | Active qualified ID or an empty string. |
| `jamm_weather_name()` | Active display name or an empty string. |
| `jamm_weather_intensity()` | Current activation fade, from 0 to 1. |
| `jamm_weather_command("package:id")` | Requests a debug override and returns the console feedback string. Accepts a unique short ID, `clear` and `auto`. The next controller tick applies the request. |

Each hook runs in the controller's instance context. `argument0` is the qualified ID and `argument1` is the current fade (0–1). `on_start` runs once when activated, `on_step` runs every active step, and `on_end` runs once as the weather stops. The controller owns scheduling, audio and populations. Keep weather-specific state on the controller under a package-specific variable name. For a light interaction, the Fog example uses `weather_foreground_scale` to attenuate foreground layers and particles while a native Lightbulb is near the sea. Use a value from 0 to 1.

## Legacy integration

The game derives phases and light coefficients from `AmbianceController` and `is_it_night()`. The controller reads that state while `SaveManager` identifies the active slot. The merged runtime installs these anchors.

| Entry | Weather integration |
| --- | --- |
| `ParallaxBackground.Draw` | Tints the sky rectangle and draws weather sky layers behind the landscape. |
| `Sea.Step`, `x_pos_to_y_off` | Blend surface tint and apply the wave multiplier. |
| `color_from_depth` | Blends the configured ambient tint into native lighting. |
| `Fish.End Step` | Parks eligible vanilla fish and retires free weather fish when their weather ends. |
| `Girl.Step` | Mixes weather music against the native surface track at its gain update. The Halloween track selection and depth track keep their native logic. |
| `obj_jamm_console.Begin Step` | Dispatches `weather` commands. JAMM installs the console if the profile does not already have it. |

JAMM looks for each anchor exactly once in the code that survives the merge. A missing or ambiguous anchor fails the build and identifies the entry to inspect. See [troubleshooting](troubleshooting.md#weather).
