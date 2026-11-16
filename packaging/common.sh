#!/usr/bin/env bash
# Shared packaging setup; source from a package script.
set -euo pipefail
trap 'printf "Packaging failed at line %s. Check the error above, install the required tools or initialize submodules, then retry.\n" "$LINENO" >&2' ERR
repo=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$repo"
command -v dotnet >/dev/null || { echo 'Install the .NET 10 SDK, then retry.' >&2; exit 1; }
[[ -f external/UndertaleModTool/UndertaleModLib/UndertaleModLib.csproj ]] || { echo 'Run git submodule update --init --recursive, then retry.' >&2; exit 1; }
version=$(dotnet msbuild src/Jamm.Gui/Jamm.Gui.csproj -getProperty:Version -nologo)
[[ $version =~ ^[0-9]+\.[0-9]+\.[0-9]+([.-][A-Za-z0-9.-]+)?$ ]] || { echo "Unsupported package version: $version" >&2; exit 1; }
output="$repo/out/packages"
mkdir -p "$output"
work=$(mktemp -d "$output/.package-XXXXXX")
trap 'rm -rf "$work"' EXIT
publish() {
    dotnet publish "$repo/src/$1/$1.csproj" -c Release -r "$2" --self-contained true -m:1 \
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o "$3"
}
