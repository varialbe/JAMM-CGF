# Base-mod mode

## Profile and build behavior

An overhaul can declare `"role": "base"` in a ModKit manifest or archive
metadata. An addon declares `"base"` with the overhaul's package or library ID
and can pin its exact version and SHA-256. CLI and desktop users can override
either role in a profile. Changing more than 25% of existing vanilla code
produces a suggestion for unclassified imports.

JAMM builds the base and any vanilla-targeting mods against vanilla first.
It diffs the addons against the base mod's original archive and merges those
changes onto the first pass. The build retains the merged base's asset order,
room IDs and catalog assignments. Runtime injection and texture repacking run
on the final result. Each addon cache is keyed by its source hash and its
ancestor hash, so a different base archive produces a fresh patch.

Checks compare changes within each ancestor group. `CGF-BASE-001` warns about
potential overlaps in mixed profiles. Missing or disabled required bases and
multiple enabled bases fail validation. `CGF-BASE-002` reports a declared version
or hash mismatch. `--strict` makes that mismatch an error for profile checks
and builds. Addon conflict choices use the `addons/` prefix, so a conflict on
the same asset can have a separate choice for each ancestor.

## Synthetic verification

The legacy VM fixtures exercise an overhaul that changes scripts, objects and a
room and adds assets. One addon edits the overhaul's new object and another
adds a fish. The tests compare the layered result with sequential edits by
hand, confirm that the patches contain the intended deltas, and check that
reordering objects in the source overhaul preserves the original game's asset
order in the output. They also cover an addon hunk choice, mixed vanilla mods,
the missing and disabled base cases, strict validation, role persistence,
cache invalidation and the guarded install and restore path.

The headless desktop test checks the role labels, indent and row order on
screen, and exercises both directions of a move across the base/addon boundary.
The existing mouse-drag test covers reordering within a group. The settings
test waits for its opening archive validation before simulating Save.

`dotnet build -c Release -m:1` passed with zero warnings and errors.
`dotnet test -c Release --no-build -m:1` passed 451 Core and 128 GUI tests.
The standard suite skipped 16 opt-in archive tests.

## Cave Expansion smoke test

The configured vanilla archive was the August 16, 2023 Steam depot. I imported
the original Cave Expansion archive as the base and generated an addon from a
read-only load of that archive. The addon adds one synthetic script returning
23. The checker reported zero conflicts. The addon patch added one code entry,
one script entry and two string entries. It also carried three existing sprite
warnings with empty field changes where the fingerprint could not prove
equality.

The profile built and verified a 56,009,798-byte archive. Verification covered
1,949 code entries and 269 catalog fish with zero inconsistent slots. I reloaded
the result and confirmed the synthetic script body. SHA-256 checks confirmed
that both input archives still matched their starting hashes.

The dock test ran under the shared game lock after the previous session exited.
`~/jamm-night/bin/run.sh <merged.win> base dock 600` returned **RECORDED** with
134 samples. The CSV and runner logs remain in `~/jamm-night/runs/base-dock/`.
The temporary game copy, build archives and isolated library were removed.

The [user guide](../user-guide.md#overhauls-and-their-addons), [CLI reference](../cli.md)
and [ModKit manifest reference](../modding/modkit-reference.md#overhauls-and-addons)
cover setup and author metadata.
