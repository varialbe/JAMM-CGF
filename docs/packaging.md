# Packaging JAMM-CGF

Packages contain the GUI and CLI with .NET 10 bundled. They contain no game or mod
archives. Obtain the matching legacy 2023 game yourself; see [legacy setup](legacy-version.md).

## Build prerequisites and version

Use a recursive checkout (`git submodule update --init --recursive`), .NET 10 SDK,
and network access to NuGet. The submodule remains pinned to its recorded commit.
On Linux install `dpkg-dev` and `zip`; `lintian` is optional. Run scripts from any
working directory. All outputs go to `out/packages/` (ignored by Git).
The version is read from `dotnet msbuild src/Jamm.Gui/Jamm.Gui.csproj -getProperty:Version`;
currently the SDK default is **1.0.0**. Set an explicit repository version property
before a future release, rather than renaming artifacts independently.

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

`.github/workflows/packaging.yml` runs manually or for `v*` tags. It builds the
portable packages on Ubuntu, then compiles `jamm-cgf_<version>_win-x64_setup.exe`
on `windows-latest` using its Inno Setup installation. The installer adds a Start
menu shortcut and uninstaller without administrator privileges. It does not add
PATH entries or remove user profiles/game files. CI uploads artifacts only; it
neither publishes releases nor pushes an image. CI has not been run merely by
adding this workflow.

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
