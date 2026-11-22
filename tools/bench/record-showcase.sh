#!/usr/bin/env bash
set -euo pipefail

# Build (or accept) the clean Harbor Town + Submarine video archive, record both
# playable showcase scenes through jamm-bench/run.sh, then crop and encode them.
ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
BENCH=${JAMM_BENCH_DIR:-$HOME/jamm-bench}
RUNNER=$BENCH/run.sh
OUT=$BENCH/videos
BUILD=$BENCH/builds/showcase
CLI=${JAMM_CLI:-$ROOT/src/Jamm.Cli/bin/Release/net10.0/cgfmm.dll}
BUILT_MM=${JAMM_MODMAKER:-$ROOT/tools/Jamm.ModMaker/bin/Release/net10.0/jamm-modmaker.dll}
MM=$BENCH/tools/showcase-mm/jamm-modmaker.dll
LEGACY=${JAMM_ORIGINAL:-$HOME/.var/app/com.valvesoftware.Steam/data/JAMM-CGF/CGF-legacy-clean/data.win}

usage() { echo "Usage: $0 [merged-video-archive.win]" >&2; }
if [[ $# -gt 1 ]]; then usage; exit 2; fi
mkdir -p "$OUT" "$BUILD"

if [[ $# -eq 1 ]]; then
    ARCHIVE=$(realpath "$1")
else
    [[ -f "$LEGACY" ]] || { echo "Legacy game archive not found: $LEGACY (set JAMM_ORIGINAL or pass a merged archive)." >&2; exit 2; }
    [[ -f "$CLI" ]] || { echo "JAMM CLI is missing: $CLI. Build src/Jamm.Cli first." >&2; exit 2; }
    for mod in submarine harbor-town; do
        dotnet "$CLI" modkit build "$ROOT/examples/modkit/$mod" --base "$LEGACY" --out "$BUILD/$mod-source.win" \
            > "$BUILD/$mod-build.log" 2>&1 || { tail -30 "$BUILD/$mod-build.log" >&2; exit 1; }
    done
    # Deliberately use only the four JAMM builtins and the two showcase mods.
    ORDER="$BENCH/inputs-now/jamm-perf.win,$BENCH/inputs-now/jamm-hd-lite.win,$BENCH/inputs-now/jamm-console.win,$BENCH/inputs-now/jamm-smooth.win,$BUILD/submarine-source.win,$BUILD/harbor-town-source.win"
    dotnet "$CLI" build --base "$LEGACY" --order "$ORDER" --out "$BUILD/video.win" \
        > "$BUILD/video-build.log" 2>&1 || { tail -50 "$BUILD/video-build.log" >&2; exit 1; }
    ARCHIVE="$BUILD/video.win"
fi
[[ -f "$ARCHIVE" ]] || { echo "Merged video archive not found: $ARCHIVE" >&2; exit 2; }
[[ -x "$RUNNER" ]] || { echo "Recording runner is not executable: $RUNNER" >&2; exit 2; }
[[ -f "$BUILT_MM" ]] || { echo "ModMaker is missing: $BUILT_MM. Build tools/Jamm.ModMaker first." >&2; exit 2; }
command -v ffmpeg >/dev/null || { echo "ffmpeg is required to encode showcase videos." >&2; exit 2; }
mkdir -p "$(dirname "$MM")"
cp -a "$(dirname "$BUILT_MM")/." "$(dirname "$MM")/"

export MM REC=1 REC_SEC=110
for scene in showcase-harbor showcase-submarine; do
    label="showcase"
    "$RUNNER" "$ARCHIVE" "$label" "$scene" 90
    run="$HOME/.var/app/com.valvesoftware.Steam/data/JAMM-CGF/bench-runs/$label-$scene"
    raw="$run/video.mp4"
    [[ -s "$raw" ]] || { echo "Recording failed; no video was written at $raw" >&2; exit 1; }
    if [[ "$scene" == showcase-harbor ]]; then
        telemetry="$run/jamm_showcase_harbor.csv"
        [[ -s "$telemetry" ]] || { echo "Harbor scene did not write telemetry: $telemetry" >&2; exit 1; }
        for building in lighthouse aquarium statue; do
            grep -q "^level,$building,100," "$telemetry" || { echo "Harbor scene did not reach level 100 for $building." >&2; exit 1; }
        done
    else
        telemetry="$run/jamm_showcase_submarine.csv"
        [[ -s "$telemetry" ]] || { echo "Submarine scene did not write telemetry: $telemetry" >&2; exit 1; }
        grep -q '^surfaced,' "$telemetry" || { echo "Submarine scene did not complete its ascent." >&2; exit 1; }
    fi

    detect=$(ffmpeg -ss 5 -t 18 -i "$raw" -vf 'cropdetect=24:16:0' -an -f null - 2>&1 \
        | sed -n 's/.*\(crop=[0-9][0-9]*:[0-9][0-9]*:[0-9][0-9]*:[0-9][0-9]*\).*/\1/p' | tail -1)
    [[ -n "$detect" ]] || detect='crop=iw:ih:0:0'
    ffmpeg -y -loglevel error -i "$raw" -vf "$detect,scale=1280:-2" -an \
        -c:v libx264 -preset fast -crf 24 -pix_fmt yuv420p -movflags +faststart "$OUT/$scene.mp4"
    ffmpeg -y -loglevel error -i "$OUT/$scene.mp4" \
        -vf 'fps=1/12,scale=320:-2,tile=4x2:padding=6:margin=6' -frames:v 1 "$OUT/$scene-contact.png"
done

ffmpeg -y -loglevel error -loop 1 -i "$OUT/showcase-harbor-contact.png" \
    -loop 1 -i "$OUT/showcase-submarine-contact.png" \
    -filter_complex '[0:v][1:v]vstack=inputs=2[out]' -map '[out]' -frames:v 1 "$OUT/showcase-contact-sheet.png"
echo "Videos: $OUT/showcase-harbor.mp4 $OUT/showcase-submarine.mp4"
echo "Contact sheet: $OUT/showcase-contact-sheet.png"
