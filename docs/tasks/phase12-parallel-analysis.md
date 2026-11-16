# Phase 12 — faster compatibility check and merge (implementer: Codex)

Branch `perf/parallel-analysis` (forked from `ui/browse-mods`; another Codex works on Browse UI/compat DB in
parallel — stay in Jamm.Core analysis/build internals and their tests; avoid src/Jamm.Gui and Browsing UI files).
No commits/pushes/git config, no submodule edits.

## User question (German, verbatim)
"kann man den check ob eine mod compatible bzw den merge schneller machen zb mehr cores nutzen oder gpu oder
whatever?"

## Orchestrator assessment
- GPU: no — the work is parsing, decompiling ~1,800 GML entries, diffing, recompiling (branchy CPU code).
- Measured today: per-mod analysis 10-19 s (ModFactAnalyzer: load base + mod, decompile both, patch, build,
  verify), merged apply ~1 min for 6 mods, all effectively single-threaded.
## Tasks
1. Profile first (Stopwatch phases or dotnet-counters/trace if available): load, fingerprint, decompile, diff,
   build/compile, verify, write. Report a table before/after on the real files
   (`/run/media/varialbe/Festplatte 2/Games/CatGoesFishing-legacy/2023-08-16_manifest-9039707068725406600/data.win`
   and `~/Cat-Goes-Fishing-Mods/*.win`, read-only).
2. Cache per-base work keyed by base SHA-256 + tool version under the manager data dir (decompiled base GML,
   base fingerprints/snapshots) so it is computed once, with safe invalidation and size limits.
3. Parallelize independent per-entry work with a bounded degree (Environment.ProcessorCount, configurable,
   default leaves one core free): decompilation (separate decompile contexts per thread if Underanalyzer is
   not thread-safe — verify), content fingerprints/hashes, per-entry diffs. Keep CompileGroup usage
   single-threaded unless proven safe.
4. Results must be byte-identical to the sequential path: add tests that compare parallel vs sequential output
   (patch JSON, merged archive hash, facts) on synthetic archives, and run the real-file comparison yourself.
5. Progress reporting stays monotonic and thread-safe (the UI shows "Decompiling n/m").
Report: docs/reports/phase12.md with the before/after table.
