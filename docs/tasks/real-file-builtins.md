# Real-file regression tests that would have caught today's bugs

Today three bugs shipped because real-file tests are skipped by default and nothing exercised the actual base archive:
- console builtin could not be generated from a fresh base (LegacyCodeHooks shared a string ResourceById → UndertaleIO.Write
  "same key already added") — fixed 33a7499;
- builtin cache key was a manual constant, stale console served — fixed e604fff;
- console moved its state to global, published ModKit mods (harbor-town, submarine) read obj_jamm_console.jamm_console_open
  instance var → "not set before reading" — fixed f4dc1a9 (instance mirror).
- ModKit untyped asset literal remap (harbor sprites) — fixed eb33ba5.

Task:
1. Add a real-file test tier (opt-in env var already exists? reuse the existing convention, check tests for
   `JAMM_REAL_` / skipped real-file tests) that uses the local base
   ~/.var/app/com.valvesoftware.Steam/data/JAMM-CGF/CGF-legacy-clean/data.win (read-only!) and:
   a) generates every builtin (perf, smooth, hd-lite, console) into a temp store and loads/verifies the written file;
   b) builds a profile of builtins + the first-party ModKit examples (examples/modkit/harbor-town, submarine, others that
      build) and runs `audit --asset-refs` style checks for known-bad patterns;
   c) static bytecode check: every variable read as `obj_jamm_console.<name>` (or any `<builtin object>.<var>`) by a ModKit
      example must be assigned on that object somewhere (instance scope) — generalize as a "cross-mod instance variable
      contract" check;
   d) VARI sanity: no name has two global-scope entries with different ids after composition.
2. A script `tools/run-real-tests.sh` that sets the env and runs only that tier; document it in CONTRIBUTING or docs/.
3. Run it, make sure it passes on current main, and prove it fails when reverting each of the fixes above
   (temporarily, in a scratch commit you then drop) — report which test catches which bug.
Never touch CGF-Personal/compatdata. No game runs needed. dotnet -m:1. Commit on test/real-file-builtins; do not push.
