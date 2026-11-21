#!/usr/bin/env bash
# autoplay.sh <data.win> <label> <minutes> [seed]
# Autoplay crash hunter for the VPS (Xvfb :99 + Wine + Steam). Instruments the archive with the
# `autoplay` bench scene, starts CGF and plays it with real keyboard input (seeded, reproducible action
# sequence). Detects Code Error windows and stores error text, screenshot, action log and state CSV.
# Outcome line goes to ~/jamm-night/autoplay/status.tsv: label seed minutes outcome first-error-line.
set -uo pipefail
src=$(realpath "$1"); label=$2; minutes=${3:-10}; seed=${4:-$RANDOM}
export DISPLAY=:99 WINEPREFIX=$HOME/jamm-vm/prefix WINEDEBUG=-all WINEDLLOVERRIDES="mscoree,mshtml=;winemenubuilder.exe=d" SteamAppId=343780 SteamGameId=343780
export PATH=$HOME/.dotnet:$PATH
N=$HOME/jamm-night
MAKER=${JAMM_MODMAKER_DLL:-$HOME/jamm-wt/autoplay/tools/Jamm.ModMaker/bin/Release/net10.0/jamm-modmaker.dll}
out=$N/autoplay/$label-$seed; rm -rf "$out"; mkdir -p "$out/game" "$out/shots"
L=$HOME/cgf-files/legacy/2023-08-16_manifest-9039707068725406600
save="$WINEPREFIX/drive_c/users/varialbe/AppData/Local/Cat_Goes_Fishing"
exec 9>"$N/game-run.lock"
flock -w 3600 9 || { echo "$label game runner busy"; exit 18; }
for w in $(seq 1 1200); do pgrep -f '^[A-Z]:.*Cat Goes Fishing.exe' >/dev/null || break; sleep 1; done
pgrep -f '^[A-Z]:.*Cat Goes Fishing.exe' >/dev/null && { echo "$label GAME ALREADY RUNNING"; exit 18; }
find "$L" -mindepth 1 -maxdepth 1 ! -name data.win -exec cp -r -t "$out/game" {} +
dotnet "$MAKER" bench "$src" "$out/game/data.win" --scene autoplay > "$out/instrument.log" 2>&1 || { echo "$label INSTRUMENTATION FAILED"; exit 2; }
[[ -d "$save.orig" ]] || mkdir -p "$save.orig"
rm -rf "$save"; cp -r "$save.orig" "$save"
[[ -e $N/.steam-dirty ]] && { $N/bin/steam-restart.sh; rm -f $N/.steam-dirty; }
( cd "$out/game" && exec $HOME/opt/wine/bin/wine "Cat Goes Fishing.exe" ) > "$out/game.log" 2>&1 &
job=$!
RNG=$seed
# rnd N -> R in [0,N). Not called in a subshell, so the generator state persists.
rnd(){ RNG=$(( (RNG * 1103515245 + 12345) % 2147483648 )); R=$(( (RNG / 65536) % $1 )); }
pick(){ rnd $#; shift $R; P=$1; }
act(){ echo -e "$(date +%s)\t$step\t$*" >> "$out/actions.tsv"; }
win(){ wmctrl -l | awk '/Cat Goes Fishing$/{print $1; exit}'; }
key(){ xdotool key --delay 60 "$@"; }
hold(){ xdotool keydown "$1"; sleep "$2"; xdotool keyup "$1"; }
# The save menu has no Esc close; click its Back button (relative to the game window).
back_button(){ local w; w=$(win); [[ -n $w ]] || return; eval $(xdotool getwindowgeometry --shell "$w")
  xdotool mousemove $((X + WIDTH * 92 / 100)) $((Y + HEIGHT * 81 / 100)) click 1; sleep 1; xdotool mousemove $((X + WIDTH * 70 / 100)) $((Y + HEIGHT * 35 / 100)); }
type_console(){ key F12; sleep 1.5; xdotool type --delay 90 "$1"; sleep 0.3; key Return; sleep 1.5; key F12; }
outcome=PLAYED; step=0
# wait for the window and the probe's first state line
for i in $(seq 1 240); do [[ -n $(win) ]] && break; kill -0 $job 2>/dev/null || break; sleep 1; done
sleep 25
end=$(( $(date +%s) + minutes * 60 ))
while [[ $(date +%s) -lt $end ]]; do
  step=$((step+1))
  if wmctrl -l | grep -q 'Code Error'; then outcome=CRASH; break; fi
  if ! kill -0 $job 2>/dev/null; then outcome=EXITED; break; fi
  w=$(win); [[ -n $w ]] && xdotool windowactivate --sync "$w" 2>/dev/null
  rnd 100; r=$R
  if   (( r < 28 )); then rnd 7; h=0.$((3+R)); rnd 15; t=$((3+R)); rnd 18; rl=$((4+R)); act "cast hold=$h wait=$t reel=$rl"; hold space $h; sleep $t; hold space $rl; key s
  elif (( r < 36 )); then act "sell"; key s
  elif (( r < 46 )); then pick a d; k=$P; rnd 6; t=$((1+R)); act "boat $k $t"; hold $k $t
  elif (( r < 52 )); then act "dive"; key q; sleep 1; for j in 1 2 3; do pick Up Down Left Right; k=$P; rnd 3; hold $k $((1+R)); done
  elif (( r < 57 )); then pick b h l r; k=$P; act "buy-lure $k"; key $k
  elif (( r < 61 )); then pick z x; k=$P; act "rod $k"; key $k
  elif (( r < 65 )); then act "save-menu"; key Escape; sleep 3; back_button
  elif (( r < 90 )); then
    rnd 900; m=$((1+R)); rnd 90; g=$((1+R)); rnd 40; lv=$((10+R))
    pick "money ${m}000000" "money +${g}000000000" "unlockgear" "unlockall" "level $lv" "catalog all" "weather rainy" "weather fog" "weather clear" "weather windy" "weather super-sunny" "help"; c=$P
    act "console $c"; type_console "$c"
  else rnd 8; t=$((2+R)); act "idle $t"; sleep $t; fi
  (( step % 10 == 0 )) && ffmpeg -loglevel error -f x11grab -video_size 1280x800 -i :99 -frames:v 1 -y "$out/shots/step$(printf %04d $step).png"
done
first=""
if [[ $outcome == CRASH ]]; then
  sleep 1; ffmpeg -loglevel error -f x11grab -video_size 1280x800 -i :99 -frames:v 1 -y "$out/error.png"
  id=$(wmctrl -l | awk '/Code Error/{print $1; exit}'); eval $(xdotool getwindowgeometry --shell "$id"); xdotool mousemove $((X+474)) $((Y+497)) click 1; sleep 0.7
  xclip -o -selection clipboard > "$out/error.txt" 2>/dev/null
  first=$(grep -m1 -E "Unable|not set|Variable|index|FATAL" "$out/error.txt" | tr '\t' ' ' | cut -c1-200)
fi
ffmpeg -loglevel error -f x11grab -video_size 1280x800 -i :99 -frames:v 1 -y "$out/final.png"
for id in $(wmctrl -l | awk '/Cat Goes Fishing$|Code Error$/{print $1}'); do wmctrl -ic "$id"; done
for k in $(seq 1 15); do pgrep -f '^[A-Z]:.*Cat Goes Fishing.exe' >/dev/null || break; sleep 1; done
if pgrep -f '^[A-Z]:.*Cat Goes Fishing.exe' >/dev/null; then pkill -f '^[A-Z]:.*Cat Goes Fishing.exe'; sleep 2; touch $N/.steam-dirty; fi
cp "$save/jamm_autoplay.csv" "$out/" 2>/dev/null
if [[ $outcome == EXITED ]] && grep -q 'SteamInit failed' "$out/game.log"; then
  touch $N/.steam-dirty; outcome=STEAM
  if [[ -z "${AP_RETRIED:-}" ]]; then rm -rf "$out/game"; exec 9>&-; AP_RETRIED=1 exec "$0" "$@"; fi
fi
rm -rf "$out/game"
echo -e "$label\t$seed\t$minutes\t$outcome\t$step\t$first" | tee -a $N/autoplay/status.tsv
