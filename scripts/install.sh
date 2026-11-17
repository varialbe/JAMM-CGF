#!/usr/bin/env bash
# Per-user source installer. Run again to update; never touches game files.
set -euo pipefail
trap 'printf "Installation failed at line %s. Check the message above, fix the problem and run again.\n" "$LINENO" >&2' ERR
root="$HOME/.local/share/jamm-cgf"
src="$root/src"
bin="$HOME/.local/bin"
desktop="$HOME/.local/share/applications/jamm-cgf.desktop"
mode="install"
if [[ $# -gt 1 ]]; then
    echo 'Usage: install.sh [--get-game | --no-game | --uninstall]' >&2
    exit 1
elif [[ $# -eq 1 ]]; then
    case "$1" in
        --uninstall)
            rm -f "$bin/cgfmm" "$bin/cgfmm-gui" "$desktop"
            printf 'Removed JAMM launchers and desktop entry. Source at %s, shared .NET, manager data and game backups are retained.\n' "$src"
            exit 0
            ;;
        --get-game) mode="get-game" ;;
        --no-game) mode="no-game" ;;
        *)
            echo 'Usage: install.sh [--get-game | --no-game | --uninstall]' >&2
            exit 1
            ;;
    esac
fi

file_sha256() {
    local out=""
    if command -v sha256sum >/dev/null 2>&1; then
        out=$(sha256sum "$1" 2>/dev/null || true)
    elif command -v shasum >/dev/null 2>&1; then
        out=$(shasum -a 256 "$1" 2>/dev/null || true)
    fi
    printf '%s' "${out%% *}"
}

get_legacy_game() {
    local depot_cmd='download_depot 343780 343782 9039707068725406600'
    local ref_data_sha='dbb2e5c4b788358d0c6465d3005b2e9d46c4c540e423b064d7a5a3629286d2c1'
    local -a base_roots=(
        "$HOME/.steam/debian-installation"
        "$HOME/.local/share/Steam"
        "$HOME/.steam/steam"
        "$HOME/.steam/root"
        "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam"
        "$HOME/snap/steam/common/.local/share/Steam"
    )
    local -a variants_labels=()
    local -a variants_cmd=()
    if command -v flatpak >/dev/null 2>&1 && { flatpak info com.valvesoftware.Steam >/dev/null 2>&1 || flatpak info --user com.valvesoftware.Steam >/dev/null 2>&1 || flatpak info --system com.valvesoftware.Steam >/dev/null 2>&1; }; then
        variants_labels+=("Flatpak")
        variants_cmd+=("flatpak run com.valvesoftware.Steam")
    fi
    if command -v snap >/dev/null 2>&1 && snap list steam >/dev/null 2>&1; then
        variants_labels+=("Snap")
        variants_cmd+=("snap run steam")
    fi
    if command -v steam >/dev/null 2>&1; then
        variants_labels+=("Native/apt")
        variants_cmd+=("steam")
    fi
    if [[ ${#variants_labels[@]} -gt 0 ]]; then
        printf 'Detected Linux Steam install(s): %s\n' "${variants_labels[*]}"
    else
        echo 'No Linux Steam install detected (checked Flatpak, Snap, and native/apt roots).'
    fi

    find_verified_depot() {
        local r sub dir sha line
        local -a all_roots=("${base_roots[@]}")
        for r in "${base_roots[@]}"; do
            if [[ -f "$r/steamapps/libraryfolders.vdf" ]]; then
                while IFS= read -r line; do
                    [[ -n "$line" ]] && all_roots+=("$line")
                done < <(grep -oE '"path"[[:space:]]+"[^"]+"' "$r/steamapps/libraryfolders.vdf" 2>/dev/null | sed -E 's/.*"([^"]+)"$/\1/; s/\\\\/\\/g' || true)
            fi
        done
        for r in "${all_roots[@]}"; do
            for sub in \
                "steamapps/content/app_343780/depot_343782" \
                "steamrt64/steamapps/content/app_343780/depot_343782" \
                "ubuntu12_32/steamapps/content/app_343780/depot_343782" \
                "steamapps/common/Cat Goes Fishing"; do
                dir="$r/$sub"
                if [[ -f "$dir/data.win" && -f "$dir/Cat Goes Fishing.exe" ]]; then
                    sha=$(file_sha256 "$dir/data.win")
                    if [[ "$sha" == "$ref_data_sha" ]]; then
                        printf '%s\n' "$dir"
                        return 0
                    fi
                fi
            done
        done
        return 1
    }

    print_manual_instructions() {
        echo 'To download the legacy 2023 Cat Goes Fishing depot manually (requires owning the game on Steam):'
        echo '  1. Open the Steam console for your Linux Steam variant:'
        echo '     - Native / apt: steam steam://open/console  (or start with: steam -console)'
        echo '     - Flatpak:      flatpak run com.valvesoftware.Steam steam://open/console  (or add -console)'
        echo '     - Snap:         snap run steam steam://open/console  (or add -console)'
        printf '  2. In the Steam "Console" tab, paste and run:\n     %s\n' "$depot_cmd"
        echo '  3. Wait for "Depot download complete", then open JAMM-CGF (or run "'"$src"'/scripts/install.sh --get-game").'
    }

    local found_dir=""
    if found_dir=$(find_verified_depot); then
        printf 'Verified legacy Cat Goes Fishing depot found at: %s\n' "$found_dir"
        echo 'JAMM will detect it automatically.'
        return 0
    fi

    if [[ "${1:-}" == "--no-game" || ! -t 0 ]]; then
        print_manual_instructions
        return 0
    fi

    if [[ ${#variants_cmd[@]} -eq 0 ]]; then
        echo 'Steam must be installed and you must own Cat Goes Fishing on Steam to download the legacy 2023 depot.'
        print_manual_instructions
        return 0
    fi

    echo 'Legacy 2023 Cat Goes Fishing depot not found yet.'
    echo 'Steam does not allow external tools to run console commands automatically; paste this command into the Steam Console tab:'
    printf '  %s\n' "$depot_cmd"
    if command -v wl-copy >/dev/null 2>&1 && printf '%s' "$depot_cmd" | wl-copy >/dev/null 2>&1; then
        echo 'Copied command to clipboard (wl-copy).'
    elif command -v xclip >/dev/null 2>&1 && printf '%s' "$depot_cmd" | xclip -selection clipboard >/dev/null 2>&1; then
        echo 'Copied command to clipboard (xclip).'
    elif command -v xsel >/dev/null 2>&1 && printf '%s' "$depot_cmd" | xsel --clipboard --input >/dev/null 2>&1; then
        echo 'Copied command to clipboard (xsel).'
    fi

    local chosen_cmd="${variants_cmd[0]}"
    if [[ ${#variants_cmd[@]} -gt 1 ]]; then
        echo 'Multiple Steam installations were detected:'
        local idx
        for idx in "${!variants_labels[@]}"; do
            printf '  %d) %s (%s)\n' "$((idx + 1))" "${variants_labels[$idx]}" "${variants_cmd[$idx]}"
        done
        local sel=""
        printf 'Which Steam installation should open the console? [1-%d, default 1]: ' "${#variants_labels[@]}"
        read -r sel || sel=""
        if [[ "$sel" =~ ^[0-9]+$ ]] && (( sel >= 1 && sel <= ${#variants_cmd[@]} )); then
            chosen_cmd="${variants_cmd[$((sel - 1))]}"
        fi
    fi

    local -a cmd_parts
    read -r -a cmd_parts <<< "$chosen_cmd"
    printf 'Opening Steam console: %s steam://open/console\n' "$chosen_cmd"
    if command -v setsid >/dev/null 2>&1; then
        setsid "${cmd_parts[@]}" "steam://open/console" >/dev/null 2>&1 </dev/null &
    else
        nohup "${cmd_parts[@]}" "steam://open/console" >/dev/null 2>&1 </dev/null &
    fi

    local restart_reply=""
    printf 'If the Steam "Console" tab did not appear, restart Steam with -console now? [y/N]: '
    read -r restart_reply || restart_reply=""
    if [[ "$restart_reply" =~ ^[Yy]([Ee][Ss])?$ ]]; then
        printf 'Shutting down Steam (%s -shutdown)...\n' "$chosen_cmd"
        "${cmd_parts[@]}" -shutdown >/dev/null 2>&1 || true
        sleep 5
        printf 'Starting Steam with -console (%s -console)...\n' "$chosen_cmd"
        if command -v setsid >/dev/null 2>&1; then
            setsid "${cmd_parts[@]}" -console >/dev/null 2>&1 </dev/null &
        else
            nohup "${cmd_parts[@]}" -console >/dev/null 2>&1 </dev/null &
        fi
    fi

    printf 'Waiting for verified legacy depot (paste "%s" into Steam Console; press Enter or Ctrl+C to skip, timeout 30 min)...\n' "$depot_cmd"
    local interrupted=0
    trap 'interrupted=1' INT
    local elapsed=0
    local timeout=1800
    while (( elapsed < timeout && !interrupted )); do
        if found_dir=$(find_verified_depot); then
            trap - INT
            printf 'Verified legacy Cat Goes Fishing depot found at: %s\n' "$found_dir"
            echo 'JAMM will detect it automatically.'
            return 0
        fi
        if read -r -t 5 _; then
            echo 'Skipped waiting for depot download.'
            trap - INT
            return 0
        fi
        if (( interrupted )); then
            echo ''
            echo 'Skipped waiting for depot download.'
            trap - INT
            return 0
        fi
        elapsed=$((elapsed + 5))
    done
    trap - INT
    if (( interrupted )); then
        echo ''
        echo 'Skipped waiting for depot download.'
        return 0
    fi
    echo 'Timed out after 30 minutes waiting for the legacy depot. Run "'"$src"'/scripts/install.sh --get-game" when the download finishes.'
    return 0
}

if [[ "$mode" == "get-game" ]]; then
    trap - ERR
    get_legacy_game || true
    exit 0
fi
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
printf 'To re-check or wait for the legacy depot download, run "%s/scripts/install.sh --get-game".\n' "$src"
trap - ERR
if [[ "$mode" == "no-game" ]]; then
    get_legacy_game --no-game || true
else
    get_legacy_game || true
fi
