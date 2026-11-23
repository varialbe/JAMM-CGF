#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
base_path="${JAMM_REAL_BUILTINS_BASE:-${HOME}/.var/app/com.valvesoftware.Steam/data/JAMM-CGF/CGF-legacy-clean/data.win}"

if [[ ! -f "$base_path" ]]; then
    echo "Legacy base archive not found: $base_path" >&2
    echo "Set JAMM_REAL_BUILTINS_BASE to the local legacy 2023 data.win and retry." >&2
    exit 2
fi

cd "$repo_root"
temp_root="$(mktemp -d /dev/shm/jamm-real-tests.XXXXXX)"
trap 'rm -rf "$temp_root"' EXIT
TMPDIR="$temp_root" JAMM_REAL_BUILTINS_BASE="$base_path" dotnet test tests/Jamm.Core.Tests/Jamm.Core.Tests.csproj \
    -c Release -m:1 --filter 'FullyQualifiedName~RealFileBuiltinsTests'
