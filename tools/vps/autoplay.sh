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
csv_file="$save/jamm_autoplay.csv"
exec 9>"$N/game-run.lock"
flock -w 3600 9 || { echo "$label game runner busy"; exit 18; }
game_running(){
  local pid state
  while read -r pid; do
    state=$(ps -o stat= -p "$pid" 2>/dev/null | awk '{print $1}')
    [[ -n $state && $state != Z* ]] && return 0
  done < <(pgrep -f '^[A-Z]:.*Cat Goes Fishing.exe' || true)
  return 1
}
for _ in $(seq 1 1200); do game_running || break; sleep 1; done
game_running && { echo "$label GAME ALREADY RUNNING"; exit 18; }
find "$L" -mindepth 1 -maxdepth 1 ! -name data.win -exec cp -r -t "$out/game" {} +
dotnet "$MAKER" bench "$src" "$out/game/data.win" --scene autoplay > "$out/instrument.log" 2>&1 || { echo "$label INSTRUMENTATION FAILED"; exit 2; }
[[ -d "$save.orig" ]] || mkdir -p "$save.orig"
rm -rf "$save"; cp -r "$save.orig" "$save"
[[ -e "$N/.steam-dirty" ]] && { "$N/bin/steam-restart.sh"; rm -f "$N/.steam-dirty"; }
steam_ready(){
  local last
  local log="$WINEPREFIX/drive_c/Program Files (x86)/Steam/logs/connection_log.txt"
  last=$(grep -aoE '\[(Logged On|Logged Off|Logging On|Logging Off|Connecting|Connected)[^]]*\]' "$log" 2>/dev/null | tail -1)
  [[ $last == "[Logged On"* ]]
}
steam_ok=0
for _ in $(seq 1 90); do
  if steam_ready; then steam_ok=1; break; fi
  sleep 2
done
if (( steam_ok == 0 )); then
  printf '%s\t%s\t%s\tSTEAM\t0\tSteam did not reconnect after restart\n' "$label" "$seed" "$minutes" | tee -a "$N/autoplay/status.tsv"
  exit 0
fi
( cd "$out/game" && exec "$HOME/opt/wine/bin/wine" "Cat Goes Fishing.exe" ) > "$out/game.log" 2>&1 &
job=$!
RNG=$seed
# rnd N -> R in [0,N). Not called in a subshell, so the generator state persists.
rnd(){ RNG=$(( (RNG * 1103515245 + 12345) % 2147483648 )); R=$(( (RNG / 65536) % $1 )); }
pick(){ rnd $#; shift "$R"; P=$1; }
act(){ printf '%s\t%s\t%s\n' "$(date +%s)" "$step" "$*" >> "$out/actions.tsv"; }
win(){ wmctrl -l | awk '/Cat Goes Fishing$/{print $1; exit}'; }
key(){ xdotool key --delay 60 "$@"; }
hold(){ xdotool keydown "$1"; sleep "$2"; xdotool keyup "$1"; }
dismiss_wine_popup(){
  local id
  id=$(wmctrl -l | awk '/Program Error/{print $1; exit}')
  if [[ -n $id ]]; then
    act "dismiss-wine-helper-popup"
    wmctrl -ic "$id" 2>/dev/null || true
    sleep 0.5
  fi
}
check_alive(){
  if wmctrl -l | grep -q 'Code Error'; then outcome=CRASH; return 1; fi
  local state
  state=$(ps -o stat= -p "$job" 2>/dev/null | awk '{print $1}')
  [[ $state == Z* ]] && { outcome=EXITED; return 1; }
  if ! kill -0 "$job" 2>/dev/null; then outcome=EXITED; return 1; fi
  return 0
}
# The save menu has no Esc close; click its Back button (relative to the game window).
back_button(){
  read_probe >/dev/null || true
  if [[ $p_cancel_icon != "-" ]]; then
    click_norm "$p_cancel_icon" || true
  else
    local w; w=$(win); [[ -n $w ]] || return
    eval "$(xdotool getwindowgeometry --shell "$w")"
    xdotool mousemove $((X + WIDTH * 92 / 100)) $((Y + HEIGHT * 81 / 100))
    sleep 0.1
    xdotool mousedown 1
    sleep 0.35
    xdotool mouseup 1
  fi
  sleep 1.5
  local w; w=$(win); [[ -n $w ]] || return
  eval "$(xdotool getwindowgeometry --shell "$w")"
  xdotool mousemove $((X + WIDTH * 70 / 100)) $((Y + HEIGHT * 35 / 100))
}
click_norm(){
  local xy=$1 nx=${1%%:*} ny=${1##*:} w
  nx=${nx%$'\r'}; ny=${ny%$'\r'}
  [[ $xy == *:* && $nx =~ ^-?[0-9]+$ && $ny =~ ^-?[0-9]+$ ]] || return 1
  w=$(win); [[ -n $w ]] || return 1
  eval "$(xdotool getwindowgeometry --shell "$w")"
  xdotool mousemove $((X + WIDTH * nx / 1000)) $((Y + HEIGHT * ny / 1000))
  sleep 0.1
  xdotool mousedown 1
  sleep 0.35
  xdotool mouseup 1
  sleep 0.3
}
type_console(){
  hold F12 1
  sleep 1.5
  xdotool type --delay 90 "$1"
  sleep 0.3
  hold Return 0.5
  sleep 1.5
  hold F12 1
  sleep 1
}
read_probe(){
  p_x=0; p_y=0; p_depth=0; p_lure_state=-1; p_boat="none"; p_chunks=-1; p_weather="none"
  p_has_sub=0; p_has_weather=0; p_has_ocean=0; p_has_silo=0; p_has_console=0
  p_has_harbor=0; p_harbor_x=-1; p_harbor_y=-1; p_harbor_ready=0; p_harbor_near=0; p_harbor_menu=0
  p_boat_icon="-"; p_boat7_card="-"; p_sub_card="-"; p_boat_menu="-"; p_cancel_icon="-"; p_silo_x=-1; p_ocean_start=-1
  [[ -s $csv_file ]] || return 1
  local line
  line=$(awk -F',' '{gsub(/\r/,"")} NR>1 && NF>=32 {last=$0} END{print last}' "$csv_file" 2>/dev/null)
  [[ -n $line ]] || return 1
  local -a f
  IFS=',' read -r -a f <<< "$line"
  p_x=${f[3]:-0}; [[ $p_x =~ ^-?[0-9]+$ ]] || p_x=0
  p_y=${f[4]:-0}; [[ $p_y =~ ^-?[0-9]+$ ]] || p_y=0
  p_depth=${f[5]:-0}; [[ $p_depth =~ ^-?[0-9]+$ ]] || p_depth=0
  p_lure_state=${f[8]:--1}; [[ $p_lure_state =~ ^-?[0-9]+$ ]] || p_lure_state=0
  p_boat=${f[11]:-none}; p_boat=${p_boat:-none}
  p_chunks=${f[12]:--1}; [[ $p_chunks =~ ^-?[0-9]+$ ]] || p_chunks=0
  p_weather=${f[13]:-none}; p_weather=${p_weather:-none}
  p_has_sub=${f[14]:-0}; [[ $p_has_sub =~ ^[0-9]+$ ]] || p_has_sub=0
  p_has_weather=${f[15]:-0}; [[ $p_has_weather =~ ^[0-9]+$ ]] || p_has_weather=0
  p_has_ocean=${f[16]:-0}; [[ $p_has_ocean =~ ^[0-9]+$ ]] || p_has_ocean=0
  p_has_silo=${f[17]:-0}; [[ $p_has_silo =~ ^[0-9]+$ ]] || p_has_silo=0
  p_has_console=${f[18]:-0}; [[ $p_has_console =~ ^[0-9]+$ ]] || p_has_console=0
  p_boat_icon=${f[19]:--}; p_boat_icon=${p_boat_icon:-"-"}
  p_boat7_card=${f[20]:--}; p_boat7_card=${p_boat7_card:-"-"}
  p_sub_card=${f[21]:--}; p_sub_card=${p_sub_card:-"-"}
  p_boat_menu=${f[22]:--}; p_boat_menu=${p_boat_menu:-"-"}
  p_cancel_icon=${f[23]:--}; p_cancel_icon=${p_cancel_icon:-"-"}
  p_silo_x=${f[24]:--1}; [[ $p_silo_x =~ ^-?[0-9]+$ ]] || p_silo_x=0
  p_ocean_start=${f[25]:--1}; [[ $p_ocean_start =~ ^-?[0-9]+$ ]] || p_ocean_start=0
  p_has_harbor=${f[26]:-0}; [[ $p_has_harbor =~ ^[0-9]+$ ]] || p_has_harbor=0
  p_harbor_x=${f[27]:--1}; [[ $p_harbor_x =~ ^-?[0-9]+$ ]] || p_harbor_x=-1
  p_harbor_y=${f[28]:--1}; [[ $p_harbor_y =~ ^-?[0-9]+$ ]] || p_harbor_y=-1
  p_harbor_ready=${f[29]:-0}; [[ $p_harbor_ready =~ ^[0-9]+$ ]] || p_harbor_ready=0
  p_harbor_near=${f[30]:-0}; [[ $p_harbor_near =~ ^[0-9]+$ ]] || p_harbor_near=0
  p_harbor_menu=${f[31]:-0}; [[ $p_harbor_menu =~ ^[0-9]+$ ]] || p_harbor_menu=0
  return 0
}
dismiss_menu(){
  read_probe >/dev/null || true
  if [[ $p_cancel_icon != "-" ]]; then
    act "dismiss-menu $p_cancel_icon"
    click_norm "$p_cancel_icon" || true
    sleep 1.5
    read_probe >/dev/null || true
  fi
}
gear_unlocked=0
money_loaded=0
ensure_unlocked(){
  read_probe >/dev/null || true
  (( p_has_console == 1 )) || return 0
  if (( money_loaded == 0 )); then
    act "console-setup money"
    type_console "money 200000000"
    money_loaded=1
  fi
  if (( gear_unlocked == 0 )); then
    act "console-setup unlockgear"
    type_console "unlockgear"
    gear_unlocked=1
  fi
  sleep 2
  read_probe >/dev/null || true
}
equip_boat(){
  local target=$1
  read_probe >/dev/null || true
  [[ $p_boat == "$target" ]] && return 0
  if [[ $p_boat == "jmk_submarine_submarine" || $p_boat == "obj_jmk_submarine" ]] && (( p_depth > 15 )); then
    act "equip-$target surface-sub depth=$p_depth"
    key v
    for _ in $(seq 1 8); do
      check_alive || return 2
      sleep 1
      read_probe >/dev/null || true
      (( p_depth <= 15 )) && break
    done
  fi
  ensure_unlocked
  if (( p_lure_state != 0 && p_lure_state != -1 )); then
    hold space 3
  fi
  key s
  sleep 0.4
  read_probe >/dev/null || true
  local opened=0
  [[ $p_boat_menu == 1:* ]] && opened=1
  if (( opened == 0 )); then
    if [[ $p_boat_icon == "-" ]]; then
      act "equip-$target request-boat-menu"
      hold F11 1
    else
      click_norm "$p_boat_icon" || return 1
    fi
    for _ in $(seq 1 12); do
      check_alive || return 2
      sleep 1
      read_probe >/dev/null || true
      if [[ $p_boat_menu == 1:* ]]; then opened=1; break; fi
    done
  fi
  if (( opened == 0 )); then
    if [[ $p_cancel_icon != "-" ]]; then click_norm "$p_cancel_icon" || true; fi
    act "equip-$target skip (boat menu timeout)"
    return 1
  fi
  local card_xy menu_rest page max_page
  for _ in $(seq 1 8); do
    read_probe >/dev/null || true
    if [[ $target == "Boat7" ]]; then card_xy=$p_boat7_card; else card_xy=$p_sub_card; fi
    if [[ $card_xy == "-" ]]; then
      sleep 1.5
      read_probe >/dev/null || true
      if [[ $target == "Boat7" ]]; then card_xy=$p_boat7_card; else card_xy=$p_sub_card; fi
    fi
    if [[ $card_xy != "-" ]]; then
      click_norm "$card_xy" || return 1
      sleep 2
      read_probe >/dev/null || true
      if [[ $p_boat == "$target" ]]; then
        act "equip-$target ok"
        return 0
      fi
      if [[ $p_cancel_icon != "-" ]]; then click_norm "$p_cancel_icon" || true; fi
      act "equip-$target blocked (boat=$p_boat)"
      return 1
    fi
    menu_rest=${p_boat_menu#*:}
    page=${menu_rest%%:*}
    max_page=${menu_rest##*:}
    [[ $page =~ ^[0-9]+$ ]] || page=0
    [[ $max_page =~ ^[0-9]+$ ]] || max_page=0
    if (( page < max_page )); then
      key Down
      sleep 1
      opened=0
      for _ in $(seq 1 10); do
        check_alive || return 2
        sleep 1
        read_probe >/dev/null || true
        if [[ $p_boat_menu == 1:* ]]; then opened=1; break; fi
      done
      (( opened == 1 )) || break
    else
      break
    fi
  done
  read_probe >/dev/null || true
  if [[ $p_cancel_icon != "-" ]]; then click_norm "$p_cancel_icon" || true; fi
  act "equip-$target skip (card not found)"
  return 1
}
exercise_submarine(){
  read_probe >/dev/null || true
  (( p_has_sub == 1 )) || return 0
  act "submarine-coverage begin"
  local equip_rc=1
  for attempt in 1 2 3; do
    equip_boat jmk_submarine_submarine
    equip_rc=$?
    (( equip_rc == 2 )) && return 2
    (( equip_rc == 0 )) && break
    key Escape
    sleep 2
  done
  (( equip_rc == 0 )) || { act "submarine-coverage equip-failed"; return 0; }
  read_probe >/dev/null || true
  if (( p_x < 1800 )); then hold d 4; fi
  act "submarine-coverage dive"
  key v; sleep 1; hold Down 2; hold Left 2; hold Right 2
  act "submarine-coverage light-and-cast"
  key l; hold space 0.4; sleep 3; hold space 5
  act "submarine-coverage surface"
  key v; sleep 3
}
exercise_harbor(){
  read_probe >/dev/null || true
  (( p_has_harbor == 1 )) || return 0
  act "harbor-coverage approach x=$p_harbor_x y=$p_harbor_y ready=$p_harbor_ready"
  (( p_harbor_ready == 1 && p_harbor_x >= 0 )) || { act "harbor-coverage unavailable"; return 0; }
  if (( p_lure_state != 0 && p_lure_state != -1 )); then hold space 3; fi
  for _ in $(seq 1 30); do
    check_alive || return 2
    read_probe >/dev/null || true
    (( p_harbor_near == 1 )) && break
    local target=$((p_harbor_x + 53)) delta=$((p_harbor_x + 53 - p_x))
    if (( delta > 0 )); then hold d 1; else hold a 1; fi
    read_probe >/dev/null || true
    (( p_harbor_y < 0 || p_y < 0 )) && break
    if (( p_y < p_harbor_y - 90 || p_y > p_harbor_y + 90 )); then
      act "harbor-coverage vertical-range-miss player_y=$p_y"
      break
    fi
    (( target >= 0 )) || break
  done
  read_probe >/dev/null || true
  if (( p_harbor_near != 1 )); then
    act "harbor-coverage proximity-failed x=$p_x y=$p_y harbor=$p_harbor_x,$p_harbor_y"
    return 0
  fi
  act "harbor-coverage press-E"
  key e
  for _ in $(seq 1 12); do
    check_alive || return 2
    sleep 1; read_probe >/dev/null || true
    (( p_harbor_menu == 1 )) && break
  done
  if (( p_harbor_menu == 1 )); then
    act "harbor-coverage menu-open"
    ffmpeg -nostdin -loglevel error -f x11grab -video_size 1280x800 -i :99 -frames:v 1 -y "$out/harbor-menu.png"
    sleep 5; key Escape; sleep 1
  else
    act "harbor-coverage menu-open-failed"
  fi
}
outcome=PLAYED; step=0
# wait for the window and the probe's first state line
for _ in $(seq 1 240); do [[ -n $(win) ]] && break; kill -0 "$job" 2>/dev/null || break; sleep 1; done
sleep 25
end=$(( $(date +%s) + minutes * 60 ))
startup_setup_done=0
while [[ $(date +%s) -lt $end ]]; do
  step=$((step+1))
  check_alive || break
  dismiss_wine_popup
  w=$(win); [[ -n $w ]] && xdotool windowactivate --sync "$w" 2>/dev/null
  dismiss_menu
  if (( startup_setup_done == 0 )); then
    startup_setup_done=1
    ensure_unlocked
  fi
  if [[ ${feature_targets_done:-0} == 0 ]] && (( step >= 2 )); then
    feature_targets_done=1
    exercise_submarine
    rc=$?; (( rc == 2 )) && break
    exercise_harbor
    rc=$?; (( rc == 2 )) && break
  fi
  rnd 100; r=$R
  if (( r < 20 )); then
    rnd 7; h=0.$((3+R)); rnd 15; t=$((3+R)); rnd 18; rl=$((4+R))
    act "cast hold=$h wait=$t reel=$rl"; hold space "$h"; sleep "$t"; hold space "$rl"; key s
  elif (( r < 25 )); then
    act "sell"; key s
  elif (( r < 32 )); then
    pick a d; k=$P; rnd 6; t=$((1+R)); act "boat $k $t"; hold "$k" "$t"
  elif (( r < 37 )); then
    act "dive"; key q; sleep 1
    for _ in 1 2 3; do pick Up Down Left Right; k=$P; rnd 3; hold "$k" $((1+R)); done
  elif (( r < 41 )); then
    pick b h l r; k=$P; act "buy-lure $k"; key "$k"
  elif (( r < 45 )); then
    pick z x; k=$P; act "rod $k"; key "$k"
  elif (( r < 49 )); then
    act "save-menu"
    read_probe >/dev/null || true
    if [[ $p_cancel_icon == "-" ]]; then key Escape; sleep 3; fi
    back_button
    for _ in 1 2 3; do
      read_probe >/dev/null || true
      [[ $p_cancel_icon == "-" ]] && break
      back_button
    done
  elif (( r < 59 )); then
    rnd 900; m=$((1+R)); rnd 90; g=$((1+R)); rnd 40; lv=$((10+R))
    pick "money ${m}000000" "money +${g}000000000" "unlockgear" "unlockall" "level $lv" "help"; c=$P
    read_probe >/dev/null || true
    if (( p_has_console == 1 )); then
      act "console $c"; type_console "$c"
      [[ $c == "unlockgear" || $c == "unlockall" ]] && gear_unlocked=1
    else
      act "console skip (not installed)"
    fi
  elif (( r < 73 )); then
    rnd 3; east_legs=$((2+R)); rnd 4; go_west=$((R == 0 ? 1 : 0)); rnd 5; west_s=$((4+R))
    equip_boat Boat7
    rc=$?; (( rc == 2 )) && break
    read_probe >/dev/null || true
    if (( p_has_ocean == 1 && p_chunks <= 0 )); then
      east_legs=30
      go_west=0
    fi
    act "far-east legs=$east_legs west=$go_west($west_s) ocean=$p_has_ocean start=$p_ocean_start boat=$p_boat x=$p_x chunks=$p_chunks"
    xdotool keydown d
    prev_x=$p_x; stall=0; ocean_extra=0
    for _ in $(seq 1 "$east_legs"); do
      sleep 4
      if ! check_alive; then xdotool keyup d; break 2; fi
      if [[ $(date +%s) -ge $end ]]; then xdotool keyup d; break 2; fi
      read_probe >/dev/null || true
      if [[ $p_cancel_icon != "-" ]]; then
        xdotool keyup d
        dismiss_menu
        xdotool keydown d
      fi
      if (( p_has_ocean == 1 && p_chunks > 0 )); then
        ocean_extra=$((ocean_extra + 1))
        if (( ocean_extra >= 4 )); then break; fi
      fi
      if (( p_x == prev_x )); then
        stall=$((stall + 1))
        if (( stall >= 3 )); then break; fi
      else
        stall=0
        prev_x=$p_x
      fi
    done
    xdotool keyup d
    if (( go_west == 1 )); then
      check_alive || break
      [[ $(date +%s) -lt $end ]] || break
      hold a "$west_s"
    fi
  elif (( r < 87 )); then
    rnd 3; sub_moves=$((2+R)); rnd 2; sub_cast=$R
    read_probe >/dev/null || true
    if (( p_has_sub == 1 )); then
      equip_boat jmk_submarine_submarine
      rc=$?; (( rc == 2 )) && break
      if (( rc == 0 )); then
        read_probe >/dev/null || true
        if (( p_x < 1800 )); then
          act "submarine clear-dock x=$p_x"
          hold d 4
        fi
        act "submarine dive moves=$sub_moves cast=$sub_cast x=$p_x"
        key v
        sleep 1
        hold Down 2
        for _ in $(seq 1 "$sub_moves"); do
          check_alive || break 2
          pick Up Down Left Right; k=$P; rnd 3; hold "$k" $((1+R))
        done
        act "submarine light-toggle"
        key l
        sleep 0.5
        if (( sub_cast == 1 )); then
          act "submarine cast-reel"
          hold space 0.4; sleep 3; hold space 5
        fi
        act "submarine surface"
        key v
        sleep 2
      fi
    else
      act "submarine skip (not installed)"
    fi
  elif (( r < 91 )); then
    read_probe >/dev/null || true
    if (( p_has_silo == 1 && p_silo_x > 0 )); then
      dx=$(( p_x - p_silo_x ))
      if (( dx >= -600 && dx <= 600 )); then
        act "silo fish x=$p_x silo_x=$p_silo_x"
        hold space 0.5; sleep 6; hold space 6; key s
      else
        act "silo skip (no console teleport; x=$p_x silo_x=$p_silo_x)"
      fi
    else
      act "silo skip (not installed)"
    fi
  elif (( r < 97 )); then
    pick "weather rainy" "weather fog" "weather clear" "weather windy" "weather super-sunny" "weather auto"; wc=$P
    read_probe >/dev/null || true
    if (( p_has_weather == 1 && p_has_console == 1 )); then
      act "weather $wc (cur=$p_weather)"; type_console "$wc"
    else
      act "weather skip (weather=$p_has_weather console=$p_has_console)"
    fi
  else
    rnd 6; t=$((2+R)); act "idle $t"; sleep "$t"
  fi
  (( step % 10 == 0 )) && ffmpeg -nostdin -loglevel error -f x11grab -video_size 1280x800 -i :99 -frames:v 1 -y "$out/shots/step$(printf %04d "$step").png"
done
first=""
if [[ $outcome == CRASH ]]; then
  sleep 1; ffmpeg -nostdin -loglevel error -f x11grab -video_size 1280x800 -i :99 -frames:v 1 -y "$out/error.png"
  id=$(wmctrl -l | awk '/Code Error/{print $1; exit}'); eval "$(xdotool getwindowgeometry --shell "$id")"; xdotool mousemove $((X+474)) $((Y+497)) click 1; sleep 0.7
  xclip -o -selection clipboard > "$out/error.txt" 2>/dev/null
  first=$(grep -m1 -E "Unable|not set|Variable|index|FATAL" "$out/error.txt" | tr '\t' ' ' | cut -c1-200)
fi
ffmpeg -nostdin -loglevel error -f x11grab -video_size 1280x800 -i :99 -frames:v 1 -y "$out/final.png"
for id in $(wmctrl -l | awk '/Cat Goes Fishing$|Code Error$/{print $1}'); do wmctrl -ic "$id"; done
for _ in $(seq 1 15); do pgrep -f '^[A-Z]:.*Cat Goes Fishing.exe' >/dev/null || break; sleep 1; done
if pgrep -f '^[A-Z]:.*Cat Goes Fishing.exe' >/dev/null; then pkill -f '^[A-Z]:.*Cat Goes Fishing.exe'; sleep 2; touch "$N/.steam-dirty"; fi
cp "$save/jamm_autoplay.csv" "$out/" 2>/dev/null
if [[ $outcome == EXITED ]] && grep -q 'SteamInit failed' "$out/game.log"; then
  touch "$N/.steam-dirty"; outcome=STEAM
  if [[ -z "${AP_RETRIED:-}" ]]; then rm -rf "$out/game"; exec 9>&-; AP_RETRIED=1 exec "$0" "$@"; fi
fi
rm -rf "$out/game"
printf '%s\t%s\t%s\t%s\t%s\t%s\n' "$label" "$seed" "$minutes" "$outcome" "$step" "$first" | tee -a "$N/autoplay/status.tsv"
