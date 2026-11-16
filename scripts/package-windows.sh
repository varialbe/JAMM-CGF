#!/usr/bin/env bash
# Cross-publish portable Windows executables; Inno Setup runs separately on Windows.
source "$(dirname "${BASH_SOURCE[0]}")/../packaging/common.sh"
command -v zip >/dev/null || { echo 'Install zip, then retry.' >&2; exit 1; }
stage="$work/windows"
publish Jamm.Cli win-x64 "$stage"
publish Jamm.Gui win-x64 "$work/gui"
# Each executable embeds its own managed/native dependencies.
cp -a "$work/gui/." "$stage/"
install -m644 LICENSE "$stage/LICENSE.txt"
install -m644 packaging/windows/README.txt "$stage/README.txt"
archive="$output/jamm-cgf_${version}_win-x64.zip"
# Build a fresh archive and replace the previous output only on success.
(cd "$stage" && zip -q -r "$work/portable.zip" .)
mv "$work/portable.zip" "$archive"
printf 'Built %s (installer: run packaging/windows/jamm-cgf.iss with Inno Setup on Windows).\n' "$archive"
