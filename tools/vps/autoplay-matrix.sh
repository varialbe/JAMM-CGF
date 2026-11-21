#!/usr/bin/env bash
# autoplay-matrix.sh <minutes> <seeds> <label=win> [label=win ...]
# Runs the autoplay hunter for each build and seed (RAM guard: > 2.5 GB available), then writes a summary.
set -uo pipefail
minutes=$1; seeds=$2; shift 2
S=$HOME/jamm-night/autoplay/summary-$(date +%Y-%m-%d).md
for spec in "$@"; do label=${spec%%=*}; win=${spec#*=}
  for s in $(seq 1 $seeds); do
    seed=$(( 1000 * s + ${#label} ))
    until (( $(awk '/MemAvailable/{print int($2/1024)}' /proc/meminfo) > 2500 )); do sleep 30; done
    $HOME/jamm-night/bin/autoplay.sh "$win" "$label" "$minutes" "$seed"
  done
done
{
  echo "# Autoplay summary $(date '+%Y-%m-%d %H:%M')"; echo
  echo "| Build | Seed | Minutes | Outcome | Actions | First error line |"; echo "|---|---:|---:|---|---:|---|"
  awk -F'\t' '{printf "| %s | %s | %s | %s | %s | %s |\n",$1,$2,$3,$4,$5,$6}' $HOME/jamm-night/autoplay/status.tsv
} > "$S"
echo "$S"
