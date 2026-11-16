# HUD layout API (legacy CGF)

JAMM injects this API when building archives with the legacy ExperienceBar and Quest draw events.
The right-hand column grows upwards from four pixels above the level bar. The level label,
bar sprite and fill keep their original position. Column text is right-aligned at
`view_xview[view_current] + view_wview[view_current] - 25`.

## Queue text from normal Draw

```gml
if (view_current == 0 && !Girl.hide_ui) {
    jamm_hud_set_font(fnt_babyVerySmall);
    draw_set_color(Colors.ui_light);
    jamm_hud_text(1, 0, "My mod: " + string(score), 1, 1, 0);
}
```

`jamm_hud_text(lane, kind, text, xscale, yscale, angle)` captures the evaluated text,
font, colour and alpha at the call. `kind` is 0 for ordinary transformed text, or 1
for the game's `draw_text_shadowed` (unit scale and zero angle). Use positive scales
and zero angle. The runtime measures the captured text with its font and scale,
including multiline text and the shadow's two-pixel extension. Rows have a minimum
height of 16 view pixels and a fixed four-pixel gap. Alignment is right/middle.

Lanes allocate **bottom to top**: 0 = extra level information, 1 = mod information,
2 = quests. Within each lane the order is the normal Draw call order (instance draw
order, then source call order). Therefore Oops! All Chaws! appears bottom-to-top as
FPS, X, Y, followed by quest rows. Quest completion/failure/reward labels share the
same layout as ordinary quests. Conditional or hidden rows reserve no space.

QuestEngine Draw Begin resets the queue and cursor **on every view draw pass**;
Draw End measures, reserves and draws its rows. This also runs on Smooth's render-only
frames. No logic tick, timestamp or Begin Step counter controls layout. Other views
do not enqueue or flush text. The API is for normal Draw in view 0, not Draw GUI,
Pre Draw, Draw End or manual repeated invocations of individual events.

## Low-level reservations

`jamm_hud_reserve(height[, owner])` returns the middle y coordinate of a reservation
and moves the cursor upwards by `max(0, height) + 4`. The x anchor is available as
`global.jamm_hud_x`; the last returned y is also `global.jamm_hud_y`. `owner` is accepted
for caller compatibility and is currently unused: each call allocates a new row.
Height is the full visible height in view pixels, including shadows or padding.
The initial upper limit is the level bar's top minus four pixels (fallback: view y + 452).

Prefer the queued text API: direct reservations are immediate and cannot participate
in lane sorting. A direct custom drawer must arrange its own order relative to the
queued flush and avoid sharing space before pending rows have been measured.
Do not call `jamm_hud_begin` or `jamm_hud_flush` yourself during normal play.

## Legacy state tracking and automatic adaptation

GM1.4 has no font/alignment getters. JAMM forwards calls to `draw_set_font`,
`draw_set_halign` and `draw_set_valign` through `jamm_hud_set_font`,
`jamm_hud_set_halign` and `jamm_hud_set_valign` throughout the selected archive's GML.
These wrappers preserve the setter's effect and track the state needed by the queue;
flush restores font, colour, alpha and alignment. Reserve the `jamm_hud_*` namespace
for JAMM. Calls must use the standard GML setters, rather than DnD font actions or
native extensions that change hidden drawing state.

At merge time, JAMM adapts the base quest text calls and **mod-added** normal Draw
calls with proven coordinates. Supported calls are `draw_text`, `draw_text_shadowed`
and literal-positive-scale, zero-angle `draw_text_transformed`. It accepts audited
ExperienceBar `draw_x + 380..455`, `draw_y - 1..160` offsets when both draw anchors
have standard view-relative assignments before the call, or explicit view-right
minus 5..60 and view-y plus 292..451 coordinates. The original text expression and
conditional execution remain in place; the draw operation becomes a queue operation.
Each adapted entry and count is listed in build decisions.

Unchanged base text, sprites, GUI elements, unknown coordinate aliases, computed
scales, rotations and unsupported text functions stay unchanged. New mods should
call the queue API explicitly instead of relying on these intentionally narrow rules.
The column does not shrink fonts, wrap long lines or paginate an excessive number
of quests: sufficiently many rows can extend above the view. In-game visual testing
remains necessary for new fonts, shader effects and custom HUD elements.
