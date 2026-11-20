if (!silo_ready) {
    if (!instance_exists(SaveManager) || !SaveManager.loaded) exit;
    hatch = instance_nearest(x, y, MissileSilo);
    if (!instance_exists(hatch) || hatch.x != x || hatch.y != y) exit;
    silo_save = "sdata" + string(SaveManager.loadflag) + "n_beta.ini";
    if (file_exists(silo_save)) {
        ini_open(silo_save);
        silo_written = ini_read_real("JAMM_Areas", "jamm-silo:launch-shaft", 0) == 1;
        ini_close();
    }
    silo_ready = true;
    silo_progress = silo_written;
    silo_glow = silo_written;
    hatch.sprite_index = silo_written ? jmk_jamm_silo_recipe_open_silo : spr_silo;
    hatch.mask_index = silo_written ? jmk_jamm_silo_recipe_open_silo : spr_silomask;
    hatch.image_index = 0;
    hatch.image_speed = 0;
    hatch.canOpen = silo_written;
}
if (!instance_exists(hatch)) exit;

if (silo_progress == 0 && !silo_opening && instance_exists(Lure) && Lure.state == 2) {
    if (point_distance(Lure.x, Lure.y, hatch.x, hatch.y) <= 250) {
        silo_opening = true;
        hatch.sprite_index = jmk_jamm_silo_recipe_open_silo;
    }
}
if (silo_opening) {
    // Limit a stalled frame so transition states still render on slower machines.
    silo_elapsed += min(100000, delta_time) / 1000000;
    silo_progress = clamp(silo_elapsed / 1.55, 0, 1);
    var glow;
    glow = clamp((silo_elapsed - 0.62) / 1.15, 0, 1);
    silo_glow = glow * glow * (3 - 2 * glow);
    if (silo_progress == 1 && !hatch.canOpen) {
        hatch.mask_index = jmk_jamm_silo_recipe_open_silo;
        hatch.canOpen = true;
    }
    silo_opening = silo_glow < 1;
}
with (GeigerLight) {
    if (variable_instance_exists(id, "silo_glow_alpha")) image_alpha = other.silo_glow * silo_glow_alpha;
}

// Extend the actual native slot only once it has a complete save header.
if (silo_progress == 1 && !silo_written) {
    if (!file_exists(silo_save) && !SaveManager.saving_locked) {
        with (SaveManager) event_user(1);
    }
    if (file_exists(silo_save)) {
        ini_open(silo_save);
        silo_written = ini_section_exists("Main");
        if (silo_written) ini_write_real("JAMM_Areas", "jamm-silo:launch-shaft", 1);
        ini_close();
    }
}
