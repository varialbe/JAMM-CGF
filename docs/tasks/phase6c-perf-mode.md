# Phase 6c — opt-in performance mode, MSAA default, review fixes (implementer: Codex)

Read AGENTS.md, `docs/reports/phase6a.md` and `docs/reports/phase6b.md` first. Work in
`tools/Jamm.ModMaker/`. Do not commit, push or change git config; the orchestrator commits.

## Why
In-game (orchestrator, real files): the merged build (Abyss, Cave, Colour, HD, Perf, Smooth) still needs
~29 ms per logic tick under Wine. Smooth therefore stays in bypass and the user gets no extra frames.
For hitch-free 120 FPS a tick frame must fit in ~8 ms. Phase 6a rightly refused behaviour-changing
optimizations. The user has now explicitly asked for an **opt-in "performance mode"** that may change
behaviour of things the player cannot currently see, as long as it is documented and switchable.

## Tasks
1. **Performance mode in the Perf mod** (`[Perf] Mode=1` in `jamm_perf.ini`, default ON, toggle key F8
   with a short on-screen toast like the Smooth mod's; Mode=0 must give exactly the 6a behaviour):
   - Throttle far, already-paused standard fish: run their per-tick logic only every N-th tick
     (N configurable, default 4) with the elapsed ticks accounted for where the game counts ticks
     (e.g. frames_since_last_flip, alarms if any are used by these fish), or skip it entirely when
     provably idle. "Far/paused": zoned (state 12) or `isVisible == 0` (fish_standard_step12 paused
     them), and farther than a safe distance from Lure, CatSwim, camera views, Boat and Net.
   - Explicit exceptions (never throttled): hooked/netted/fleeing/special states, `always_zoned`,
     netherFish, bosses/specials (Dragon, Noctis, Torby, Ghost, Abyssal, Kingfish, Shaku, Bagu, Curby,
     ... check the code for creatures with their own Step/user events doing more than standard fish).
   - Must stay correct: catalog/live counts, night departure/return when the fish comes into view
     (resync state when a throttled fish becomes near), respawn (current_time based), FishThinner,
     zoning toggles (FishThinner uses `with (Fish)` — throttled fish must still be visible to it), save data.
   - Implementation approach is yours (e.g. a per-fish early-exit at the top of Fish_Step_2 with a tick
     phase, or deactivation — but NEVER deactivate instances other code iterates or reads; see the 6a
     constraints). Keep patches line-level/narrow for JAMM's three-way merge.
   - Measure offline what you can (how many fish are eligible in the NewWorld census at the start
     position) and design the debug output so the orchestrator can measure tick ms with Mode 0/1.
2. **HD**: default MSAA 4x instead of 8x (ini still allows 8). Keep everything else.
3. **Review fixes** (Gemini review of 6a): reject a positional output that starts with `--` and check for a
   missing output before path comparisons; normalize `\r\n` in decompiled text before string matching;
   for the guarded `if` patches, assert that no `else` follows the guarded statement (fail with a clear
   message otherwise).

## Rules
Same as 6a/6b: outputs only to `out/phase6c/` (gitignored), never touch the original game files
(`/run/media/varialbe/Festplatte 2/Games/CatGoesFishing-legacy/2023-08-16_manifest-9039707068725406600/data.win`,
read-only), 0 warnings, produced files pass `cgfmm verify`, WHY-comments for every game quirk.
Report in `docs/reports/phase6c.md`: exact throttling rules, exceptions, every behavioural difference
in Mode 1, expected savings, and what the orchestrator must check in game.
