var current_title = jmk_harbor_town_script_title(harbor_donated);
if (current_title != "")
{
    draw_set_color(Colors.ui_dark);
    draw_set_font(fnt_babyVerySmall);
    draw_set_halign(fa_right);
    draw_set_valign(fa_top);
    draw_text(548, 24, current_title);
    draw_set_halign(fa_left);
    draw_set_color(c_white);
}
if (!harbor_menu) exit;

draw_set_alpha(1);
draw_set_color(Colors.ui_dark);
draw_roundrect(207, 76, 593, 433, false);
draw_set_color(Colors.ui_light);
draw_set_font(fnt_babySmall);
draw_set_halign(fa_left);
draw_set_valign(fa_top);
draw_text(221, 86, "Harbor Town");
draw_set_halign(fa_right);
draw_text(533, 87, jmk_harbor_town_script_format(Girl.dollars));
draw_text(580, 87, "Close");
var title = jmk_harbor_town_script_title(harbor_donated);
draw_set_halign(fa_right);
if (title != "") draw_text(533, 104, title);
for (var i = 0; i < 4; i += 1)
{
    var yy = 125 + i * 55;
    var level = harbor_level[i];
    var price = jmk_harbor_town_script_cost(harbor_base[i], level);
    var tint = i == harbor_selected ? c_white : Colors.ui_light2;
    draw_sprite_stretched_ext(spr_rowcard, 0, 218, yy, 364, 52, tint, 1);
    draw_set_color(Colors.ui_dark);
    draw_set_font(fnt_babySmall);
    draw_set_halign(fa_left);
    draw_text(230, yy + 4, harbor_name[i] + "   Lv " + string_format(level, 0, 0));
    draw_set_font(fnt_babyVerySmall);
    var effect = "A gilded dock landmark";
    if (i == 0) effect = "+" + string(floor(100 * jmk_harbor_town_script_bonus(level))) + "% lighthouse radius";
    if (i == 1) effect = level > 0 ? string(harbor_fish_count) + " / 6 caught species on display" : "Display up to six caught species";
    if (i == 3) effect = "+" + string_format(100 * jmk_harbor_town_script_research(level, 0), 2, 1) + "% sale, +" + string_format(100 * jmk_harbor_town_script_research(level, 1), 2, 1) + "% bite, +" + string_format(100 * jmk_harbor_town_script_research(level, 2), 2, 1) + "% lure luck";
    draw_text(230, yy + 25, effect);
    draw_set_halign(fa_right);
    draw_text(490, yy + 7, jmk_harbor_town_script_format(price));
    draw_set_color(Colors.ui_dark);
    draw_roundrect(496, yy + 7, 573, yy + 46, false);
    draw_set_color(Girl.dollars >= price ? Colors.ui_light : Colors.ui_dark2);
    draw_set_halign(fa_center);
    draw_set_valign(fa_middle);
    draw_text(534, yy + 26, level == 0 ? "Build" : "Upgrade");
    draw_set_valign(fa_top);
}
var next_donation = 1000000;
if (harbor_donated >= 1000000) next_donation = 1000000000;
if (harbor_donated >= 1000000000) next_donation = 1000000000000;
if (harbor_donated >= 1000000000000) next_donation = 1000000000000000;
var donate_cost = max(0, next_donation - harbor_donated);
draw_set_color(Colors.ui_dark);
draw_roundrect(218, 351, 582, 395, false);
if (harbor_selected == 4)
{
    draw_set_color(Colors.ui_light);
    draw_roundrect(218, 351, 582, 395, false);
}
draw_set_color(Colors.ui_light);
draw_set_font(fnt_babyVerySmall);
draw_set_halign(fa_left);
draw_text(230, 358, "Donated " + jmk_harbor_town_script_format(harbor_donated) + "  " + (title == "" ? "Donate to earn titles and dock decor" : title));
draw_set_halign(fa_right);
draw_text(490, 358, "Next " + jmk_harbor_town_script_format(donate_cost));
draw_set_color(Girl.dollars > 0 ? Colors.ui_light : Colors.ui_dark2);
draw_set_halign(fa_center);
draw_text(534, 376, "Donate");
draw_set_halign(fa_left);
var footer = harbor_status;
if (footer == "") footer = gamepad_is_connected(0) ? "D-pad to select   A to buy/donate   B to close" : "Up / Down to select   Space to buy   D to donate";
draw_text(221, 405, footer);
draw_set_color(c_white);
