if (!silo_opening || !instance_exists(hatch)) exit;
var travel, opacity, angle, dx, dy, i, age, side;
travel = silo_progress * silo_progress * (3 - 2 * silo_progress);
opacity = 1 - clamp((travel - 0.78) / 0.22, 0, 1);
angle = hatch.image_angle;

// The door sprites share the original origin. Move along the relocated hatch's local axes.
for (i = 0; i < 2; i += 1) {
    side = 2 * i - 1;
    dx = side * travel * 160 * hatch.image_xscale;
    dy = -travel * 21 * hatch.image_yscale;
    draw_sprite_ext(jmk_jamm_silo_recipe_silo_doors, i,
        x + lengthdir_x(dx, angle) + lengthdir_x(dy, angle - 90),
        y + lengthdir_y(dx, angle) + lengthdir_y(dy, angle - 90),
        hatch.image_xscale, hatch.image_yscale, angle + side * travel * 8, c_white, opacity);
}

// A short bubble puff rises from the opening as the shutters pull away.
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
