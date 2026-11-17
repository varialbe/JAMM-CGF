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
# Modder documentation and the ModKit example packages (our own art; no game data).
mkdir -p "$stage/examples" "$stage/docs"
cp -r examples/modkit "$stage/examples/modkit"
cp -r docs/modding "$stage/docs/modding"
archive="$output/jamm-cgf_${version}_win-x64.zip"
# Build a fresh archive and replace the previous output only on success.
(cd "$stage" && zip -q -r "$work/portable.zip" .)
mv "$work/portable.zip" "$archive"
printf 'Built %s\n' "$archive"
# Optional installer: set ISCC to an Inno Setup compiler command, e.g. ISCC="wine $HOME/.wine/drive_c/Program Files (x86)/Inno Setup 6/ISCC.exe"
# (Inno Setup runs fine under Wine) or run packaging/windows/jamm-cgf.iss with Inno Setup on Windows.
if [[ -n "${ISCC:-}" ]]; then
    to_win() { if command -v winepath >/dev/null; then winepath -w "$1"; else printf '%s' "$1"; fi; }
    eval "$ISCC" "/DAppVersion=$version" "\"/DPayloadDir=$(to_win "$stage")\"" "\"/DOutputDir=$(to_win "$output")\"" "\"$(to_win "$repo/packaging/windows/jamm-cgf.iss")\""
    printf 'Built %s\n' "$output/jamm-cgf_${version}_win-x64_setup.exe"
else
    echo 'Installer skipped (set ISCC to build jamm-cgf_<version>_win-x64_setup.exe; see docs/packaging.md).'
fi
