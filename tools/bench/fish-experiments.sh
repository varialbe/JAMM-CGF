#!/usr/bin/env bash
# Run fish-isolation and fish-count scaling experiments across one or more data.win builds
# using ~/jamm-bench/run.sh, and write a Markdown summary table to ~/jamm-bench/results/.
#
# Usage:
#   tools/bench/fish-experiments.sh [--report-only] <build.win>...
#
# Environment overrides:
#   SCENE=dock           Benchmark scene: dock, sonar, or deepwater (default: dock)
#   DURATION=30          Capture duration in seconds (default: 30)
#   RUN_SH=...           Path to run.sh (default: ~/jamm-bench/run.sh)
#   RUNROOT=...          Directory where run.sh saves run folders
#   RESULTS_DIR=...      Output directory for the Markdown table (default: ~/jamm-bench/results)
#   DATE_TAG=...         Date tag in output filename (default: YYYY-MM-DD)
set -euo pipefail

REPORT_ONLY=0
if [[ "${1:-}" == "--report-only" ]]; then
  REPORT_ONLY=1
  shift
fi

if [[ $# -lt 1 ]]; then
  echo "Usage: $0 [--report-only] <build.win>..." >&2
  exit 1
fi

RUN_SH="${RUN_SH:-$HOME/jamm-bench/run.sh}"
RUNROOT="${RUNROOT:-$HOME/.var/app/com.valvesoftware.Steam/data/JAMM-CGF/bench-runs}"
RESULTS_DIR="${RESULTS_DIR:-$HOME/jamm-bench/results}"
SCENE="${SCENE:-dock}"
DURATION="${DURATION:-30}"
DATE_TAG="${DATE_TAG:-$(date +%Y-%m-%d)}"
OUT_MD="$RESULTS_DIR/fish-experiments-$DATE_TAG.md"

for build in "$@"; do
  if [[ ! -f "$build" ]]; then
    echo "Error: build archive not found: $build" >&2
    exit 1
  fi
done

if [[ "$REPORT_ONLY" -eq 0 ]]; then
  if [[ ! -x "$RUN_SH" ]]; then
    echo "Error: run script not executable or missing: $RUN_SH" >&2
    exit 1
  fi
  if command -v systemctl >/dev/null 2>&1 && systemctl --user is-active --quiet jamm-bench-m1 2>/dev/null; then
    echo "Error: jamm-bench-m1.service is active; refusing to launch concurrent game runs." >&2
    exit 1
  fi
fi

EXPERIMENTS=(
  "full|--fish-mode full"
  "nostep|--fish-mode nostep"
  "nodraw|--fish-mode nodraw"
  "deactivate|--fish-mode deactivate"
  "cap-2000|--fish-cap 2000"
  "cap-1000|--fish-cap 1000"
  "cap-500|--fish-cap 500"
  "cap-200|--fish-cap 200"
)

MANIFEST=$(mktemp)
trap 'rm -f "$MANIFEST"' EXIT

for build in "$@"; do
  build_abs=$(realpath "$build")
  build_name=$(basename "$build_abs" .win)
  for spec in "${EXPERIMENTS[@]}"; do
    exp_name="${spec%%|*}"
    exp_flags="${spec#*|}"
    label="fe-${build_name}-${exp_name}"
    run_dir="$RUNROOT/${label}-${SCENE}"
    csv_path="$run_dir/jamm_bench_${SCENE}.csv"

    if [[ "$REPORT_ONLY" -eq 0 ]]; then
      # shellcheck disable=SC2086
      "$RUN_SH" "$build_abs" "$label" "$SCENE" "$DURATION" $exp_flags || true
      sleep 3
    fi

    printf '%s\t%s\t%s\t%s\n' "$build_name" "$exp_name" "$run_dir" "$csv_path" >> "$MANIFEST"
  done
done

mkdir -p "$RESULTS_DIR"

python3 - "$MANIFEST" "$OUT_MD" "$SCENE" "$DURATION" "$DATE_TAG" <<'PYEOF'
import os
import re
import sys

manifest_path, out_md, scene, duration, date_tag = sys.argv[1:6]

def parse_kv(line):
    out = {}
    for m in re.finditer(r'([A-Za-z0-9_]+)=([^\s]+)', line):
        out[m.group(1)] = m.group(2)
    return out

def fmt_ms(us_str):
    if us_str is None:
        return "-"
    try:
        v = float(us_str)
        if v < 0:
            return "-"
        return f"{v / 1000.0:.2f}"
    except ValueError:
        return "-"

def fmt_num(val_str, decimals=1):
    if val_str is None:
        return "-"
    try:
        v = float(val_str)
        if v < 0:
            return "-"
        return f"{v:.{decimals}f}"
    except ValueError:
        return "-"

rows = []
with open(manifest_path, encoding="utf-8") as mf:
    for raw in mf:
        build_name, exp_name, run_dir, csv_path = raw.rstrip("\n").split("\t")
        if not os.path.isfile(csv_path) or os.path.getsize(csv_path) == 0:
            err_path = os.path.join(run_dir, "error.txt")
            status = "CODE ERROR" if os.path.isfile(err_path) and os.path.getsize(err_path) > 0 else "MISSING"
            rows.append((build_name, exp_name, status, "-", "-", "-", "-", "-", "-", "-", "-", "-"))
            continue

        meta = {}
        with open(csv_path, encoding="utf-8", errors="replace") as cf:
            for line in cf:
                if not line.startswith("#"):
                    break
                body = line[1:].strip()
                if not body:
                    continue
                first = body.split()[0]
                kv = parse_kv(body)
                if first in ("frame_us", "loop_us", "present_sleep_us", "step_us", "draw_us", "fish_step_us", "fish_count"):
                    meta[first] = kv
                else:
                    meta.update(kv)

        avg_fps = fmt_num(meta.get("avg_fps"), 2)
        f_p95 = fmt_ms(meta.get("frame_us", {}).get("p95"))
        f_p99 = fmt_ms(meta.get("frame_us", {}).get("p99"))
        loop_mean = fmt_ms(meta.get("loop_us", {}).get("mean"))
        step_mean = fmt_ms(meta.get("step_us", {}).get("mean"))
        draw_mean = fmt_ms(meta.get("draw_us", {}).get("mean"))
        fish_step_mean = fmt_ms(meta.get("fish_step_us", {}).get("mean"))
        pres_mean = fmt_ms(
            meta.get("present_sleep_us", {}).get("mean")
            or meta.get("loop_us", {}).get("present_sleep_us_mean")
        )
        fish_count = fmt_num(
            meta.get("fish_count", {}).get("mean") or meta.get("active_fish_mean"),
            0,
        )
        rows.append((
            build_name, exp_name, "OK",
            avg_fps, f_p95, f_p99,
            loop_mean, step_mean, draw_mean, fish_step_mean, pres_mean,
            fish_count,
        ))

lines = [
    f"# Fish Experiments ({date_tag})",
    "",
    f"- Scene: `{scene}`",
    f"- Capture duration: `{duration}s`",
    "- Units: frame/loop/step/draw/fish-step/present in milliseconds (`ms`); `fish_count` is mean active `Fish` instances per frame.",
    "",
    "| Build | Experiment | Status | Avg FPS | Frame p95 (ms) | Frame p99 (ms) | Loop mean (ms) | Step mean (ms) | Draw mean (ms) | Fish step mean (ms) | Present mean (ms) | Fish count |",
    "| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |",
]
for r in rows:
    lines.append("| " + " | ".join(r) + " |")
lines.append("")

with open(out_md, "w", encoding="utf-8") as out:
    out.write("\n".join(lines))

print(f"Wrote {out_md} ({len(rows)} row(s)).")
PYEOF
