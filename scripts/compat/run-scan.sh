#!/usr/bin/env bash
set -euo pipefail
: "${JAMM_BASE:?Set JAMM_BASE to the read-only base data.win}"
: "${JAMM_COMPAT_ROOT:?Set JAMM_COMPAT_ROOT to the private scanner state directory}"
: "${JAMM_COMPAT_EXPORT:?Set JAMM_COMPAT_EXPORT to the public JSON directory}"
: "${JAMM_CGFMM:?Set JAMM_CGFMM to the published cgfmm executable}"
mkdir -p "$JAMM_COMPAT_ROOT" "$JAMM_COMPAT_EXPORT"
exec 9>"$JAMM_COMPAT_ROOT/scan.lock"
flock -n 9 || exit 75
printf '%s compatibility scan starting\n' "$(date -u +%FT%TZ)"
"$JAMM_CGFMM" --data-dir "$JAMM_COMPAT_ROOT/state" compat-scan --base "$JAMM_BASE" --db "$JAMM_COMPAT_ROOT/compat.db" --export "$JAMM_COMPAT_EXPORT"
printf '%s compatibility scan completed\n' "$(date -u +%FT%TZ)"
