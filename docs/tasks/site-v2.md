# Task: CGF mods site v2 – real technical depth, current Silo v3 media, better site overall

Branch `exp/site-v2`. Site source: `site/cgf-mods/` (`content/*.json` → `make-pages.py` → `mods/*.html`, `index.html`,
`style.css`, `images/`, `catalog.json`, packages via `make-packages.py`). Live at https://varialbe.xyz/cgf-mods/.
Read AGENTS.md, the existing generator and all `docs/modding/*.md` and the matching `docs/reports/*.md` first.
The user says the site is "not good enough yet": it must explain HOW things work, technically, with real detail.

## 1. Silo: current v3 media
Silo v3 (2.3.0) changed the opening: the rock no longer splits, only the hatch door slides, depth effect (foreground rock
drawn in front of the lure). The site images in `images/jamm-silo/` are from v2. Replace them with v3 captures from the
SOLO build (never use any `*all7*` run or anything showing TGE/PWB content):
`ssh varialbe@varialbe.xyz` → `~/jamm-night/runs/silo-v3-lit-solo-silo/save/` (jamm_silo_frame_100..1800.png = opening
sequence, closed, chamber, glow, boat, compare_*), and `silo-v3-lit-solo-reload-silo/save/reload-*` if present.
Make: an animated opening (webp or gif, ≤ 3 MB, from the frame sequence), a closed/open before-after pair, chamber, depth
effect close-up (rim-occluded / rim-entry style frames from a SOLO run only), thumbnails, alt texts. Keep total image weight sane.
Explain the v3 technique on the page: collision from `spr_silomask` frame 0 while closed, `shaft-mask` at full travel,
door-only animation, depth ordering of the foreground rock vs lure, open flag in `[JAMM_Areas]` of the native save slot
(per slot, survives reload). Source: docs/reports/silo-v3.md, examples/modkit/silo, the generator code.

## 2. Technical deep dives for every mod (from code + reports, never invented)
Each mod page gets a "How it works" section with real mechanics, the involved GameMaker objects/scripts/events, data flow,
save format, compatibility/merge behaviour (decisions the cgfmm builder makes) and known limits. Use inline SVG diagrams
(both light/dark readable) where a picture helps. At minimum:
* **Endless Ocean**: how the playable area is extended – room/coordinate handling, chunk streaming (`jamm_chunk_stream`
  and its script argument typing fix), worldgen seed → chunk content, what is spawned/despawned per chunk, how far x goes,
  camera/boat/sonar handling, fish population and performance, save data. Source: docs/reports/worldgen-v0.md, worldgen
  docs/tasks/specs, examples/modkit/endless-ocean, the builder code.
* **Weather Pack / Weather API**: composed controller from all packages' `weathers[]`, deterministic forecast, persistence
  next to the save slot, parking/restoring vanilla fish, weather fish via Fish/Catalog runtimes, Fog + Lightbulb lure, audio.
* **Silo** (see 1), **Perf** (fish scheduling rules, FishGrid, what is excluded, instance guards incl. the general
  object_index guard CGF-INSTANCE-00x), **Smooth** (tick vs render frames, interpolation, F7), **HD-Lite**, **Console**
  (commands, money > 999M compact display K/M/B/T), example fish/rod (ModKit API walkthrough with a manifest snippet).
* A new page **"How JAMM builds a modded game"**: base data.win → per-mod packages → merge order → conflict decisions →
  guards/anchors → verify, with a diagram; and **"Compatibility"** (which public packs are tested together).

## 3. General site quality
Clear navigation (header nav, breadcrumbs, back links), per-mod version + changelog, install steps, download size/sha256,
requirements, FAQ, mobile layout (16 px gutters, no horizontal scroll), fast (lazy images, thumbnails), consistent design,
dark/light. Keep it static (no build step on the server besides what make-pages.py generates). Do not publish any .win or
game assets beyond our own screenshots. No TGE/PWB names in media; mention Vel only as already credited.
Performance numbers: leave a clearly marked section that pulls from `site/cgf-mods/content/benchmarks.json` if present
(do not invent numbers; the orchestrator will add measured data).

## Rules
Update the site tests (there is a site package test) and keep the full suite green, zero warnings. Render-check pages with
headless Chromium (desktop + 390 px mobile) and put screenshots in `~/jamm-bench/site-shots/`. Commit on the branch.
Do NOT deploy, do NOT push, do NOT merge – the orchestrator reviews facts and deploys.
