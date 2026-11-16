# Shared compatibility database

JAMM uses `(base data.win SHA-256, mod archive SHA-256, analyzer version)` as the primary key.
The base hash identifies an exact game version. Installed-mod combinations are deliberately excluded:
every selection and load order would create another key, exploding the database and giving almost no
cache hits. Pairwise/profile conflicts still run locally from cached patches using Check.
A successful single-mod result does not promise that an entire profile is conflict free.

Only the operator's scanner writes the SQLite database. Clients never submit archives, results,
identifiers or telemetry. The operator must legally supply the read-only game base. Game archives,
patches, extracted assets and private scanner state must never be served publicly.

## Storage and wire format

The canonical schema is [schema.sql](../scripts/compat/schema.sql). `checks` contains:
`base_sha256, mod_sha256, analyzer_version, compatibility, reason, adds_json, source_build_utc,
mod_name, provider, external_id, file_id, version, size_bytes, checked_utc`, with the first three
columns forming the primary key. Source build is the archive GEN8 timestamp, not proven ancestry.
`source_files` is a secondary identity map keyed by base, provider, file ID and analyzer version.
It preserves multiple file/build IDs that refer to identical archive bytes and updates identities
when a provider changes bytes under an existing ID. Analysis is skipped for an existing primary key.
Downloads reuse the existing content-addressed cache; no archives are written back to their sources.

`cgfmm compat-scan --base /private/base/data.win --db /private/compat.db --export /srv/jamm/compat`
loads the Litterbox catalog and resolves every supported file of every released mod. Unsupported
sources and failed files are logged; other mods continue. Exit 0 means success, 1 means usage,
I/O or one or more scan failures. Partial successful results are still exported. Cancellation or
process interruption leaves previously published files intact; rerun to finish.

JSON paths are `v1/<base>/index.json` and `v1/<base>/<mod>.json`. Both use the same envelope:

```json
{"SchemaVersion":1,"BaseHash":"<64 lowercase hex>","AnalyzerVersion":4,"Checks":[
  {"Provider":"gamejolt","FileId":"123","ModName":"Example","ExternalId":"40",
   "Version":"1.0","SizeBytes":12345,"SourceBuildUtc":"2023-08-16T00:00:00Z",
   "CheckedUtc":"2026-11-16T00:00:00Z",
   "Facts":{"FileHash":"<64 lowercase hex>","BaseHash":"<64 lowercase hex>",
    "AnalyzerVersion":4,"Compatibility":"Compatible","Reason":"...","Adds":["2 sprites"]}}
]}
```

Per-mod documents include the identities for that hash. Output has ordinal ordering and preserved
check timestamps, so unchanged scans produce identical bytes. Each document is written to a
unique temporary file then atomically renamed; per-mod documents precede the index. Concurrent
cron scans are prevented by the shell wrapper's lock. No game bytes appear in exports.

## Client behavior and privacy

The default-on `useSharedCompatibilityResults` setting is persisted in `config.json`; old configs
inherit `true`. Settings exposes “Use shared compatibility results (no account, no cookies)”.
CLI users may run `cgfmm config set useSharedCompatibilityResults false`.

Browse looks up provider/file identity before downloading. A hit displays facts with the tooltip
“checked by JAMM server on <date>”. On download/import, the client hashes the actual extracted
archive, fetches its per-mod document and requires exact base/mod hashes and analyzer version.
A mismatch or miss runs the local analyzer; its tooltip says “checked on this PC”.

Requests are plain HTTPS GETs to `https://varialbe.xyz/jamm/compat/v1/<base>/...`, without cookies,
accounts, uploads or query strings. **The server sees the client's IP address, like any web request.**
Server access logs are an operator responsibility. Turning the setting off sends no compatibility
requests, including cache validation. Catalog and file-source requests remain necessary for Browse.

Lookups have a three-second total deadline including body reads, a 2 MiB maximum document size,
and public HTTPS/redirect/DNS safety checks from ModHttp. ETag and Last-Modified are persisted and
sent for conditional validation. Only validated schema-1/current-analyzer documents are cached.
Malformed, oversized, missing, timed-out and offline responses fall back locally; stale results are
not silently reused when offline. Remote facts are an optimization, never a requirement to use JAMM.

See [Phase 11 report](reports/phase11.md) for VPS deployment commands.
