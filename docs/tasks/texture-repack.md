# Task: repack and deduplicate texture pages after merging (VRAM)

Measured 2026-11-19 (UndertaleModLib, uncompressed RGBA):
* vanilla legacy: 34 pages, 536 MB, 94 % of page area used by texture page items
* all-7 build (TGE, Antiquity, PWB, Abyss, Colour, Verdant, Cave): 417 pages (137x 2048², 57x 1024², 45x 512², …), 3400 MB, only 58 % used.
A tester on a laptop with integrated graphics got a black screen with sound, then 2–3 FPS. GameMaker 1.4 keeps texture pages in VRAM, so this
build needs >3 GB. ResourceImporter copies whole source pages per mod.

## Do
1. Final build step (after all merges, runtime patches and sprite relocations; before write): rebuild the atlas from the texture page items that
   are actually referenced (sprites, backgrounds, fonts, any other TPAG users — enumerate them all from UndertaleModLib types).
   * Deduplicate identical item images (hash of pixels + size) across mods and vanilla, share one TPAG region (keep each item's own target/bounding fields).
   * Drop regions nothing references (overridden vanilla art, unused parts of mod pages).
   * Pack into 2048x2048 pages (skyline/maxrects), with padding identical to what GM 1.4 expects (check vanilla padding/border; keep texture
     filtering bleed safe). Items larger than a page keep their own page.
   * Keep vanilla texture groups semantics if 1.4 uses them (check `TextureGroupInfo`/ `UndertaleTextureGroupInfo` presence in this format version).
   * Deterministic output.
2. Make it on by default for multi-mod builds, with a profile/CLI switch to disable (`--no-repack`) for debugging. Single vanilla build untouched.
3. Report in build decisions: `[CGF-TEX-001] Texture pages: 417 -> N, 3400 MB -> M MB (K duplicates merged, U unused regions dropped)`.
4. Optional second step, opt-in setting "Low VRAM": downscale only very large non-UI images (e.g. > 1024 px backgrounds) by 2 with draw scale
   compensation ONLY if it can be done without changing gameplay/collision (masks stay untouched). If not safe, document why and skip.

## Tests
* Synthetic: two mods with the same sprite image → one region; unused region dropped; every sprite/background/font frame renders identical pixels
  before/after (compare decoded item images byte-for-byte); collision masks untouched; page count bounded.
* Real (VPS, opt-in): all-7 build → report numbers; pixel-compare every TPAG item image old vs new build; in-game `~/jamm-night/bin/run.sh <win> tex-all7 dock 600`
  and `deepwater` (one run at a time) must be RECORDED; screenshot comparison of the t60 screenshot vs the non-repacked build.
* Build time impact noted.

## Rules
AGENTS.md. Branch `exp/texture-repack` in `~/jamm-wt/texture-repack`. Zero warnings, tests green. Commit on branch OK, no push/merge. Never copy TGE/PWB off the VPS.
Report `docs/reports/texture-repack.md`.
