# Task: gameplay showcase videos for Harbor Town and Submarine

The user wants videos of both mods showing how they look in real gameplay. Recording works locally:
`REC=1 MM=<modmaker> ~/jamm-bench/run.sh <build.win> <label> <scene> <sec>` records the game window to
`~/.var/app/com.valvesoftware.Steam/data/JAMM-CGF/bench-runs/<label>-<scene>/video.mp4` (x11grab, full
3440x1440 screen when the game goes fullscreen; the 5:3 game area was `crop=2400:1440:520:0`). Audio goes to a null sink.
Build used: `~/jamm-bench/videos.sh` (JAMM builtins + submarine + harbor-town, tools in ~/jamm-bench/tools/cli-now, mm-now).

Problems with the existing bench scenes (they are for verification, not for showing):
- `--scene harbor` only runs console commands at the dock; the Harbor Town buildings near the Cliff are never on screen
  and most of the video is black. Its PNG screenshots land in the save dir and are wiped.
- `--scene submarine` is good, but the final surfacing stage hits `timeout_24` (ascent takes > 30 s) — check if this is
  a real submarine bug (ascent speed / stuck at ~985 depth) or only a scene timeout; fix the right thing.

## Job
1. Add `--scene showcase-harbor` and `--scene showcase-submarine` to tools/Jamm.ModMaker (new files, follow
   SubmarineScene/HarborScene style). They play like a human: harbor = walk the cat from the dock to the Harbor Town
   sign, camera follows, open the menu with the real input path (E), show the cards, buy/upgrade each building a few
   times (give money via the harbor/money command first), close menu, walk past the buildings so they are on screen,
   then raise levels (1 → 5 → 20 → 100) with pauses so the visual growth is visible. Submarine = buy in boat shop
   (show the shop card), sail east, dive slowly, headlight toggle, cast, catch, show oxygen HUD, surface completely.
   Pace for video: ~60–90 s each, no frantic teleports, keep the HUD readable.
2. A script `tools/bench/record-showcase.sh` that builds (or takes) the archive, runs both scenes with REC=1 via
   ~/jamm-bench/run.sh, auto-crops black borders (ffmpeg cropdetect), scales to 1280 px wide, encodes H.264
   (crf ~24, faststart, no audio) to `~/jamm-bench/videos/<scene>.mp4`, and writes a small contact-sheet PNG.
3. Record both and look at the contact sheets yourself; iterate until the mods are clearly visible.
4. Tests for the new scenes (compile on synthetic game like the existing bench tests). Core tests green.
5. Report docs/tasks/showcase-scenes-report.md. Commit on exp/showcase-scenes. Do not merge or push.
Rules: bench/record only via ~/jamm-bench/run.sh (global lock). No TGE/PWB in these builds. Never touch CGF-Personal.
If external/UndertaleModTool is empty in the worktree, use a copy of /tmp/jamm-perffix-dependency.targets with paths adjusted.
