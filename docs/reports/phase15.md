# Phase 15 — Packaging

Implemented on `build/packaging`, without commits, pushes or git configuration
changes. Scope is packaging and documentation; application code and the pinned
UndertaleModTool source are unchanged.

## Delivered

- `scripts/package-deb.sh`: self-contained Release linux-x64 CLI and GUI, separate
  payloads in `/opt/jamm-cgf`, `/usr/bin` symlinks, desktop entry, original SVG
  icon, Debian metadata and native dependencies for Ubuntu 24.04+ amd64.
  Rootless package assembly, root ownership in the archive, bounded compression
  workers and optional lintian validation. Outputs replace prior packages only
  after a successful build/check.
- `scripts/package-windows.sh`: self-contained, untrimmed single-file win-x64
  CLI and GUI, supporting GameSpecificData files, license and portable README,
  assembled into a fresh zip. Native dependencies are bundled for extraction.
- Shared packaging setup resolves MSBuild's repository version (currently SDK
  default `1.0.0`), checks prerequisites, cleans temporary staging directories,
  and limits publish concurrency. Both scripts work outside the repository root.
- `packaging/windows/jamm-cgf.iss`: optional per-user Inno Setup 6 installer,
  Start menu entry and uninstaller. The GitHub Actions workflow cross-publishes
  on Ubuntu, builds the installer on Windows, and smoke-tests Docker. Artifacts
  are uploaded to Actions, with no automatic releases or registry pushes.
- `Dockerfile`: multi-stage .NET 10 Noble CLI-only image, non-root `app` user,
  `/data` volume, `ENTRYPOINT ["cgfmm"]`, default help command. A restrictive
  `.dockerignore` excludes game files, archives, outputs and Git metadata.
- Short README install additions and `docs/packaging.md` with build/install/
  removal instructions, version policy, Windows installer compilation, GPL
  source guidance and a read-only base mount example for `compat-scan`.

## Local validation

- `dotnet build -c Release`: passed, **0 warnings, 0 errors**.
- `dotnet test -c Release --no-build`: **351 passed, 7 skipped, 0 failed**
  (Core: 264/6, GUI: 87/1). Real-file tests remain opt-in; no game data used.
- Shell syntax validation passed for both builders and their shared helper.
  Workflow YAML parsed successfully.
- `desktop-file-validate packaging/debian/jamm-cgf.desktop`: passed.
- Built `out/packages/jamm-cgf_1.0.0_amd64.deb`; inspected control metadata
  with `dpkg-deb -I` and complete file list with `dpkg-deb -c`; verified root
  ownership, normalized permissions, both launcher symlinks and no game archives;
  extracted without
  installing it. Published Linux `cgfmm --help` and an empty-store
  `cgfmm --data-dir /tmp/jamm-packaged-smoke list --json` passed (including
  native SQLite initialization).
- Built `out/packages/jamm-cgf_1.0.0_win-x64.zip` by cross-publishing on Linux.
  Both executables are Windows x64 PE applications. Portable archive integrity
  and presence of both executables checked.

The environment's home-directory NuGet cache is read-only. Validation used a
writable copy at `/tmp/jamm-packaging-nuget` with
`NUGET_HTTP_CACHE_PATH=/tmp/jamm-packaging-http`. The first publish attempt
failed before this adjustment. An initial Windows publish lost MSBuild workers;
the retry with bounded concurrency succeeded. The sandbox also prevents bundle
extraction in the home directory, so Linux help was run with
`DOTNET_BUNDLE_EXTRACT_BASE_DIR=/tmp/jamm-bundle-cache` (documented override).
A Debian verification rebuild was interrupted by editing the builder while it
was running; the final script was rerun unchanged. The test runner printed its
existing VSTEST temporary-directory fallback notice.

## Verification limits

Docker and lintian are not installed here: the image build/run and lintian
cleanliness are **not locally verified**. The orchestrator can run the documented
Docker commands; the supplied CI workflow also builds and smoke-tests the image.
No Docker image artifact was built locally. Windows execution, GUI interaction,
installation/removal on Ubuntu, and the Inno Setup compilation were not run on
this Linux host. The installer is provided as source plus a Windows CI job;
no installer `.exe` was built locally and CI has not been triggered.

No input game files were opened or modified, and packages contain no `.win`
archives. Generated binaries remain under ignored `out/` and are not staged.
