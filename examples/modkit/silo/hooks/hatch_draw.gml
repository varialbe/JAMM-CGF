if (!instance_exists(hatch)) exit;
var travel, angle, dx, dy;
travel = silo_progress * silo_progress * (3 - 2 * silo_progress);
angle = hatch.image_angle;

// The plate slides into the left recess along the hatch's local X axis.
dx = -270 * travel * hatch.image_xscale;
draw_sprite_ext(jmk_jamm_silo_recipe_silo_door, 0,
    hatch.x + lengthdir_x(dx, angle), hatch.y + lengthdir_y(dx, angle),
    hatch.image_xscale, hatch.image_yscale, angle, hatch.image_blend, hatch.image_alpha);

// Draw the lip after the fish and before LightingSystem so it shares the body's lighting.
draw_sprite_ext(jmk_jamm_silo_recipe_silo_rim, 0, hatch.x, hatch.y,
    hatch.image_xscale, hatch.image_yscale, angle, hatch.image_blend, hatch.image_alpha);

if (!silo_opening) exit;
var i, age;
// A short bubble puff rises from the opening as the plate retracts.
draw_set_color(make_color_rgb(157, 187, 163));
for (i = 0; i < 14; i += 1) {
    age = (silo_elapsed - 0.21 - i * 0.035) / 0.86;
    if (age <= 0 || age >= 1) continue;
    dx = (60 + sin(i * 2.4) * (18 + 23 * age)) * hatch.image_xscale;
    dy = 27 * hatch.image_yscale;
    draw_set_alpha(0.45 * sin(pi * age));
    draw_circle(x + lengthdir_x(dx, angle) + lengthdir_x(dy, angle - 90) + 10 * sin(age * 5 + i),
        y + lengthdir_y(dx, angle) + lengthdir_y(dy, angle - 90) - 135 * age, 2 + (i mod 4) + age * 2, true);
}
draw_set_alpha(1);
draw_set_color(c_white);
