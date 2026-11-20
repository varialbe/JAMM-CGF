# Weather Pack

A ModKit demo for the public [Weather API](../../../docs/modding/weather.md). Import this folder, add **Weather Pack** to a legacy profile and build or play it through JAMM. Open the JAMM Console with F12 and use `weather <id>` at the dock.

| Weather | Console ID | Surface fish |
| --- | --- | --- |
| Super Sunny | `super-sunny` | Bask, Flaret |
| Rainy | `rainy` | Drizzit, Lowbelly |
| Windy | `windy` | Kest, Tack |
| Fog | `fog` | Mote, Veilmouth |

Try the native Lightbulb lure near the surface in Fog to see the haze thin out.

The sprite recipes sample the player's legacy data at build time and combine it with the original SVG parts in `parts/`. The sky uses the same mountain source with different daylight palettes and layers. Each fish has its own shape, colour, swimming speed and Fishalog text.

The eight Ogg files in `audio/` are original synthesized placeholder-quality loops. Music loops last 64–84 seconds. Atmosphere loops last 24 seconds. To regenerate them, run `python3 make-audio.py` with Python 3 and ffmpeg on your path. To replace a track, point the weather's `music.sound` or `ambience.sound` at your own Ogg Vorbis file inside the package and rebuild. The loop, gain and crossfade settings are in `jamm-mod.json`.
