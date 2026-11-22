#!/usr/bin/env bash
set -euo pipefail

repo_root="$(git -C "$(dirname "${BASH_SOURCE[0]}")/../.." rev-parse --show-toplevel)"
temp_root="$(mktemp -d "${TMPDIR:-/tmp}/jamm-cgf-site.XXXXXX")"
checkout="$temp_root/checkout"
cleanup() {
  rm -rf "$temp_root"
}
trap cleanup EXIT

git -C "$repo_root" fetch --all --prune
origin_url="$(git -C "$repo_root" remote get-url origin)"
git clone --branch main "$origin_url" "$checkout"
git -C "$checkout" fetch --all --prune
git -C "$checkout" checkout --detach origin/main
python3 "$checkout/site/cgf-mods/make-pages.py"

if git -C "$checkout" diff --quiet HEAD -- site/cgf-mods; then
  echo "JAMM-CGF site output is unchanged; skipping publish."
  exit 0
fi

rsync --archive --delete \
  --exclude make-packages.py \
  --exclude make-pages.py \
  --exclude make_progress.py \
  --exclude sources/ \
  "$checkout/site/cgf-mods/" \
  varialbe@varialbe.xyz:/var/www/html/cgf-mods/
echo "Published regenerated JAMM-CGF site from origin/main."
