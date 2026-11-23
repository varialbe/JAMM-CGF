# ModKit asset remapping — Round 2 report

Untyped integer literals assigned to variables or arrays named for sprites, sounds, objects, or backgrounds are now resolved through the source mod's own asset table before the merge compiler runs. The merge uses the resulting asset-name symbol to link the literal to the merged index. Post-compile semantic verification resolves those asset-named assignments against the merged table as well. Ambiguous candidate values produce a warning and remain unchanged.

Added `cgfmm audit --asset-refs <archive.win> --base <original.win>` to list integer constants matching added or shifted asset indices, including untyped literals. The audit is intentionally a candidate report: numeric matches outside asset-like contexts can be unrelated constants and require review.

The current remapper uses the conservative asset-name/context heuristic for archive code. It does not yet embed an instruction-ordinal literal sidecar when ModKit source packages are compiled; untyped references outside recognized asset-named assignments and typed call arguments remain a follow-up.

Verification on the copied `personal` store profile (9 mods): `harbor_sprite` entries compile to merged sprite indices 521, 522, and 523, resolving to the harbor-town sprites rather than silo sprites. The build passed archive and semantic verification. The Round 2 regression test checks two synthetic mods with different sprite names at the same source index. `dotnet test -m:1 -c Release` passed (669 Core tests, 130 GUI tests; 19 skipped real-file tests total).

Visual verification: recorded the harbor scene from the rebuilt archive and inspected an extracted frame; harbor buildings render with their harbor sprites. Build output and captures were kept outside the repository. The source archive and personal store were read-only; the profile was copied to `/tmp/jamm-store-r2` before use.
