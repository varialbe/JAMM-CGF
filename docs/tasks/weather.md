# Task: Weather mod + public Weather API (4 weathers, own backgrounds, fish, music)

User request (2026-11-20): a mod with 4 new weathers – **Super Sunny**, **Rainy**, **Windy** and one more cool one (proposal: **Fog** – thick drifting
sea mist, low visibility, lantern/light upgrades matter; pick something better only if clearly cooler and still a daytime weather). Each weather has its
own animated background, its own surface fish and its own music. It must feel like real weather, NOT like night. And it must be built on a **public
Weather API** so other modders can add custom weathers (this is a must, same importance as the 4 weathers).

## Part A – Weather API (public, documented)
* ModKit `weathers[]` (+ GML functions): `id`, `name`, `weight`/schedule (chance per in-game day, allowed day phases – daytime only by default),
  `duration` range, `transition` (fade time), `background` layers (sprites, parallax factor, scroll speed, animation fps, tint over day progress),
  `particles` (rain drops, wind streaks, sun glints, fog banks… – own objects or a small built-in emitter with documented params),
  `water` (surface colour/tint, wave amplitude multiplier if the game exposes one), `light`/ambient tint, `music` (sound asset, loop, crossfade),
  `ambience` (looping sound), `fish` (list of fish ids that spawn at the surface only during this weather), `replaceDayFish` (default true:
  vanilla daytime surface fish do not spawn/are hidden while this weather is active; they come back afterwards), hooks `on_start`, `on_step`, `on_end`.
* Runtime: one controller (persistent), weather state saved per slot, deterministic schedule from save seed + day, debug command in the JAMM console
  (`weather <id>` / `weather clear`) for testing. Night stays vanilla (weather ends or pauses at dusk; no weather overrides night unless a weather opts in).
* How vanilla day/night, sky/background, surface fish spawning and music work must be studied in the decompiled game first (Girl/Handler/day cycle,
  background objects, fish `stayOutAtNight`/surface spawners, `background_music`). Hook via JAMM's runtime-patch mechanisms with fail-closed anchors;
  minimal, documented vanilla touch points; no collisions with the 7 known mods (check them).
* Docs `docs/modding/weather.md` + api/modkit reference + troubleshooting.

## Part B – The 4 weathers (example mod `examples/modkit/weather-pack/`, also the demo for modders)
* Backgrounds: animated, in Cat Goes Fishing's style – flat colours, soft shapes, the game's palette logic. Build from vanilla background/sky/cloud
  parts via the sprite compose recipe (from `exp/silo-v2`: merge that branch into yours at the start; no vanilla pixels shipped) plus original SVG parts.
  Super Sunny: bright warm sky, sun with slow rays, heat shimmer, sparkles on the water. Rainy: grey layered clouds, rain streaks + splashes/ripples on the
  surface, darker water. Windy: fast-moving clouds, wind lines, choppier surface, leaves/spray. Fog: drifting fog banks in layers, muted colours, low contrast.
  Must not read as night (keep daylight brightness levels; compare against vanilla day/night screenshots).
* Fish: per weather 2–3 new surface fish (8–12 total), composed from vanilla fish parts like the silo fish (same art-review process: side-by-side sheet with
  vanilla fish on the weather's background → `~/jamm-night/weather/art-review.png`), children of vanilla Fish with real behaviour, in the fishalog, CGF-style
  names and short poetic fishalog descriptions that hint at the weather (like Shaku's texts hint at its secret).
* Music: original only. Make one looping ambient track per weather that fits CGF's calm acoustic/chill feel (synthesised in code, e.g. a small Python
  synth: soft plucks/pads, 60–90 s seamless loop, OGG). Keep the pipeline swappable (`music` just points at a file) because the user may replace tracks
  later; document that clearly as a placeholder-quality option. Ambience loops (rain, wind, gulls/sun, foghorn) likewise original.

## Part C – Tests
* Synthetic: API validation errors, schedule determinism, save/load of weather state, replaceDayFish gating (vanilla day fish suppressed and restored),
  night unaffected, a second custom weather from a fake mod coexists.
* VPS in-game (ONE run at a time, 600 s for packs): bench scene that forces each weather via the debug command at the dock/surface, screenshots per weather
  (+ vanilla day and night for comparison) → `~/jamm-night/weather/screens/`, fish count at the surface per weather, no CODE ERROR. Weather pack alone,
  + public 5-pack, + all-7.
* Send the art-review sheet and one screenshot per weather to the user via `~/jamm-night/bin/telegram-photo.sh <img> "<caption>"`.

## Rules
AGENTS.md. Branch `exp/weather` in `~/jamm-wt/weather`, from main, then `git merge exp/silo-v2` (for the compose API). Zero warnings, tests green.
Commit on branch OK, no push/merge to main. Never copy TGE/PWB or vanilla art off the VPS. Report `docs/reports/weather.md`.
