if (sprite_index != spr_jmk_ocean_underlay)
{
    draw_self();
    exit;
}

// Repeat the stock material in the tall solid section. Draw only the visible rows.
if (x + 512 < view_xview[view_current] || x > view_xview[view_current] + view_wview[view_current]) exit;
var top = max(y, view_yview[view_current]);
var bottom = min(room_height + 2, view_yview[view_current] + view_hview[view_current]);
for (var row = y + max(0, floor((top - y) / 256)) * 256; row < bottom; row += 256)
{
    draw_sprite_part_ext(sprite_index, 0, 0, 0, 512, min(256, room_height + 2 - row),
                        x, row, 1, 1, image_blend, 1);
}
