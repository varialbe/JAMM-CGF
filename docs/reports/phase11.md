# Phase 11 — shared compatibility database

Implemented on `ui/browse-mods`, without commits, pushes, git configuration changes or tools/ edits.

The server scanner uses the existing catalog, resolver, bounded download cache and ModFactAnalyzer.
SQLite checks are keyed by exact base/mod hashes and analyzer revision. A secondary identity table
retains aliases without repeating analysis. Static JSON exports are deterministic and published
with temp-and-rename writes. The cron wrapper serializes scans with flock and returns 75 on lock
contention; errors return 1. Timestamps and one-event-per-line stdout suit journald/log rotation.

Browse previews server facts before archive download, validates the archive hash after downloading,
and falls back to local analysis. Settings has a persisted default-on opt-out switch. Conditional
HTTP caching, a three-second deadline, a 2 MiB limit and schema/version validation bound lookups.
No cookies, account, uploads or telemetry are used; the server still sees request IP addresses.

The cache excludes installed-mod combinations: selections/load orders create too many keys for
useful cache reuse. Local Check already resolves profile/pairwise conflicts from cached patches.
See [protocol and privacy details](../compat-db.md).

## VPS deployment (orchestrator)

These commands are deployment instructions; no VPS configuration was changed by the implementer.
Use a VPS with .NET 10 SDK/runtime, nginx, util-linux (flock), and a private legally obtained legacy
base. The following assumes the operator has checked out this worktree's eventual revision at
`/opt/jamm/source` and uses nginx's `www-data` group. Adjust the group on distributions using nginx.

```sh
sudo useradd --system --home /var/lib/jamm-compat --create-home --shell /usr/sbin/nologin jamm-compat
sudo install -d -o jamm-compat -g jamm-compat -m 700 /var/lib/jamm-compat/private
sudo install -d -o jamm-compat -g www-data -m 755 /srv/jamm/compat
cd /opt/jamm/source
git submodule update --init --recursive
dotnet publish src/Jamm.Cli -c Release -o /tmp/jamm-compat-publish
sudo mkdir -p /opt/jamm/compat-bin
sudo cp -a /tmp/jamm-compat-publish/. /opt/jamm/compat-bin/
sudo install -m 755 scripts/compat/run-scan.sh /opt/jamm/run-compat-scan.sh
# Copy your legally obtained legacy base from private storage; never download it from this repository.
sudo install -o jamm-compat -g jamm-compat -m 400 /private/operator/legacy/data.win /var/lib/jamm-compat/private/data.win
```

Create `/etc/jamm-compat.env` (root-owned, mode 644; contains paths, no credentials):

```sh
JAMM_BASE=/var/lib/jamm-compat/private/data.win
JAMM_COMPAT_ROOT=/var/lib/jamm-compat/private
JAMM_COMPAT_EXPORT=/srv/jamm/compat
JAMM_CGFMM=/opt/jamm/compat-bin/cgfmm
```

First scan and verification:

```sh
sudo -u jamm-compat sh -c 'set -a; . /etc/jamm-compat.env; set +a; exec /opt/jamm/run-compat-scan.sh'
sudo sha256sum /var/lib/jamm-compat/private/data.win
# Inspect /srv/jamm/compat/v1/<base hash>/index.json; scan again and compare checksums.
```

In the existing **HTTPS varialbe.xyz server block**, add this location (serve only exports, never
`/var/lib/jamm-compat` or scanner state):

```nginx
location /jamm/compat/ {
    alias /srv/jamm/compat/;
    autoindex off;
    etag on;
    if_modified_since exact;
    default_type application/json;
    add_header Cache-Control "public, max-age=300" always;
    access_log off;
    limit_except GET HEAD { deny all; }
}
```

Validate and reload with `sudo nginx -t && sudo systemctl reload nginx`. Verify HTTPS response
headers using `curl -I https://varialbe.xyz/jamm/compat/v1/<base hash>/index.json`, then verify a
conditional request returns 304 with its ETag. Configure TLS using the VPS's existing certificate.
Do not add cookies or request analytics. Upstream proxy logs, if any, require their own retention policy.

Create `/etc/cron.d/jamm-compat`:

```cron
17 3 * * * jamm-compat /bin/sh -c 'set -a; . /etc/jamm-compat.env; set +a; exec /opt/jamm/run-compat-scan.sh' 2>&1 | /usr/bin/logger -t jamm-compat
```

For a manual run, invoke the wrapper directly to observe its exit code (the cron logger pipeline
records output but does not preserve the scanner's status). Inspect `journalctl -t jamm-compat`.
Retain SQLite and state privately between runs; back up the database with SQLite's backup API while
idle. On an analyzer update publish the new binary and rescan; old analyzer rows are retained but
only current analyzer facts are exported. If deploying an incompatible wire schema, use a new URL
version. Roll back by disabling the nginx location; clients automatically analyze locally.

## Verification

- Release solution build: passed, zero warnings and zero errors.
- Release tests: Core 263 passed / 5 opt-in real-file tests skipped; GUI 73 passed / 1 opt-in real-file test skipped.
- Fake HTTP tests cover hits, misses, offline, malformed/null JSON, oversized responses, timeouts,
  schema/analyzer mismatch, downloaded SHA mismatch, ETag/304, Last-Modified/304 and persisted opt-out
  with no request. GUI integration proves pre-download hits, local fallback and replacement of a
  mismatched server preview after downloading.
- Fake catalog/resolver with generated synthetic archives exercises scanner persistence, aliases
  for identical bytes, skip-on-existing-key, repeatable export and unchanged read-only inputs.
- CLI help and missing-option error path checked; shell wrapper passed `bash -n`.
- `git diff --check` passed. No external submodule or tools/ source changes.

Verification used `NUGET_PACKAGES=/tmp/jamm-nuget` and
`NUGET_HTTP_CACHE_PATH=/tmp/jamm-http` because the default package caches are read-only in this
sandbox. Final commands were `dotnet build -c Release --disable-build-servers -m:1` and
`dotnet test -c Release --disable-build-servers -m:1`. The SQLite native bundle is explicitly pinned
to 3.0.5 to avoid the vulnerable older transitive native library; no audit warnings were suppressed.
The existing test runner emits a VSTEST_ARTIFACTS_DIRECTORY notice; it does not affect results.

Live VPS publication and a full real-catalog/game-file scan were not performed. They require the
orchestrator's server access and private base archive; deployment instructions above are ready to use.
