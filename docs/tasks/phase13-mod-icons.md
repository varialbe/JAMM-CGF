# Phase 13 — minimalist mod icons (implementer: Gemini / agy)

Work only in `src/Jamm.Gui/Assets/ModIcons/` (create it) and this task's report. Do not touch any other file.
Do not commit, push or change git config. You do not need any shell commands: read the images, write SVG files.

## User request (German, verbatim)
"die icons von den mods sollen wirklich icons sein, sag gemini / agy hier er soll die icons simpel generieren aber
wichtig die müssen wirklich minimalistisch sein und ähnlich zu den wirklichen mod game screenshots die supplied sind."

## Input
`/home/varialbe/.local/share/jamm-tools/mod-screens/<litterbox-id>/` contains `name.txt` (mod name) and up to five
images (icon, banner, screenshots) published on litterbox.fyi by the mod authors. Look at them to capture each
mod's dominant colours and one recognisable motif (e.g. a fish silhouette, a cave arch, coral, a boat, a glacier).
These images are the authors' property: do NOT copy, trace or embed them; draw your own simple shapes.

## Output
- One SVG per catalog mod: `litterbox-<id>.svg` (all 24 ids in the input folder).
- Local mods without a catalog entry: `local-abyss.svg` (The Abyss: deep dark-blue water, a glowing fish),
  `local-cave.svg` (Cave Expansion: dark rock arch over water), `local-smooth.svg` (Smooth 60/120 FPS: motion
  lines), `local-hd.svg` (HD textures: a crisp diamond/pixel motif), `local-perf.svg` (Perf: a speed gauge),
  and `default.svg` (neutral fish on a wave) for anything else.
- Style, strictly: square 64x64 viewBox, flat fills, 2-4 colours taken from the mod's screenshots, one central motif,
  no text, no gradients unless essential, no filters, no external references, no raster images, readable at 24 px,
  rounded-square background (rx 14) so all icons look like one family. Keep each file small (< 3 KB).
- `manifest.json`: `{ "<file>.svg": { "mod": "<name>", "motif": "<what you drew>", "colours": ["#..", ...] } }`.
- Report `docs/reports/phase13-icons.md` listing every icon with its motif (one line each).
