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
