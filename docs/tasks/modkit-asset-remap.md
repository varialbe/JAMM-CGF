# Task: ModKit mods' compiled asset indices must be remapped in combined packs

## Bug (verified 2026-11-23 by parsing SPRT of the combined build)
ModKit mods compile sprite (and possibly object/sound/background) NAMES to fixed numeric INDICES against the base archive. When
several mods that each ADD assets are combined, the merged archive assigns different indices; but the ModKit-compiled GML still holds
the old numbers. Example: profile `personal` = perf, hd-lite, console, smooth, jamm-silo 2.3.0, weather-pack 1.0.1, endless-ocean,
submarine, harbor-town. Harbor-town's and submarine's code references sprite indices 488+ which in the merged pack are the silo sprites
`jmk_jamm_silo_recipe_veyl/radik/ferrex/garrusk`; the real harbor sprites ended up at 521–524, submarine at 519–520. In game, the dock
showed giant silo creatures instead of harbor/submarine graphics. No Code Error, so existing tests missed it.

Reproduction inputs (local, not in repo): `~/jamm-personal/inputs/{silo,weather-pack,endless-ocean,submarine,harbor-town}.win`,
base `~/jamm-personal/store` (CGFMM_DATA_DIR) base data.win sha dbb2e5c4…. Use a COPY of the store (e.g. cp -r to /tmp) — never
write into ~/jamm-personal/store and never touch anything under CGF-Personal or compatdata cgf-personal.

## Required
1. Find where numeric asset literals in mod code are (or are not) resolved: `src/Jamm.Core/Building/SemanticCode.cs` handles some
   index→name→merged-index remapping; ModKit-built (`src/Jamm.Core/ModKit/ModKitBuilder.cs`) and archive (.win) mods evidently escape it
   for some instruction forms (pushi/push.i constants, sprite_index assignments, draw_sprite args, instance_create object args,
   audio_play_sound args, room instances, object sprite/parent fields).
2. Fix: every asset reference of a mod is remapped mod-index → asset name (from that mod's own archive/patch) → merged index. Prefer
   making ModKit emit name-based references in the patch (resolved at build time) AND remap in the merger, so old packages also work.
3. Verification tool/test: after building, for each mod, check that every resolvable sprite/object/sound reference in its code points
   to an asset with the same NAME as in the mod's own archive. Add as `cgfmm audit --asset-refs <out.win>` or a test helper, plus a
   unit test with two synthetic ModKit mods that both add sprites. Run it against the personal 9-mod profile build (store copy) and
   report results: harbor/submarine references must resolve to their own sprites.
4. `dotnet test` passes. Commit on branch, do not push. Write a short report docs/tasks/modkit-asset-remap-report.md.

## Round 2 (orchestrator, 2026-11-23 04:00) — BUG CONFIRMED IN GAME, previous audit missed it
Recorded harbor scene of the 9-mod `personal` build from main: giant silo creature sprites drawn where harbor buildings belong; submarine
renders fine. Object sprite fields are correct. Root cause found in harbor-town's code (store patch 0c79a967….sanitized.cgfmm-patch.json):
`harbor_sprite[0] = 488; harbor_sprite[1] = 489; harbor_sprite[2] = 490;` then `draw_sprite_ext(harbor_sprite[i], stage, …)`.
ModKit compiled sprite NAMES in an UNTYPED context (array assignment) to plain integer constants (pushi). The remapper only handles typed
operands, so these stay 488–490, which in the merged pack are jmk_jamm_silo_recipe_* sprites. Your 1/1 audit only looked at typed refs.
Required:
1. ModKitBuilder: while compiling a ModKit source package, record every asset-name token that compiled to a numeric constant (code entry,
   instruction index/offset or occurrence ordinal, asset type, asset name) — sidecar in the patch (e.g. `assetLiterals`). Merger rewrites
   those constants to the merged index of that name. Works for typed and untyped contexts.
2. For .win/archive mods without source: conservative heuristic + WARNING decision: integer constants in mod-ADDED/changed code that equal
   the index of an asset ADDED by that same mod (index ≥ base count for that type) and appear in an asset-ish context (assigned to
   variables/arrays whose name contains sprite/spr/sound/snd/obj/bg, or passed to draw_*/sprite_*/audio_*/instance_* args) → remap; ambiguous
   ones → warning listing code entry + value. Also: when the ModKit .win was produced by JAMM ModKit, embed the sidecar in the archive
   (e.g. a JAMM metadata string/extension) so distributed .win builds carry it.
3. Audit tool must check ALL integer constants that match mod-added asset indices, not only typed refs. Add `cgfmm audit --asset-refs`.
4. Verify on the 9-mod personal profile (store COPY): harbor_sprite constants resolve to jmk_harbor_town_* sprites; record a harbor scene
   with `REC=1 REC_SEC=55 ~/jamm-bench/run.sh <win> <label> harbor 60` (MM=$HOME/jamm-personal/tools/mm/jamm-modmaker.dll) and extract a
   frame with ffmpeg to confirm buildings render (look at the PNG). Commit on branch in THIS worktree (not /tmp), no push.
