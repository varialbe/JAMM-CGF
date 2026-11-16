# Phase 4 verification

Implemented and verified on 16 November 2026 on `experimental`, using .NET 10
and the pinned, unmodified UndertaleModTool submodule. No commit, push, release,
GitHub setting change or git configuration change was made. No game was launched.

## Implemented behavior

- Configurable managed storage (`--data-dir`, `CGFMM_DATA_DIR`, XDG default),
  persistent configuration, safe mod/profile names and managed-path link refusal.
- Read-only `.win` / nested zip imports, complete-archive hashing, immediate
  semantic patch generation, exact-byte duplicate detection, compatibility/cache
  listing and removal of unused mods. Every zip entry is checked for unsafe paths;
  executable files are ignored. Ambiguous/missing `data.win` entries fail import.
- Ordered profiles, active selection, text/JSON views, deletion and validated
  mod/`keep-both` resolution choices, using the existing Phase 3 build plan.
- `apply`: verified build, printed decisions, explicit installation confirmation
  or `--yes`, permanent verified originals in both managed storage and the game
  folder, and reuse only when profile, sources, patches, game and marker match.
- Configured-directory `restore`, retaining backups and invalidating installed
  state; traditional `restore --game` remains available.
- Wine/Proton command construction, optional Wine prefix, configurable Proton
  compatibility/client directories, runner/executable checks, shell-free argument
  passing and process start without waiting. `play` checks the runner before any
  profile build/install, ensures the profile is installed, then prints its launch.
- README command reference, six-step Steam-to-modded-game guide, FAQ and
  [managed data layout](../data-layout.md).

## Build and tests

`dotnet build -c Release` completed with **zero warnings and zero errors**.

With `JAMM_ORIGINAL` and `JAMM_MODS_DIR` unset, the suite passed **131 tests**,
with **5 opt-in real-file tests skipped**. With both exported, the full suite
passed **136 tests, zero skipped** (4 min 38 s). After the final zip-identity
and warning-output refinements, a fresh zero-warning Release build and focused
management run passed **12 tests** (one opt-in real-file test intentionally skipped).

New temporary-directory coverage includes config persistence/validation and storage
precedence, `.win` and nested zip imports, ignored zip executables, traversal and
ambiguous/missing archive rejection, cleanup after failed imports, library/cache
compatibility, damaged payload detection, profile order/resolutions/dependencies,
managed symlinks, clean CLI JSON, confirmation cancellation/acceptance, installed
reuse, restore, Wine/Proton arguments/environment, and actionable missing-runner
errors. Runner tests construct commands and validate prerequisites; they do not
start Wine, Proton or the game.

The new opt-in management test imports Abyss, builds and installs a profile into
a temporary copy of the complete legacy game folder, checks both original backups,
recognizes the installed profile, notices a different selected profile, refuses a
tampered installed archive, and restores. It verifies original/mod source hashes
on exit. Existing real-file tests also cover each single mod, both three-mod orders,
reference/GML verification, comparisons, patches and conflict analysis.

## Quick-start walkthrough (shortened transcript)

The complete legacy 16 August folder was copied to a fresh temporary `game/` folder.
All management commands used a separate temporary `--data-dir`. The configured
base remained the untouched source archive outside the temporary game folder.
`play` was excluded as required. Mod names below are explicit import display names.

```text
$ cgfmm config set base "$JAMM_ORIGINAL"
Set base.
$ cgfmm config set gameDir "$TEMP_GAME"
Set gameDir.
$ cgfmm config set wine.mode wine
Set wine.mode.
$ cgfmm config set wine.binary wine
Set wine.binary.
$ cgfmm config show --json
{ ... base, gameDir, Wine/Proton/Steam settings ... }

$ cgfmm add "$JAMM_MODS_DIR/the-abyss-beta.win" --name Abyss
Added Abyss (...). Patch cached.                         [35.271 s]
$ cgfmm add "$JAMM_MODS_DIR/cave-expansion.win" --name Cave
Added Cave (...). Patch cached.                          [53.657 s]
$ cgfmm add "$JAMM_MODS_DIR/colour-expansion.win" --name Colour
Added Colour (...). Patch cached.                        [29.333 s]
WARNING: source timestamp 1692057761 differs from base 1692207312.
Expected manifest 1512054085756769140; public 2811289735487427888 is near-identical.

$ cgfmm list --json
[ ... three mods, baseHashMatches=true, versionMatches=true, patchCached=true;
      Colour timestampMatches=false ... ]
$ cgfmm profile create combined --mods Abyss,Cave,Colour
Created profile 'combined'.
$ cgfmm profile use combined
Active profile: combined.
$ cgfmm profile list
* combined: Abyss, Cave, Colour
$ cgfmm profile show combined --json
{ "name": "combined", "mods": [ ... ordered IDs ... ], "resolutions": {} }
$ cgfmm profile resolve combined code/<changed-code-entry> Abyss
Saved resolution for 'code/<changed-code-entry>'.

$ cgfmm apply
Decision: ... [explicit choice plus load-order/default decisions]
Built and verified profile 'combined' (105,066,322 bytes).
Install this build? [y/N] n
Installation cancelled.                                 [exit 1; 47.295 s]
# Game archive hash unchanged; no game-folder backup or installation marker created.

$ cgfmm apply --yes
Built and verified profile 'combined' (105,066,322 bytes).
Installed profile 'combined'. Verified original backups retained.
                                                        [exit 0; 64.659 s]
$ cgfmm apply --yes
Selected profile is already installed; archive and backup hashes verified.
                                                        [exit 0; 2.636 s]
$ cgfmm verify "$TEMP_GAME/data.win"
Verified 'data.win': 2133 code entries decompile and compile; references valid.
                                                        [exit 0; 9.894 s]
$ cgfmm restore
Restored data.win. Original backups retained.            [exit 0; 0.667 s]

$ cgfmm profile delete combined
Deleted profile 'combined'.
$ cgfmm remove Abyss
$ cgfmm remove Cave
$ cgfmm remove Colour
$ cgfmm list --json
[]
```

All 24 CLI commands returned their expected status. The transcript omits GML,
large decision/warning lists and local source paths. Timings include other tests
running on the machine and are not benchmarks. Six inherited out-of-bounds texture
warnings remained in the combined archive; those require visual desktop checks.

The installed archive SHA-256 was
`8412d075f4f2199adec3736d9eee6ce643932053a622ebdf842d0779a2605e7a`.
The copied game's original, both backups and restored archive all matched base SHA-256
`dbb2e5c4b788358d0c6465d3005b2e9d46c4c540e423b064d7a5a3629286d2c1`.
SHA-256 checks before/after the walkthrough matched for **all 21 source files** in
both supplied legacy game folders (15/16 August) and the three mod archives.

The temporary walkthrough copy, managed builds/backups and detailed local artifacts
were removed after verification. No game data is retained in the walkthrough directory.

## What works and what remains

The mod-management and installation/restore workflow passes unit, CLI and real-file
verification. The six-step workflow through installation and restore works on this
machine. Wine/Proton construction, argument boundaries and missing-runner failures
are covered without executing a runner.

Actual Wine/Proton startup, rendering and gameplay remain **unverified on this
headless machine**. A successful asynchronous start cannot prove the runner or game
stays alive. Desktop testing with the matching legacy executable is still required,
especially for catalog/index overlaps and inherited texture warnings. Existing
Phase 3 limits remain: YYC, GMS2 room layers and nested child-code reconstruction
are unsupported; runtime compatibility and dynamic resource lookups are not fully
provable by static verification. External audio and the executable must already
exist in the complete legacy game folder.

Changing the configured base does not silently regenerate library patches. Restore
the original base setting, or remove dependent profiles/mods and re-add against the
new base. Managed backups are retained permanently. Temporary builds are removed after
installation or a CLI cancellation/failure; interrupted-process leftovers can be
removed manually.

## Independent review follow-up

1. Confirmed: management option values and `--data-dir` values can start with `--`.
   Global options no longer consume management option values. Management commands
   also accept `--` before positional values beginning with a hyphen.
2. Partly confirmed: commas and control characters are rejected before import.
   Skipped the spaces claim: profiles store mod IDs, so names containing spaces work.
3. Confirmed: text `config show` now prints the same dotted keys as `config set`;
   `--json` retains its structured output.
4. Confirmed: explicit runner paths check both platform directory separators;
   Windows lookup also considers the current directory and PATHEXT. Launch uses the
   resolved runner path. Skipped current-directory-first lookup on Unix: bare runner
   names correctly use PATH, while explicit relative paths use the current directory.
5. Confirmed: cancelled and successful installs no longer accumulate full archives.
   The CLI discards its prepared build in a finally block, including failed installs;
   Core exposes guarded `Discard` for callers abandoning prepared builds.
6. Partly confirmed: stale markers, mismatched backups and out-of-sync installed
   hashes return false; `Prepare` still refuses unsafe installation state.
   Skipped the ordinary modified-archive claim: its hash mismatch already returns false.
7. Confirmed: Proton now falls back to the Steam environment variable and standard
   existing Linux Steam folders. Explicit configuration takes precedence.
   Skipped the `profile list --json` claim: it already returns a JSON array and is an
   additive command feature, not a spec violation.
8. Confirmed: removed the retained walkthrough files and ephemeral artifact paths
   from this report.

Regression coverage includes each confirmed case, cleanup after cancellation and
installation, and continued refusal to overwrite damaged installation state.


Review validation on 16 November 2026:

- `dotnet build -c Release`: zero warnings, zero errors.
- Focused management run: 30 passed, including the opt-in real-file workflow.
- `dotnet test --no-restore --logger 'console;verbosity=minimal'` with both real-file
  environment variables set: **153 passed, zero failed, zero skipped** (5 min 50 s).
- Final diff checks passed; the UndertaleModTool submodule is unchanged.
- No commit was made. Runner/game startup was not attempted.
