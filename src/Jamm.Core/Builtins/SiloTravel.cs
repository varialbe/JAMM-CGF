namespace Jamm.ModMaker;

internal static class SiloTravel
{
    internal const string HatchCreate = """
        if (!instance_exists(obj_jamm_silo_ctrl)) instance_create(0, 0, obj_jamm_silo_ctrl);
        instance_create(x, y - 16, obj_jamm_silo_beacon);
        silo_open = 0;
        silo_near = false;
        silo_prompt = "";
        """;

    internal static string HatchStep(bool hasHandler) => $$"""
        var ctrl = instance_find(obj_jamm_silo_ctrl, 0);
        var swimmer = instance_find(CatSwim, 0);
        silo_near = swimmer != noone && point_distance(swimmer.x, swimmer.y, x, y - 32) < 110;
        silo_open = clamp(silo_open + (silo_near ? 0.13 : -0.08), 0, 1);
        silo_prompt = "";
        if (!silo_near || ctrl == noone || ctrl.silo_cooldown > 0) exit;
        {{(hasHandler ? "if (instance_exists(Handler)) exit;" : "")}}

        if (!instance_exists(Boat) || !instance_exists(Lure) || Lure.state != 0 ||
            swimmer.holding > 0 || !instance_exists(BreathBar) || BreathBar.breath <= 0)
        {
            silo_prompt = "Reel in and free your paws to dive.";
            exit;
        }
        if (!instance_exists(SaveManager) || !SaveManager.loaded ||
            SaveManager.saving_locked || SaveManager.saveflag <= 0)
        {
            silo_prompt = "Load an active save to dive.";
            exit;
        }
        silo_prompt = "[E] Enter the flooded silo";
        if (silo_open >= 1 && keyboard_check_pressed(ord("E")))
            with (ctrl) event_user(0);
        """;

    internal const string HatchDraw = """
        var lip = y - 15;
        var lid = lip - 25 * silo_open;
        var rust = make_color_rgb(102, 115, 99);
        var brass = make_color_rgb(200, 157, 96);
        var signal = merge_color(make_color_rgb(247, 158, 75), make_color_rgb(121, 226, 199), silo_open);

        draw_set_color(make_color_rgb(12, 24, 28));
        draw_rectangle(x - 32, y - 12, x + 32, y + 9, false);
        draw_ellipse(x - 38, y - 29, x + 38, y + 3, false);
        draw_set_color(rust);
        draw_ellipse(x - 35, y - 28, x + 35, y - 1, false);
        draw_set_color(make_color_rgb(17, 37, 40));
        draw_ellipse(x - 28, y - 24, x + 28, y - 5, false);
        draw_set_color(brass);
        draw_ellipse(x - 33, lid - 10, x + 33, lid + 9, false);
        draw_set_color(make_color_rgb(88, 107, 94));
        draw_ellipse(x - 31, lid - 10, x + 31, lid + 5, false);
        draw_set_color(brass);
        draw_line(x - 8, lid - 3, x + 8, lid - 3);
        draw_set_color(signal);
        draw_circle(x + 28, y - 4, 3, false);
        draw_circle(x - 28, y - 4, 3, false);
        draw_sprite_ext(spr_jamm_silo_glow, 0, x, lip, 0.9, 0.65, 0, signal, 0.73);
        draw_set_color(c_white);
        """;

    internal static string HatchGui(string font) => $$"""
        var ctrl = instance_find(obj_jamm_silo_ctrl, 0);
        var label = silo_prompt;
        if (ctrl != noone && current_time < ctrl.silo_notice_until) label = ctrl.silo_notice;
        if (label == "") exit;

        var center = display_get_gui_width() / 2;
        draw_set_font({{font}});
        draw_set_halign(fa_center);
        draw_set_valign(fa_middle);
        var half = string_width(label) / 2 + 12;
        draw_set_alpha(0.86);
        draw_set_color(make_color_rgb(10, 24, 29));
        draw_rectangle(center - half, 76, center + half, 105, false);
        draw_set_alpha(1);
        draw_set_color(make_color_rgb(190, 235, 217));
        draw_text(center, 90, label);
        draw_set_color(c_white);
        draw_set_font(-1);
        draw_set_halign(fa_left);
        draw_set_valign(fa_top);
        """;

    internal static string BeaconCreate(bool inheritsLighting) => inheritsLighting ? """
        event_inherited();
        add_blend = make_color_rgb(91, 142, 135);
        sub_blend = make_color_rgb(208, 237, 223);
        image_scale = 3.8;
        image_xscale = image_scale;
        image_yscale = image_scale;
        """ : "visible = false;";

    internal const string ControllerCreate = """
        silo_manager = noone;
        silo_nav = noone;
        silo_cooldown = 0;
        silo_visiting = false;
        silo_return_ready = false;
        silo_load_slot = -1;
        silo_music = -1;
        silo_ambient_handle = -1;
        silo_exit = 0;
        silo_earned = 0;
        silo_claimed = 0;
        silo_pending = 0;
        silo_notice = "";
        silo_notice_until = 0;
        """;

    // Write the vanilla checkpoint while all objects it reads are still present. The
    // namespaced section identifies this particular snapshot and belongs to the active slot.
    internal static string Depart(bool hasNav) => $$"""
        if (room != NewWorld || silo_visiting) exit;
        silo_manager = instance_find(SaveManager, 0);
        silo_slot = silo_manager.saveflag;
        silo_file = "sdata" + string(silo_slot) + "n_beta.ini";

        with (silo_manager) event_user(1);
        ini_open(silo_file);
        var saved_at = ini_read_real("Main", "datetime", 0);
        var has_checkpoint = saved_at > 0 && ini_read_real("Main", "saveflag", 0) == silo_slot;
        if (has_checkpoint)
        {
            silo_claimed = clamp(floor(ini_read_real("jamm_silo", "salvaged", 0)), 0, 15);
            ini_write_real("jamm_silo", "return_snapshot", saved_at);
        }
        ini_close();
        if (!has_checkpoint)
        {
            silo_notice = "The game could not save a dive checkpoint.";
            silo_notice_until = current_time + 3000;
            exit;
        }

        silo_room_persistent = room_persistent;
        silo_save_persistent = silo_manager.persistent;
        // Leave the manager with its world. Room End must not replace the checkpoint.
        silo_manager.saving_locked = true;
        silo_manager.persistent = false;
        {{(hasNav ? "silo_nav = instance_find(NavBrain, 0); if (silo_nav != noone) instance_deactivate_object(silo_nav);" : "")}}

        silo_music = Girl.background_music;
        if (!audio_is_playing(silo_music) || audio_is_paused(silo_music)) silo_music = -1;
        if (silo_music != -1) audio_pause_sound(silo_music);

        silo_visiting = true;
        silo_exit = 0;
        silo_earned = 0;
        silo_pending = 0;
        room_persistent = true;
        room_goto(room_jamm_silo);
        """;

    internal const string RoomStart = """
        if (room != NewWorld || !silo_visiting) exit;
        room_persistent = silo_room_persistent;
        silo_return_ready = true;
        """;

    internal static string BeginStep(bool hasNav) => $$"""
        if (room != NewWorld) exit;
        if (silo_cooldown > 0) silo_cooldown -= 1;

        if (silo_return_ready)
        {
            silo_return_ready = false;
            silo_visiting = false;
            silo_cooldown = 45;
            if (silo_ambient_handle != -1) audio_stop_sound(silo_ambient_handle);
            silo_ambient_handle = -1;
            if (silo_music != -1) audio_resume_sound(silo_music);
            {{(hasNav ? "if (silo_nav != noone) instance_activate_object(silo_nav);" : "")}}
            if (instance_exists(silo_manager))
            {
                silo_manager.persistent = silo_save_persistent;
                silo_manager.saving_locked = false;

                // Commit the reward and its claim bits together in the same slot.
                var total = Girl.dollars + silo_earned;
                ini_open(silo_file);
                if (silo_earned > 0)
                {
                    ini_write_real("Main", "dollars", total);
                    ini_write_real("jamm_silo", "salvaged", silo_claimed | silo_pending);
                }
                if (silo_exit != 2) ini_key_delete("jamm_silo", "return_snapshot");
                ini_close();
                Girl.dollars = total;
                if (silo_earned > 0)
                {
                    ini_open("meta.ini");
                    ini_write_real("save" + string(silo_slot), "Dollars", total);
                    ini_close();
                }

                if (silo_exit == 2)
                {
                    // The vanilla checkpoint already includes the complete world save.
                    silo_manager.saving_locked = true;
                    game_end();
                    exit;
                }
            }
            silo_notice = "Returned to the maintenance hatch";
            if (silo_exit == 3) silo_notice = "Emergency ascent. Unbanked salvage was left behind.";
            if (silo_earned > 0) silo_notice = "Salvage delivered  $" + string(silo_earned);
            silo_notice_until = current_time + 3500;
        }

        if (!instance_exists(SaveManager) || !SaveManager.loaded || SaveManager.loadflag <= 0) exit;
        var slot = SaveManager.loadflag;
        if (slot == silo_load_slot) exit;
        var hatch = instance_find(obj_jamm_silo_hatch, 0);
        if (hatch == noone || !instance_exists(Girl) || !instance_exists(Rod) ||
            !instance_exists(Boat) || !instance_exists(BreathBar) || !instance_exists(Camera)) exit;

        silo_load_slot = slot;
        ini_open("sdata" + string(slot) + "n_beta.ini");
        var resume_at = ini_read_real("jamm_silo", "return_snapshot", -1);
        var checkpoint = ini_read_real("Main", "datetime", -2);
        if (resume_at > 0 && resume_at == checkpoint) ini_key_delete("jamm_silo", "return_snapshot");
        ini_close();
        if (resume_at <= 0 || resume_at != checkpoint || instance_exists(CatSwim)) exit;

        var diver = instance_create(hatch.x, hatch.y - 62, CatSwim);
        BreathBar.breath = BreathBar.MAX_BREATH;
        Camera.x = diver.x;
        Camera.y = diver.y;
        Camera.target_x = diver.x;
        Camera.target_y = diver.y;
        Camera.atOrigin = 1;
        silo_cooldown = 45;
        silo_notice = "Dive checkpoint restored";
        silo_notice_until = current_time + 3500;
        """;
}
