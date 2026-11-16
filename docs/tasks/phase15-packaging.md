# Phase 15 — .deb, Windows .exe and Docker image (implementer: Codex)

Branch `build/packaging` (from main). No commits/pushes/git config. Only add packaging files (`packaging/`,
`scripts/package-*.sh`, `Dockerfile`, `.dockerignore`, a GitHub Actions workflow if useful) plus docs; do not change
app code except what packaging strictly needs (e.g. version properties, icon file references).

## User request (German, verbatim)
"compile eine deb und exe datei und ein docker image"

## Deliverables
1. **Ubuntu .deb** (`scripts/package-deb.sh`): `dotnet publish` self-contained linux-x64 (GUI + CLI, no system
   .NET needed), installs to `/opt/jamm-cgf`, symlinks `cgfmm`/`cgfmm-gui` into `/usr/bin`, `.desktop` entry and
   icon, correct Depends (fontconfig/libICU etc. as needed for Avalonia), version from the repo, `dpkg-deb --build`;
   lintian-clean if lintian is available. Output to `out/packages/`.
2. **Windows .exe**: self-contained single-file `win-x64` publish of the GUI (`cgfmm-gui.exe`) and CLI
   (`cgfmm.exe`) via `scripts/package-windows.sh` (cross-publish from Linux works for .NET); additionally an
   installer if it can be produced on Linux with free tools (e.g. Inno Setup is Windows-only — then provide an
   Inno Setup script + a GitHub Actions job on windows-latest that builds the installer; say clearly what was
   built locally vs in CI). Output a portable zip at minimum.
3. **Docker image** (`Dockerfile`): sensible scope is the headless CLI and the compatibility scanner (GUI in Docker
   makes no sense for players): multi-stage build, non-root user, `ENTRYPOINT ["cgfmm"]`, volume for the data dir,
   documented example for `compat-scan` with a mounted legally obtained base; no game files in the image.
4. Verify locally where possible: build the .deb and inspect it (`dpkg-deb -c`/`-I`), run `cgfmm --help` from the
   published linux build, `docker build` if docker is available (else say so; the orchestrator builds it).
5. Docs: README install section gets the .deb/.exe/Docker lines (keep README short), details in
   `docs/packaging.md`. Report `docs/reports/phase15.md`.
