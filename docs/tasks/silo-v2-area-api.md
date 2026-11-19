# Task: Silo v2 – a real in-world area built entirely through JAMM's public APIs (+ new Area API)

The current Silo (`exp/silo-area`, merged) is a separate room with procedural art and its own diver. The user rejects it: it does not look or play
like Cat Goes Fishing ("AI slop"). Replace it with a real place in the game world, with custom fish that match the game's art style, built ONLY
through JAMM's public modding APIs, so it doubles as the flagship demo mod for other modders.

## Concept (already decided)
* The vanilla game already hints at it: `MissileSilo` sits in the deep trench at (33984, 11570), has a 5-frame opening mask `spr_silomask`, but
  `canOpen = 0`; vanilla places radioactive-looking fish next to it (3x Geiger, Noxius, Powerfin). Theme: the sealed silo opens; below it a flooded
  launch shaft / chamber in NewWorld, reached the normal way (boat + line + lure, sonar, light upgrades). Dark, faint green glow. Deep → needs a good rod.
* Built from VANILLA building blocks only (CaveRock*/CaveRockBig pieces, vanilla lights / AbstractLight children, vanilla decoration objects),
  so it looks like the rest of the game. No custom UI banners, no own diver, no procedural gradients, no text overlays in the world.
* 4–5 custom fish with real behavior (children of vanilla `Fish` → swim, avoid walls, lunge, flee), in the fishalog via the catalog API, with normal
  size classes and prices. Themes, e.g.: a "Fallout" relative of the Geiger, a blind shaft fish, a rust-coloured eel-like fish along the walls, a big
  guardian deep in the chamber (rare). Names fitting CGF's naming style (short invented words like Geiger, Noxius, Magmer, Garder).

## Part A – Area API (new, public, documented, reusable)
Add what is missing so any modder can build an area:
* ModKit JSON `areas[]` (and a matching GML function if sensible): `id`, `room` (default NewWorld), `bounds` {x,y,w,h},
  `pieces[]` = instances of existing objects by name (vanilla or own) with x/y (relative to bounds), scale, rotation, `lights[]`, optional `removes[]`
  only for instances inside the own bounds, and an optional `opens` hook for existing objects (e.g. set a variable on an existing instance via a
  documented per-instance creation-code patch, never by editing the object's events).
* Collision safety at build time: JAMM knows every other mod's NewWorld edits. If another mod adds/moves/removes instances inside an area's bounds,
  report it (check + build decision `[CGF-AREA-001]`), and let the area declare `anchor` behaviour: `fixed` (warn) or `follow` an existing instance
  (e.g. follow the MissileSilo if another mod moved it – Cave Expansion moves it to (35472, 11984) and sets canOpen = 1).
* Fish spawning inside areas: extend fish `spawn` so it can reference an area id and only picks free-water points (no spawn inside solid rocks) –
  this also answers a modder's concern that fish spawned by rectangle can end up stuck in caves.
* Docs: `docs/modding/areas.md` + api/modkit reference + troubleshooting. Validation errors with human messages.

## Part B – Fish art: custom fish composed from vanilla fish parts
The user's decision: the new fish are custom, but built by REUSING parts of vanilla fish (heads, fins, tails, bodies, eyes) as building blocks,
recoloured and recombined – like a kit – so they automatically match the game's style. Own SVG parts only where no vanilla part fits (e.g. glow spots).
* Legal/packaging rule (JAMM principle): never ship vanilla pixels. The mod ships a **composition recipe** (JSON) + only original SVG/PNG parts.
  JAMM composes the final sprites at build time from the player's own data.win.
* Add this as a reusable public feature (part of the API, documented): ModKit `sprites[]` entries with `compose: [ { from: "<vanilla sprite name>",
  frame, crop {x,y,w,h} (optional), x, y, scaleX, scaleY, rotation, flip, tint / hue-shift / palette-map, opacity, z } , { file: "parts/glow.png", ... } ]`,
  per swim frame if vanilla fish animate. Deterministic output; validation errors when a referenced vanilla sprite/frame is missing.
* Workflow: (1) export a contact sheet of ~20 vanilla fish incl. Geiger, Noxius, Powerfin, Magmer to `~/jamm-night/silo-v2/ref/` (VPS only, never
  committed); note style rules and which parts are reusable. (2) Mock up each fish as SVG on the VPS using the exported parts as references, then turn the
  mockup into the composition recipe. (3) Review sheet: each new fish next to 3 vanilla fish of the same size class on the game's water colour →
  `~/jamm-night/silo-v2/art-review.png`; iterate until they read as belonging to the game but are clearly new species (not just recolours).
  (4) Commit only recipes + original parts.

## Part C – The mod
* `examples/modkit/silo/` (ModKit) + any GML via the documented hooks. Zero changes to vanilla objects/events; build must report no modified vanilla code.
* Replace the old Silo recipe/builtin (`SiloMod.cs`, `SiloInterior*.cs`, `SiloTravel.cs`, `SiloSprites.cs`, ModMaker `silo` command, site package
  `jamm-silo`) with the new ModKit mod; remove obsolete code and its tests; keep `SiloScene` test tooling only if still useful.
* README in the mod folder written as a tutorial for modders ("how this area was built").

## Part D – Tests
* Synthetic: Area API validation, placement, overlap detection with a fake second mod, follow-anchor, area-restricted spawns never inside solids.
* Real (VPS, opt-in): check/build Silo alone, + each of the 7 mods, + public 5-pack, + all-7. Zero conflicts involving Silo assets (except reported anchor follow with Cave).
* In-game (one run at a time, 600 s): scene that moves the boat/lure into the chamber, screenshots of the opened silo, the chamber with lights, each fish
  swimming; a scripted bite/catch of each new fish if the bench tooling allows (otherwise verify spawn + fishalog entries); save/reload keeps catches.
  Silo alone, + public 5-pack, + all-7.
* Screenshots to `~/jamm-night/silo-v2/screens/`.

## Rules
AGENTS.md. Branch `exp/silo-v2` in `~/jamm-wt/silo-v2`, based on main. Zero warnings, tests green. Commit on branch OK; no push/merge. Never copy TGE/PWB
or vanilla art off the VPS. Report `docs/reports/silo-v2.md` with style rules, art-review path, run table, open points.
