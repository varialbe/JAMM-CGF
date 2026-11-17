#!/usr/bin/env bash
# Build an Ubuntu 24.04+ amd64 package without requiring root.
source "$(dirname "${BASH_SOURCE[0]}")/../packaging/common.sh"
command -v dpkg-deb >/dev/null || { echo 'Install dpkg-dev, then retry.' >&2; exit 1; }
stage="$work/deb"
install -d "$stage/opt/jamm-cgf" "$stage/usr/bin" "$stage/DEBIAN" \
    "$stage/usr/share/applications" "$stage/usr/share/icons/hicolor/scalable/apps" \
    "$stage/usr/share/doc/jamm-cgf"
publish Jamm.Cli linux-x64 "$stage/opt/jamm-cgf/cli"
publish Jamm.Gui linux-x64 "$stage/opt/jamm-cgf/gui"
ln -s /opt/jamm-cgf/cli/cgfmm "$stage/usr/bin/cgfmm"
ln -s /opt/jamm-cgf/gui/cgfmm-gui "$stage/usr/bin/cgfmm-gui"
install -m644 packaging/debian/jamm-cgf.desktop "$stage/usr/share/applications/"
install -m644 packaging/jamm-cgf.svg "$stage/usr/share/icons/hicolor/scalable/apps/"
install -m644 packaging/debian/copyright "$stage/usr/share/doc/jamm-cgf/copyright"
# Modder documentation and the ModKit example packages (our own art; no game data).
install -d "$stage/usr/share/doc/jamm-cgf/examples"
cp -r examples/modkit "$stage/usr/share/doc/jamm-cgf/examples/modkit"
cp -r docs/modding "$stage/usr/share/doc/jamm-cgf/modding"
sed "1s/(1.0.0)/($version)/" packaging/debian/changelog | gzip -n -9 -c > "$stage/usr/share/doc/jamm-cgf/changelog.gz"
size=$(du -sk "$stage/opt" | cut -f1)
cat > "$stage/DEBIAN/control" <<CONTROL
Package: jamm-cgf
Version: $version
Section: games
Priority: optional
Architecture: amd64
Maintainer: JAMM-CGF contributors <jamm-cgf@users.noreply.github.com>
Homepage: https://github.com/varialbe/JAMM-CGF
Installed-Size: $size
Depends: libc6 (>= 2.39), libgcc-s1, libstdc++6, zlib1g, libssl3t64, libicu74 | libicu76 | libicu78, libfontconfig1, libfreetype6, libx11-6, libice6, libsm6, libxext6, libxrender1, libxrandr2, libxi6, libxcursor1
Description: Mod manager for legacy Cat Goes Fishing
 Merge compatible mods, review conflicts and manage profiles using a desktop
 interface or the cgfmm command line. Requires a legally obtained legacy game.
 Game files are not included. The .NET runtime is bundled.
CONTROL
find "$stage" -type d -exec chmod 755 {} +
find "$stage" -type f -exec chmod 644 {} +
chmod 755 "$stage/opt/jamm-cgf/cli/cgfmm" "$stage/opt/jamm-cgf/gui/cgfmm-gui"
package="$output/jamm-cgf_${version}_amd64.deb"
built="$work/jamm-cgf_${version}_amd64.deb"
dpkg-deb --threads-max=2 --root-owner-group --build "$stage" "$built"
if command -v lintian >/dev/null; then lintian --fail-on error,warning "$built"; fi
mv "$built" "$package"
printf 'Built %s\n' "$package"
