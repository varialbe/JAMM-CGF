# Task: Phase 2 — conflict detection and three-way merge

Read `AGENTS.md`, `docs/patch-format.md` and `docs/legacy-version.md` first. Work happens on the
`experimental` branch. Inputs are Phase 1 patch files (`*.cgfmm-patch.json`); compute missing patches
with the Phase 1 diff code.

## Step 0 — fix Phase 1 review findings first (independent review of commit 84f35df)
Verify each against the code before fixing; skip with a note if it is not real. Bump the patch
`formatVersion` if the format changes (old cached patches must then be recomputed, not misread).
1. Patch filtering relies on Phase 0 fingerprints that miss fields for sounds, fonts, objects, backgrounds →
   fingerprint these from the same field snapshot used for the patch; never emit no-op changes
   (e.g. scripts with base == mod and no fields).
2. `functions`: changed definitions are ignored; added strings/variables/functions have empty `fields`;
   `FieldDiff.Compare(null, x)` emits one `""` path instead of leaf paths → fix, `--show` must work for additions.
3. `AssetFields.Sprite` dereferences `frame.Texture` without a null check; texture page keys sort as strings
   ("10" < "2") → null-safe, numeric ordering.
4. Cache reuse with a different `--name` keeps the old name → compare against the effective name.
5. **Collision events** (event type 4): the subtype is the other object's *index* → key and store by the
   other object's *name*; handle duplicate (type, subtype) without throwing.
6. **Arrays diffed by position** (room layers, timeline moments, font glyphs, instances) → key them semantically
   (layer name, moment step, glyph character, instance key) so appends by two mods don't collide by index.
7. **Room tiles** (GM 1.4 stores tiles in `room.Tiles`) are only stored as count + hash → store keyed tiles
   (background, x, y, source rect, depth) like instances.
8. Texture page items: record geometry (source/target rects, bounding size) for sprites/fonts/backgrounds;
   new texture pages are content-addressed additions, not "page N"; sounds store the audio group *name*;
   keep the original asset order of additions (needed to insert in the right order in Phase 3).
9. `LineDiff` allocates a full array per edit step (O((N+M)·D) memory) → linear-space or bounded variant.
   Never line-diff/merge GML against disassembly fallback text: report it as a separate, clear condition.
10. `--verbose` must be accepted by `diff`.

## Command
```
cgfmm check <mod-or-patch> <mod-or-patch> [...] --base <data.win> [--json <file>] [--details <asset>] [--verbose]
```
- Arguments may be mod `.win` files (patch computed or reused from cache next to the output dir,
  default `./patches/`) or existing patch files. All patches must have the same `base.sha256`;
  otherwise explain which mod targets which base and stop (exit 1).
- The order of arguments is the load order (later mod wins in Phase 3). Show it in the output.
- Output: a readable terminal table grouped by severity:
  `CONFLICT` (needs a decision), `AUTO` (resolvable automatically), `INFO` (overlap but harmless,
  e.g. identical change in both mods), plus a short per-mod summary. `--json` writes everything.
- `--details <asset>` prints both versions side by side (code: merged result with conflict markers;
  fields: base / mod A / mod B values).
- Exit codes: 0 no conflicts, 3 conflicts found (still a successful run), 1 error.

## Rules
Conflict = two mods change the same asset, or add an asset with the same name.
| Kind | Merge rule |
|---|---|
| code (changed by ≥2 mods) | line-based **three-way merge** of decompiled GML with the base text as ancestor, applied in load order (merge A into base, then B into the result, …). Non-overlapping hunks → `AUTO`; overlapping hunks with different content → `CONFLICT` with both versions; identical hunks → `INFO`. Also handle: same code added under the same name by two mods (identical → INFO, different → two-way conflict). |
| objects | field-level: different fields → AUTO; same field, different values → CONFLICT; events: per (type, subtype) — both add/change the same event → treated through the event's code entry. |
| rooms | field-level as objects; instances: additions by both mods are a union (AUTO) unless both put the same object at the same position (INFO, duplicate) ; removal of an instance another mod changed → CONFLICT. |
| sprites, texture pages, sounds, fonts, backgrounds, paths, timelines | changed by both with different content hashes → CONFLICT (binary; later mod wins by default). Same name added by both with different content → CONFLICT. |
| strings, variables, functions | additions are a union (no conflict). |

### Indirect problems (report as CONFLICT or WARNING with explanation)
1. **Removed but used:** mod A removes an asset that mod B references. References to extract:
   object parent / sprite / mask, event code entries, room instance objects and creation code,
   script → code, sprite references in objects, and identifiers in mod B's added/changed GML that equal
   an asset name (sprite, object, sound, script, room) — tokenise GML, skip strings/comments.
2. **Shared lists / catalogs:** both mods change the same code entry by inserting lines at the same
   position (typical for "add new fish to the fish list"). Detect and report as `CONFLICT (list)` with a
   suggested automatic resolution "keep both insertions, A then B" — the resolution must be selectable in Phase 3.
3. **Index collisions:** in added/changed lines of different mods, assignments to the same array/ds slot with
   a numeric literal index (`name[42] = …`, `ds_list_set(name, 42, …)`, `ds_map_add(name, "key", …)`) using
   the same container name and same index/key → `CONFLICT (index)`, even when the lines are in different
   code entries. Report container, index, mods, code entries.
4. **Same-name additions** of objects/sprites/etc. with different content (already above) — make them prominent;
   two mods adding the same new fish object name is a real problem.

## Design
- New `Jamm.Core/Merging/`: `ThreeWayMerge` (line-based, built on the Phase 1 line diff; returns merged text +
  hunks with status), `ConflictAnalyzer` (patches + load order → `ConflictReport` records), `GmlReferences`
  (tokeniser + reference/index extraction). Must work from patch files alone (no UndertaleModLib needed),
  except for the reference index of base assets, which may be stored in the patch or loaded from the base.
- The merge result per code entry must be reusable by Phase 3 (store merged text + per-hunk resolution).
- Deterministic output and stable ordering.

## Tests
- Unit tests for ThreeWayMerge (non-overlapping, overlapping, identical, insertion at same point, deletions,
  CRLF/LF), index-collision detection, reference extraction (strings/comments ignored), object/room field merges.
- Real-file test (skipped without env vars): `check` on The Abyss + Cave Expansion + Colour Expansion.

## Docs
- README: `check` with example output and exit codes; roadmap Phase 2 done.
- `docs/conflicts.md`: rules above, explained for non-developers, with what each status means and what Phase 3 will do.

## Verify before finishing
- `dotnet build -c Release` zero warnings, `dotnet test` green with and without env vars.
- Run `check` on all three mods (all orders that matter: at least Abyss,Cave,Colour and Colour,Cave,Abyss), and on each pair.
  Put the summary (counts per status, the top conflicts by name) and timings in your final message.
  Spot-check 3 reported conflicts with `--details` and say whether they look like real conflicts.
- Do NOT commit. No game content or personal paths in tracked files.
