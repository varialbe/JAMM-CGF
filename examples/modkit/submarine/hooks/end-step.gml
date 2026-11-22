if (sub_diving)
{
    Girl.visible = false;
    Rod.visible = false;
    if (sub_drop > 0)
    {
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
        }
        else
        {
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
        }
    }
}

if (sub_diving || sub_cam_blend > 0.002 || sub_cam_zoom > 1.002)
{
    var zoom_target = 1.0;
    var blend_target = 0.0;
    if (sub_diving)
    {
        zoom_target = 1.55;
        blend_target = 1.0;
    }
    if (!instance_exists(Handler))
    {
        sub_cam_zoom += (zoom_target - sub_cam_zoom) * min(1, 5.0 * sub_dt);
        sub_cam_blend += (blend_target - sub_cam_blend) * min(1, 6.0 * sub_dt);
    }

    var look_target = sub_facing * 150;
    sub_cam_look_x += (look_target - sub_cam_look_x) * min(1, 5.5 * sub_dt);

    if (sub_diving)
    {
        var sub_cx, sub_cy;
        if (Lure.state != 0 || sub_drop > 0)
        {
            sub_cx = Lure.x;
            sub_cy = Lure.y;
        }
        else
        {
            sub_cx = sub_x + sub_cam_look_x + lengthdir_x(320 * sub_peek_amount, sub_light_angle);
            sub_cy = sub_y + 10 + lengthdir_y(320 * sub_peek_amount, sub_light_angle);
        }
        sub_cam_x += (sub_cx - sub_cam_x) * min(1, 8.0 * sub_dt);
        sub_cam_y += (sub_cy - sub_cam_y) * min(1, 8.0 * sub_dt);
        if (instance_exists(Camera))
        {
            Camera.x = sub_cam_x;
            Camera.y = sub_cam_y;
            Camera.atOrigin = 0;
        }
    }
    else
    {
        sub_peek_amount += (0.0 - sub_peek_amount) * min(1, 8.0 * sub_dt);
        if (instance_exists(Camera))
        {
            var van_x, van_y;
            if (Lure.state != 0 && !instance_exists(CatSwim))
            {
                van_x = Lure.x;
                van_y = Lure.y;
            }
            else
            {
                van_x = Girl.x + Camera.RELATIVE_ANCHOR_X;
                if (wasJustMoving) van_y = Girl.y + Camera.RELATIVE_ANCHOR_Y;
                else van_y = 1294 + Camera.RELATIVE_ANCHOR_Y_FROM_SEA;
            }
            sub_cam_x += (van_x - sub_cam_x) * min(1, 6.0 * sub_dt);
            sub_cam_y += (van_y - sub_cam_y) * min(1, 6.0 * sub_dt);
            Camera.x = sub_cam_x;
            Camera.y = sub_cam_y;
        }
    }

    if (!sub_diving && sub_cam_blend <= 0.004 && sub_cam_zoom <= 1.004)
    {
        sub_cam_zoom = 1;
        sub_cam_blend = 0;
        sub_peek_amount = 0;
        view_wview[0] = sub_cam_base_w;
        view_hview[0] = sub_cam_base_h;
        view_hborder[0] = sub_cam_base_hb;
        view_vborder[0] = sub_cam_base_vb;
        if (instance_exists(Camera))
        {
            view_xview[0] = clamp(Camera.x - sub_cam_base_w * 0.5, 0, max(0, room_width - sub_cam_base_w));
            view_yview[0] = clamp(Camera.y - sub_cam_base_h * 0.5, 0, max(0, room_height - sub_cam_base_h));
        }
        if (instance_exists(LightingSystem))
        {
            if (LightingSystem.width != sub_cam_base_w || LightingSystem.height != sub_cam_base_h)
            {
                LightingSystem.width = sub_cam_base_w;
                LightingSystem.height = sub_cam_base_h;
                if (surface_exists(LightingSystem.light_surface))
                    surface_free(LightingSystem.light_surface);
                LightingSystem.light_surface = surface_create(sub_cam_base_w, sub_cam_base_h);
            }
        }
    }
    else
    {
        var vw = round(sub_cam_base_w * sub_cam_zoom);
        var vh = round(sub_cam_base_h * sub_cam_zoom);
        var lw = round(sub_cam_base_w * 1.55);
        var lh = round(sub_cam_base_h * 1.55);
        view_wview[0] = vw;
        view_hview[0] = vh;
        view_hborder[0] = vw * 0.5;
        view_vborder[0] = vh * 0.5;
        if (instance_exists(Camera))
        {
            view_xview[0] = clamp(Camera.x - vw * 0.5, 0, max(0, room_width - vw));
            view_yview[0] = clamp(Camera.y - vh * 0.5, 0, max(0, room_height - vh));
        }
        // Keep the lighting surface at the zoomed dimensions across the transition to avoid per-frame reallocations.
        if (instance_exists(LightingSystem))
        {
            if (LightingSystem.width != lw || LightingSystem.height != lh)
            {
                LightingSystem.width = lw;
                LightingSystem.height = lh;
                if (surface_exists(LightingSystem.light_surface))
                    surface_free(LightingSystem.light_surface);
                LightingSystem.light_surface = surface_create(lw, lh);
            }
        }
    }
}
