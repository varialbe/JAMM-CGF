# Packaging JAMM-CGF

Packages contain the GUI and CLI with .NET 10 bundled. They contain no game or mod
archives. Obtain the matching legacy 2023 game yourself; see [legacy setup](legacy-version.md).

## Build prerequisites and version

Use a recursive checkout (`git submodule update --init --recursive`), .NET 10 SDK,
and network access to NuGet. The submodule remains pinned to its recorded commit.
On Linux install `dpkg-dev` and `zip`; `lintian` is optional. Run scripts from any
working directory. All outputs go to `out/packages/` (ignored by Git).
The version is read from `dotnet msbuild src/Jamm.Gui/Jamm.Gui.csproj -getProperty:Version`;
manual builds use the project version (the SDK default is **1.0.0** unless overridden).
Set the `Version` environment variable to override filenames and package metadata:
`Version=0.9.0 bash scripts/package-deb.sh`. The Windows package script accepts the
same override. Tag builds set this variable automatically.

```sh
bash scripts/package-deb.sh
bash scripts/package-windows.sh
```

Builds are Release, self-contained, untrimmed single-file applications. Native
libraries extract on launch into the user's temporary directory, which must be
writable and allow loading shared libraries. `DOTNET_BUNDLE_EXTRACT_BASE_DIR` can
select a different location. Keep the entire portable payload: UndertaleModLib
also supplies external `GameSpecificData` files. No .NET SDK/runtime is needed
on the target machine. Cross-publishing does not test execution on Windows.

## Ubuntu amd64

Target: Ubuntu 24.04 or newer, x86-64, with an X11 desktop or XWayland for the GUI.
The package declares ICU, OpenSSL, fontconfig, FreeType and X11 dependencies;
apt resolves them. Other distributions and older Ubuntu releases are not verified.

```sh
sudo apt install ./out/packages/jamm-cgf_1.0.0_amd64.deb
cgfmm --help
cgfmm-gui
sudo apt remove jamm-cgf
```

The package installs separate CLI/GUI payloads below `/opt/jamm-cgf`, symlinks in
`/usr/bin`, and the JAMM-CGF desktop entry and original SVG icon. No maintainer
scripts run and no game files or manager profiles are changed during installation
or removal. A previous per-user source installation may take precedence on PATH;
remove its launchers with `bash scripts/install.sh --uninstall` if switching.

The builder uses `dpkg-deb --root-owner-group` without root, and runs lintian
with warnings/errors treated as failures when available. Inspect with:

```sh
dpkg-deb -I out/packages/jamm-cgf_1.0.0_amd64.deb
dpkg-deb -c out/packages/jamm-cgf_1.0.0_amd64.deb
```

## Windows x64

Extract `jamm-cgf_1.0.0_win-x64.zip` to a writable directory. Open
`cgfmm-gui.exe`, or run `cgfmm.exe --help` in a terminal. The zip includes both
self-contained executables, supporting content and GPL license. These are unsigned;
Windows may display an unknown publisher prompt. Launch the legacy game manually
after Install, as described in the [user guide](user-guide.md).

The portable zip can be built on Linux. The optional per-user installer uses
free Inno Setup 6 on Windows; Linux builds do **not** produce an installer.
After extracting the zip to `out/payload`, compile in PowerShell:

```powershell
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" /DAppVersion=1.0.0 "/DPayloadDir=$pwd\out\payload" "/DOutputDir=$pwd\out\packages" packaging/windows/jamm-cgf.iss
```

The installer adds a Start menu shortcut and uninstaller without administrator
privileges. It does not add PATH entries or remove user profiles/game files.

## Release process

The Packaging workflow supports manual `workflow_dispatch` builds and `v*` tag
pushes. Manual runs use the project version, upload packages as workflow artifacts,
and build and smoke-test Docker locally without publishing.

For a release, prepare `docs/release-notes/vX.Y.Z.md` (include the full suffix in
the filename when applicable), review the changes, and have the release
orchestrator push a tag such as `v0.9.0` or `v1.0.0-rc.1` on the intended commit.
Tags must follow `vX.Y.Z` with an optional `-suffix`. CI strips the leading `v`
and passes `Version` to both package scripts, so filenames and metadata match.
The Ubuntu job builds the `.deb` and Windows x64 zip; the Windows job downloads
the zip and compiles `jamm-cgf_<version>_win-x64_setup.exe`, installing Inno Setup
through Chocolatey if needed.

After both package jobs succeed, CI creates a GitHub release titled
`JAMM-CGF vX.Y.Z` (including any suffix) and attaches all three packages. It uses
the matching release-notes file when present, otherwise GitHub-generated notes.
Major version zero and any suffixed tag are prereleases; these are explicitly
excluded from GitHub's latest release selection. Use a new tag/version for a
correction: release creation does not overwrite an existing release.

The Docker job builds an image with OCI source, GPL-3.0 license and description
labels and runs `--help` as a smoke test before publishing on tag pushes. It uses
`GITHUB_TOKEN` to push `ghcr.io/<lowercase-repository-owner>/jamm-cgf:X.Y.Z`
(including any suffix). Only stable releases also update `:latest`; preview
releases such as `v0.9.0` do not. The release job has `contents: write`, and the
Docker job has `packages: write`; repository/organization policy must allow these
token permissions. Inspect the workflow run to verify both release assets and
image publication. Windows packaging in CI does not establish real-hardware
compatibility.

## Headless Docker CLI and scanner

Use an x86-64 Docker host (or an explicitly configured amd64 emulator). From the
repository root with initialized submodules:

```sh
docker build --platform linux/amd64 -t jamm-cgf:local .
docker run --rm jamm-cgf:local --help
```

The multi-stage image uses .NET 10 Ubuntu Noble build/runtime-deps images,
contains only the CLI publish output, and runs as the non-root `app` user
(UID 1654). The `.dockerignore` allowlist excludes local outputs, archives,
credentials and game data. `/data` is a volume; explicitly pass `--data-dir`
to keep manager state there. Internet access is needed for the catalog scanner.

For a scanner run, prepare a writable output directory and mount your legally
obtained original read-only. Adjust the host path; do not mount the game writable:

```sh
mkdir -p out/scan
# Give the container's UID access to this output directory once:
sudo chown 1654:1654 out/scan
docker run --rm \
  --mount type=bind,src="/absolute/path/to/legacy/data.win",dst=/base/data.win,readonly \
  --mount type=bind,src="$PWD/out/scan",dst=/data \
  jamm-cgf:local --data-dir /data/manager compat-scan \
  --base /base/data.win --db /data/compat.db --export /data/export
```

The scanner downloads catalog mods and writes its database, cache and exports
under the output mount. Results survive container deletion. It returns a nonzero
status when scans fail. Local game/mod content must never be added to the image
or uploaded as a workflow artifact. Docker has no desktop GUI.

## Redistribution

JAMM-CGF and UndertaleModLib are GPL-3.0. Distribute the corresponding source
(including the pinned submodules and packaging scripts) alongside binaries, or
meet the GPL source-offer requirements. Source:
<https://github.com/varialbe/JAMM-CGF>. Preserve bundled third-party license notices;
.NET, Avalonia, SkiaSharp, Magick.NET and SQLite dependencies retain their own
licenses. The root `LICENSE` is included in Windows and Docker outputs; Debian
provides a machine-readable copyright file referencing the system GPL text.
No copyrighted game assets are supplied.

## What every package contains

The .deb, the Windows zip/installer and the Docker image contain the CLI and GUI, the four built-in JAMM
mods (Perf, Smooth, HD Lite, Console; they are generated locally from your own legacy game on first use, so no
game data is shipped), the modder guide (`docs/modding`) and the ModKit example packages
(`examples/modkit`: `example-fish`, `showcase-upgrades`, with original art). Locations: `.deb`
`/usr/share/doc/jamm-cgf/{modding,examples/modkit}`; Windows `docs\modding`, `examples\modkit` next to the
executables; Docker `/usr/share/doc/jamm-cgf/`.

## Windows installer on Linux (Inno Setup under Wine)

`package-windows.sh` builds `jamm-cgf_<version>_win-x64_setup.exe` when `ISCC` names an Inno Setup compiler
command. Inno Setup 7 runs under Wine (64-bit prefix, `WINEARCH=win64`):

```sh
WINEARCH=win64 wine innosetup-7.x-x64.exe /VERYSILENT /CURRENTUSER
ISCC="wine \"$HOME/.wine/drive_c/users/$USER/AppData/Local/Programs/Inno Setup 7/ISCC.exe\"" \
    bash scripts/package-windows.sh
```

Paths are converted with `winepath` when it is on PATH. Without `ISCC` only the portable zip is built.
