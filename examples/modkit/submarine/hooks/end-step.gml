if (!sub_diving) exit;
Girl.visible = false;
Rod.visible = false;
if (sub_drop <= 0) exit;

// The legacy reel retrieves a line shorter than 100 units. Pay out 150 before handing it over.
var paid = sub_drop - 1;
with (Lure)
{
    x = other.sub_port_x + 0.6 * paid * other.sub_facing;
    y = other.sub_port_y + 0.8 * paid;
    x_previous = x;
    y_previous = y;
    speed = 0;
    spd = 0;
    tether_distance = paid;
    tether_direction = point_direction(tether_x, tether_y, x, y);
}
if (paid < sub_drop_length)
{
    sub_drop = min(1 + sub_drop_length, sub_drop + 260 * sub_dt);
    exit;
}

sub_drop = 0;
Rod.state = 3;
with (Lure)
{
    cast_begin();
    in_the_water = true;
    near_the_water = true;
    rise_speed = 0;
    tether_length = tether_distance;
    cast_distance = x - tether_x;
    state = 2;
    if (Rod.upgrades[15]) instance_create(x, y, CastShield);
    if (hookedFishSize > 0 && instance_exists(fish))
    {
        if (hookedFishSize >= 3)
            lure_fish_go_free();
        else if (fish.object_index != Bombat && Worm.state != 3)
            lure_fish_to_bait();
    }
}
