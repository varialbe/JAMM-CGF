#!/usr/bin/env bash
# Per-user source installer. Run again to update; never touches game files.
set -euo pipefail
trap 'printf "Installation failed at line %s. Check the message above, fix the problem and run again.\n" "$LINENO" >&2' ERR
root="$HOME/.local/share/jamm-cgf"
src="$root/src"
bin="$HOME/.local/bin"
desktop="$HOME/.local/share/applications/jamm-cgf.desktop"
if [[ ${1:-} == --uninstall && $# == 1 ]]; then
    rm -f "$bin/cgfmm" "$bin/cgfmm-gui" "$desktop"
    printf 'Removed JAMM launchers and desktop entry. Source at %s, shared .NET, manager data and game backups are retained.\n' "$src"
    exit 0
fi
if [[ $# != 0 ]]; then echo 'Usage: install.sh [--uninstall]' >&2; exit 1; fi
for dependency in git curl; do
    command -v "$dependency" >/dev/null || { echo "Missing $dependency. Install git and curl with your distribution package manager, then retry." >&2; exit 1; }
done
has_sdk() { "$1" --list-sdks 2>/dev/null | grep -q '^10\.'; }
host=''
if command -v dotnet >/dev/null && has_sdk "$(command -v dotnet)"; then host="$(command -v dotnet)"
elif [[ -x "$HOME/.dotnet/dotnet" ]] && has_sdk "$HOME/.dotnet/dotnet"; then host="$HOME/.dotnet/dotnet"
else
    echo 'Installing Microsoft .NET 10 SDK for this user...'
    temp=$(mktemp -d)
    trap 'rm -rf "$temp"' EXIT
    curl --fail --show-error --location https://dot.net/v1/dotnet-install.sh -o "$temp/dotnet-install.sh"
    bash "$temp/dotnet-install.sh" --channel 10.0 --install-dir "$HOME/.dotnet" --no-path
    host="$HOME/.dotnet/dotnet"
fi
export DOTNET_ROOT
DOTNET_ROOT=$(dirname "$(readlink -f "$host")")
export PATH="$DOTNET_ROOT:$PATH"
mkdir -p "$root"
if [[ -e "$src" ]]; then
    [[ -d "$src/.git" ]] || { echo "Cannot update $src: not a Git checkout. Move it aside and retry." >&2; exit 1; }
    [[ $(git -C "$src" remote get-url origin) == https://github.com/varialbe/JAMM-CGF.git ]] || { echo "Unexpected source remote in $src. Move the checkout aside and retry." >&2; exit 1; }
    changes=$(git -C "$src" status --porcelain)
    [[ -z $changes ]] || { echo "Local changes in $src. Save them elsewhere before updating." >&2; exit 1; }
    [[ $(git -C "$src" branch --show-current) == main ]] || { echo "Source checkout must be on main: $src" >&2; exit 1; }
    git -C "$src" pull --ff-only origin main
    git -C "$src" submodule update --init --recursive
else
    git clone --branch main --recurse-submodules https://github.com/varialbe/JAMM-CGF.git "$src"
fi
echo 'Building JAMM (Release)...'
"$host" build "$src" -c Release
mkdir -p "$bin" "$(dirname "$desktop")"
for app in cgfmm cgfmm-gui; do
    project=Jamm.Cli
    [[ $app == cgfmm-gui ]] && project=Jamm.Gui
    { printf '#!/usr/bin/env bash\n'; printf 'export DOTNET_ROOT=%q\n' "$DOTNET_ROOT"; printf 'exec %q %q "$@"\n' "$host" "$src/src/$project/bin/Release/net10.0/$app.dll"; } > "$bin/$app"
    chmod +x "$bin/$app"
done
# Desktop-entry strings have their own escaping rules (including literal percent).
desktop_exec=${bin//\\/\\\\}
desktop_exec=${desktop_exec//\"/\\\"}
desktop_exec=${desktop_exec//\$/\\\$}
desktop_exec=${desktop_exec//\`/\\\`}
desktop_exec=${desktop_exec//%/%%}
cat > "$desktop" <<ENTRY
[Desktop Entry]
Type=Application
Name=JAMM-CGF
Comment=Cat Goes Fishing legacy mod manager
Exec="$desktop_exec/cgfmm-gui"
Terminal=false
Categories=Game;Utility;
ENTRY
printf 'Installed. Open JAMM-CGF from your applications menu or run "%s/cgfmm-gui".\n' "$bin"
printf 'For terminal commands, add %s to PATH if needed. Re-run this installer to update.\n' "$bin"
