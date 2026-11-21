#!/usr/bin/env bash
# autoplay-bisect.sh <label> <seed> <minutes> <mod1.win,mod2.win,...>
# After a crash: drop one mod at a time (linear reduction) and replay the same seed. A mod stays out when the
# crash still happens without it. Prints the smallest crashing order. Crashes are timing dependent, so treat the
# result as a strong hint, not proof.
set -uo pipefail
label=$1; seed=$2; minutes=$3; IFS=',' read -ra mods <<< "$4"
export PATH=$HOME/.dotnet:$PATH
L=$HOME/cgf-files/legacy/2023-08-16_manifest-9039707068725406600/data.win
CLI=${JAMM_CLI_DLL:-$HOME/JAMM-CGF/src/Jamm.Cli/bin/Release/net10.0/cgfmm.dll}
W=$HOME/jamm-night/autoplay/bisect-$label-$seed; mkdir -p $W
crashes(){ local order; order=$(IFS=','; echo "$*"); local out=$W/try-$(echo "$order" | md5sum | cut -c1-8).win
  dotnet "$CLI" build --base "$L" --order "$order" --out "$out" > "$out.log" 2>&1 || { echo "build failed: $order" >&2; return 2; }
  local line; line=$($HOME/jamm-night/bin/autoplay.sh "$out" "bisect-$label" "$minutes" "$seed" | tail -1); rm -f "$out"
  echo "$line" >> $W/log.tsv; [[ $(cut -f4 <<< "$line") == CRASH ]]; }
keep=("${mods[@]}")
for m in "${mods[@]}"; do
  trial=(); for k in "${keep[@]}"; do [[ $k == "$m" ]] || trial+=("$k"); done
  (( ${#trial[@]} == 0 )) && break
  if crashes "${trial[@]}"; then keep=("${trial[@]}"); echo "without $(basename $m): still crashes -> dropped" | tee -a $W/result.txt
  else echo "without $(basename $m): no crash -> needed" | tee -a $W/result.txt; fi
done
echo "Smallest crashing set: $(for k in "${keep[@]}"; do basename $k; done | tr '\n' ' ')" | tee -a $W/result.txt
