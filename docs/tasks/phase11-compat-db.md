# Phase 11 — central compatibility database on varialbe.xyz (implementer: Codex)

Continue on `ui/browse-mods`. Same rules (no commits/pushes/git config, no tools/ changes). Network enabled.

## User request (German, verbatim)
"store eine database auf varialbe.xyz wo eine table ist mit bereits checked mods und ob diese kompatibel sind und
browse mods checker fetched immer von da und wenn nicht in der table für genau die game version dann erst selber
checken (automatisches opt in, möglichkeit zu opt out in settings, keine cookies oder so) UND entscheide: ist das
im moment sinnvoll für game version + mod oder game version mit mods + die mod?"

## Orchestrator decisions (binding)
- Key = (base data.win SHA-256, mod file SHA-256, analyzer version). NOT combinations with installed mods:
  combinations explode (every selection/load order is a new key, cache hit rate ~0) and pairwise/profile conflicts
  are already computed locally and quickly by the existing Check from cached patches. Document this reasoning.
- Secondary lookup index per base: (provider, external file/build id) -> mod SHA-256 + facts, so cards show facts
  BEFORE downloading; after download the client verifies the SHA-256 matches the entry (mismatch -> ignore remote,
  check locally).
- Only the server fills the table (it has the game files). Clients are read-only: no uploads (poisoning risk).
- Privacy: plain HTTPS GET of static JSON; no cookies, no identifiers, no query strings with user data beyond the
  two hashes / file id in the path; state in docs that the server sees the IP like any web request.
  Opt-in by default, opt-out switch in Settings (persisted in config.json, backward compatible), and the client
  never blocks on the server: short timeout, offline -> local check.

## Server side (runs on the VPS; deliverables in this repo)
- SQLite schema (`docs/compat-db.md` + `scripts/compat/schema.sql`): table `checks(base_sha256, mod_sha256,
  analyzer_version, compatibility, reason, adds_json, source_build_utc, mod_name, provider, external_id,
  file_id, version, size_bytes, checked_utc, PRIMARY KEY(base_sha256, mod_sha256, analyzer_version))`.
- A CLI command in Jamm.Cli, e.g. `cgfmm compat-scan --base <data.win> --db <compat.db> --export <dir>`: loads
  the Litterbox catalog, resolves/downloads each releasable mod with the existing services, analyzes with
  ModFactAnalyzer, upserts rows, skips rows already present for the same key, then exports static JSON:
  `<dir>/v1/<base_sha256>/index.json` (provider+file id -> mod sha + facts) and
  `<dir>/v1/<base_sha256>/<mod_sha256>.json`, written atomically (temp + rename). Deterministic output.
- `scripts/compat/run-scan.sh` for cron (lock file, log rotation-friendly output, exit codes).
## Client side
- Remote facts provider used by Browse before local analysis: GET
  `https://varialbe.xyz/jamm/compat/v1/<base>/index.json` (cached locally with ETag/Last-Modified, small size
  limit, existing ModHttp safety rules) and per-mod JSON when needed; validate schema + analyzer version;
  UI shows where the verdict came from (tooltip: "checked by JAMM server on <date>" vs "checked on this PC").
- Settings toggle "Use shared compatibility results (no account, no cookies)", default on.
## Tests
Fake HTTP handler: hit, miss -> local check, offline, malformed JSON, analyzer version mismatch, SHA mismatch
after download, opt-out sends no request. CLI scan/export with fake catalog + synthetic archives, idempotent
upsert, deterministic export.
Report: docs/reports/phase11.md (include exact VPS deployment steps for the orchestrator).
