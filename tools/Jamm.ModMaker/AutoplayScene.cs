using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.ModMaker;

/// <summary>
/// Bench scene for the autoplay crash hunter. The bot itself sends real keyboard input from the VPS runner
/// (tools/vps/autoplay.sh); this probe only closes the new-game menus and appends the game state to
/// <c>jamm_autoplay.csv</c> every two seconds so a crash report shows what the game was doing.
/// </summary>
internal static class AutoplayScene
{
    private const string Probe = "obj_jamm_autoplay";

    internal static bool Requested(string[] args)
    {
        int index = Array.IndexOf(args, "--scene");
        return index >= 3 && args.ElementAtOrDefault(index + 1) == "autoplay";
    }

    internal static void Apply(UndertaleData data)
    {
        if (data.Rooms.ByName("NewWorld") is null || data.GameObjects.ByName("Girl") is null)
            throw new InvalidDataException("The autoplay scene needs the legacy NewWorld room and Girl object.");
        var probe = ModObjects.Create(data, Probe, persistent: true);
        var compiler = new CompileGroup(data);
        string dismiss = string.Join('\n', new[] { "GameModeMenu", "GM_Classic", "GM_Realism", "Message_Start", "Handler", "Message" }
            .Where(name => data.GameObjects.ByName(name) is not null)
            .Select(name => $"with ({name}) instance_destroy();"));
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Create, 0u, data), """
            jamm_ap_start = -1;
            jamm_ap_next = 0;
            jamm_ap_dismissed = 0;
            var f = file_text_open_write("jamm_autoplay.csv");
            file_text_write_string(f, "ms,room,fps,x,y,depth,dollars,level,lure_state,lure_y,instances,boat,chunks,weather,has_sub,has_weather,has_ocean,has_silo,has_console,boat_icon_xy,boat7_card_xy,sub_card_xy,boat_menu,cancel_icon_xy,silo_x,ocean_start,has_harbor,harbor_x,harbor_y,harbor_ready,harbor_near,harbor_menu");
            file_text_writeln(f);
            file_text_close(f);
            """);
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.EndStep, data), $$"""
            if (jamm_ap_start < 0) jamm_ap_start = current_time;
            var jamm_ap_sm_obj = asset_get_index("SaveManager");
            var jamm_ap_loaded = 1;
            if (jamm_ap_sm_obj > -1 && asset_get_type("SaveManager") == asset_object && object_exists(jamm_ap_sm_obj))
            {
                jamm_ap_loaded = 0;
                if (instance_exists(jamm_ap_sm_obj))
                {
                    var jamm_ap_sm = instance_find(jamm_ap_sm_obj, 0);
                    if (instance_exists(jamm_ap_sm) && variable_instance_exists(jamm_ap_sm, "loaded") && jamm_ap_sm.loaded)
                        jamm_ap_loaded = 1;
                }
            }
            if (room == NewWorld && jamm_ap_loaded && !jamm_ap_dismissed && current_time - jamm_ap_start > 5000)
            {
                {{dismiss}}
                if (instance_exists(Girl) && variable_instance_exists(Girl, "startMessageShown")) Girl.startMessageShown = 1;
                jamm_ap_dismissed = 1;
            }
            if (keyboard_check_pressed(vk_f11))
            {
                with (Handler) instance_destroy();
                with (icon_Boats) event_user(0);
            }
            var jamm_ap_bmenu_obj = asset_get_index("BoatMenu");
            if (jamm_ap_bmenu_obj < 0 || asset_get_type("BoatMenu") != asset_object || !object_exists(jamm_ap_bmenu_obj))
                jamm_ap_bmenu_obj = -1;
            var jamm_ap_bmenu_open = (jamm_ap_bmenu_obj >= 0 && instance_exists(jamm_ap_bmenu_obj));
            if (current_time < jamm_ap_next) exit;
            jamm_ap_next = current_time + (jamm_ap_bmenu_open ? 500 : 2000);

            var jamm_ap_ls = -1, jamm_ap_ly = 0, jamm_ap_x = 0, jamm_ap_y = 0, jamm_ap_depth = 0, jamm_ap_d = 0, jamm_ap_lv = 0;
            if (instance_exists(Girl))
            {
                jamm_ap_x = Girl.x;
                jamm_ap_y = Girl.y;
                jamm_ap_depth = max(0, jamm_ap_y - 1294);
                if (variable_instance_exists(Girl, "dollars")) jamm_ap_d = Girl.dollars;
                if (variable_instance_exists(Girl, "level")) jamm_ap_lv = Girl.level;
            }
            var jamm_ap_lure_obj = asset_get_index("Lure");
            if (jamm_ap_lure_obj > -1 && asset_get_type("Lure") == asset_object && object_exists(jamm_ap_lure_obj) && instance_exists(jamm_ap_lure_obj))
            {
                var jamm_ap_lure = instance_find(jamm_ap_lure_obj, 0);
                if (instance_exists(jamm_ap_lure))
                {
                    if (variable_instance_exists(jamm_ap_lure, "state")) jamm_ap_ls = jamm_ap_lure.state;
                    jamm_ap_ly = jamm_ap_lure.y;
                }
            }

            var jamm_ap_sub_obj = asset_get_index("jmk_submarine_submarine");
            if (jamm_ap_sub_obj < 0 || asset_get_type("jmk_submarine_submarine") != asset_object || !object_exists(jamm_ap_sub_obj))
            {
                jamm_ap_sub_obj = asset_get_index("obj_jmk_submarine");
                if (jamm_ap_sub_obj < 0 || asset_get_type("obj_jmk_submarine") != asset_object || !object_exists(jamm_ap_sub_obj))
                    jamm_ap_sub_obj = -1;
            }
            var jamm_ap_has_sub = (jamm_ap_sub_obj >= 0) ? 1 : 0;

            var jamm_ap_boat = "none";
            var jamm_ap_boat_obj = asset_get_index("Boat");
            if (jamm_ap_boat_obj > -1 && asset_get_type("Boat") == asset_object && object_exists(jamm_ap_boat_obj) && instance_exists(jamm_ap_boat_obj))
            {
                var jamm_ap_binst = instance_find(jamm_ap_boat_obj, 0);
                if (instance_exists(jamm_ap_binst))
                {
                    jamm_ap_boat = object_get_name(jamm_ap_binst.object_index);
                    if (variable_instance_exists(jamm_ap_binst, "sub_diving") && jamm_ap_binst.sub_diving && variable_instance_exists(jamm_ap_binst, "sub_y"))
                        jamm_ap_depth = max(0, jamm_ap_binst.sub_y - 1294);
                }
            }
            else if (jamm_ap_sub_obj >= 0 && instance_exists(jamm_ap_sub_obj))
            {
                var jamm_ap_sinst = instance_find(jamm_ap_sub_obj, 0);
                if (instance_exists(jamm_ap_sinst))
                {
                    jamm_ap_boat = object_get_name(jamm_ap_sinst.object_index);
                    if (variable_instance_exists(jamm_ap_sinst, "sub_diving") && jamm_ap_sinst.sub_diving && variable_instance_exists(jamm_ap_sinst, "sub_y"))
                        jamm_ap_depth = max(0, jamm_ap_sinst.sub_y - 1294);
                }
            }

            var jamm_ap_ocean_obj = asset_get_index("obj_jmk_endless_ocean");
            if (jamm_ap_ocean_obj < 0 || asset_get_type("obj_jmk_endless_ocean") != asset_object || !object_exists(jamm_ap_ocean_obj))
                jamm_ap_ocean_obj = -1;
            var jamm_ap_has_ocean = (jamm_ap_ocean_obj >= 0) ? 1 : 0;
            var jamm_ap_chunks = -1;
            var jamm_ap_ocean_start = -1;
            if (jamm_ap_ocean_obj >= 0 && instance_exists(jamm_ap_ocean_obj))
            {
                var jamm_ap_ocean = instance_find(jamm_ap_ocean_obj, 0);
                if (instance_exists(jamm_ap_ocean))
                {
                    if (variable_instance_exists(jamm_ap_ocean, "ocean_chunks") && ds_exists(jamm_ap_ocean.ocean_chunks, ds_type_map))
                        jamm_ap_chunks = ds_map_size(jamm_ap_ocean.ocean_chunks);
                    if (variable_instance_exists(jamm_ap_ocean, "ocean_start"))
                        jamm_ap_ocean_start = round(jamm_ap_ocean.ocean_start);
                }
            }

            var jamm_ap_weather_obj = asset_get_index("obj_jamm_weather");
            if (jamm_ap_weather_obj < 0 || asset_get_type("obj_jamm_weather") != asset_object || !object_exists(jamm_ap_weather_obj))
                jamm_ap_weather_obj = -1;
            var jamm_ap_has_weather = (jamm_ap_weather_obj >= 0) ? 1 : 0;
            var jamm_ap_weather = "none";
            if (jamm_ap_weather_obj >= 0 && instance_exists(jamm_ap_weather_obj))
            {
                var jamm_ap_winst = instance_find(jamm_ap_weather_obj, 0);
                if (instance_exists(jamm_ap_winst) && variable_instance_exists(jamm_ap_winst, "weather_live") && variable_instance_exists(jamm_ap_winst, "weather_key"))
                {
                    if (jamm_ap_winst.weather_live >= 0 && jamm_ap_winst.weather_live < array_length_1d(jamm_ap_winst.weather_key))
                        jamm_ap_weather = string_replace_all(string(jamm_ap_winst.weather_key[jamm_ap_winst.weather_live]), ",", "_");
                    else
                        jamm_ap_weather = "clear";
                }
            }

            var jamm_ap_silo_obj = asset_get_index("jmk_jamm_silo_object_hatch");
            if (jamm_ap_silo_obj < 0 || asset_get_type("jmk_jamm_silo_object_hatch") != asset_object || !object_exists(jamm_ap_silo_obj))
                jamm_ap_silo_obj = -1;
            var jamm_ap_has_silo = (jamm_ap_silo_obj >= 0) ? 1 : 0;
            var jamm_ap_silo_x = -1;
            if (jamm_ap_silo_obj >= 0 && instance_exists(jamm_ap_silo_obj))
            {
                var jamm_ap_hatch = instance_find(jamm_ap_silo_obj, 0);
                if (instance_exists(jamm_ap_hatch)) jamm_ap_silo_x = round(jamm_ap_hatch.x);
            }

            var jamm_ap_harbor_obj = asset_get_index("jmk_harbor_town_object_harbor");
            if (jamm_ap_harbor_obj < 0 || asset_get_type("jmk_harbor_town_object_harbor") != asset_object || !object_exists(jamm_ap_harbor_obj))
                jamm_ap_harbor_obj = -1;
            var jamm_ap_has_harbor = (jamm_ap_harbor_obj >= 0) ? 1 : 0;
            var jamm_ap_harbor_x = -1, jamm_ap_harbor_y = -1, jamm_ap_harbor_ready = 0;
            var jamm_ap_harbor_near = 0, jamm_ap_harbor_menu = 0;
            if (jamm_ap_harbor_obj >= 0 && instance_exists(jamm_ap_harbor_obj))
            {
                var jamm_ap_harbor = instance_find(jamm_ap_harbor_obj, 0);
                if (instance_exists(jamm_ap_harbor))
                {
                    jamm_ap_harbor_x = round(jamm_ap_harbor.x);
                    jamm_ap_harbor_y = round(jamm_ap_harbor.y);
                    if (variable_instance_exists(jamm_ap_harbor, "harbor_ready")) jamm_ap_harbor_ready = jamm_ap_harbor.harbor_ready;
                    if (variable_instance_exists(jamm_ap_harbor, "harbor_near")) jamm_ap_harbor_near = jamm_ap_harbor.harbor_near;
                    if (variable_instance_exists(jamm_ap_harbor, "harbor_menu")) jamm_ap_harbor_menu = jamm_ap_harbor.harbor_menu;
                }
            }

            var jamm_ap_con_obj = asset_get_index("obj_jamm_console");
            var jamm_ap_has_console = (jamm_ap_con_obj > -1 && asset_get_type("obj_jamm_console") == asset_object && object_exists(jamm_ap_con_obj)) ? 1 : 0;

            var jamm_ap_vx = view_xview[0], jamm_ap_vy = view_yview[0], jamm_ap_vw = max(1, view_wview[0]), jamm_ap_vh = max(1, view_hview[0]);
            var jamm_ap_handler_obj = asset_get_index("Handler");
            var jamm_ap_handler_open = (jamm_ap_handler_obj > -1 && asset_get_type("Handler") == asset_object && object_exists(jamm_ap_handler_obj) && instance_exists(jamm_ap_handler_obj));

            var jamm_ap_bicon_xy = "-";
            var jamm_ap_bicon_obj = asset_get_index("icon_Boats");
            if (!jamm_ap_handler_open && jamm_ap_bicon_obj > -1 && asset_get_type("icon_Boats") == asset_object && object_exists(jamm_ap_bicon_obj) && instance_exists(jamm_ap_bicon_obj))
            {
                var jamm_ap_bi = instance_find(jamm_ap_bicon_obj, 0);
                if (instance_exists(jamm_ap_bi) && jamm_ap_bi.visible && (!variable_instance_exists(jamm_ap_bi, "unlocked") || jamm_ap_bi.unlocked > 0))
                {
                    var jamm_ap_cx = (jamm_ap_bi.bbox_right > jamm_ap_bi.bbox_left) ? (jamm_ap_bi.bbox_left + jamm_ap_bi.bbox_right) * 0.5 : jamm_ap_bi.x;
                    var jamm_ap_cy = (jamm_ap_bi.bbox_bottom > jamm_ap_bi.bbox_top) ? (jamm_ap_bi.bbox_top + jamm_ap_bi.bbox_bottom) * 0.5 : jamm_ap_bi.y;
                    var jamm_ap_nx = round(1000 * (jamm_ap_cx - jamm_ap_vx) / jamm_ap_vw);
                    var jamm_ap_ny = round(1000 * (jamm_ap_cy - jamm_ap_vy) / jamm_ap_vh);
                    if (jamm_ap_nx >= 0 && jamm_ap_nx <= 1000 && jamm_ap_ny >= 0 && jamm_ap_ny <= 1000)
                        jamm_ap_bicon_xy = string(jamm_ap_nx) + ":" + string(jamm_ap_ny);
                }
            }

            var jamm_ap_cicon_xy = "-";
            var jamm_ap_cicon_obj = asset_get_index("icon_Cancel");
            if (jamm_ap_handler_open && jamm_ap_cicon_obj > -1 && asset_get_type("icon_Cancel") == asset_object && object_exists(jamm_ap_cicon_obj) && instance_exists(jamm_ap_cicon_obj))
            {
                var jamm_ap_ci_inst = instance_find(jamm_ap_cicon_obj, 0);
                if (instance_exists(jamm_ap_ci_inst) && jamm_ap_ci_inst.visible)
                {
                    var jamm_ap_cx = (jamm_ap_ci_inst.bbox_right > jamm_ap_ci_inst.bbox_left) ? (jamm_ap_ci_inst.bbox_left + jamm_ap_ci_inst.bbox_right) * 0.5 : jamm_ap_ci_inst.x;
                    var jamm_ap_cy = (jamm_ap_ci_inst.bbox_bottom > jamm_ap_ci_inst.bbox_top) ? (jamm_ap_ci_inst.bbox_top + jamm_ap_ci_inst.bbox_bottom) * 0.5 : jamm_ap_ci_inst.y;
                    var jamm_ap_nx = round(1000 * (jamm_ap_cx - jamm_ap_vx) / jamm_ap_vw);
                    var jamm_ap_ny = round(1000 * (jamm_ap_cy - jamm_ap_vy) / jamm_ap_vh);
                    if (jamm_ap_nx >= 0 && jamm_ap_nx <= 1000 && jamm_ap_ny >= 0 && jamm_ap_ny <= 1000)
                        jamm_ap_cicon_xy = string(jamm_ap_nx) + ":" + string(jamm_ap_ny);
                }
            }

            var jamm_ap_bmenu = "-", jamm_ap_b7_xy = "-", jamm_ap_sub_xy = "-";
            if (jamm_ap_bmenu_open)
            {
                var jamm_ap_bm = instance_find(jamm_ap_bmenu_obj, 0);
                if (instance_exists(jamm_ap_bm))
                {
                    var jamm_ap_done = variable_instance_exists(jamm_ap_bm, "done_spawning") ? jamm_ap_bm.done_spawning : 1;
                    var jamm_ap_srow = variable_instance_exists(jamm_ap_bm, "scroll_row") ? jamm_ap_bm.scroll_row : 0;
                    var jamm_ap_mrow = variable_instance_exists(jamm_ap_bm, "max_scroll_row") ? jamm_ap_bm.max_scroll_row : 0;
                    jamm_ap_bmenu = string(jamm_ap_done) + ":" + string(jamm_ap_srow) + ":" + string(jamm_ap_mrow);

                    var jamm_ap_b7_obj = asset_get_index("Boat7");
                    if (jamm_ap_b7_obj < 0 || asset_get_type("Boat7") != asset_object || !object_exists(jamm_ap_b7_obj))
                        jamm_ap_b7_obj = -1;
                    var jamm_ap_bdata_obj = asset_get_index("BoatData");
                    var jamm_ap_bcard_obj = asset_get_index("BoatCard");
                    if (jamm_ap_done && jamm_ap_bdata_obj > -1 && asset_get_type("BoatData") == asset_object && object_exists(jamm_ap_bdata_obj) && instance_exists(jamm_ap_bdata_obj)
                        && jamm_ap_bcard_obj > -1 && asset_get_type("BoatCard") == asset_object && object_exists(jamm_ap_bcard_obj) && instance_exists(jamm_ap_bcard_obj))
                    {
                        var jamm_ap_bdata = instance_find(jamm_ap_bdata_obj, 0);
                        if (instance_exists(jamm_ap_bdata) && variable_instance_exists(jamm_ap_bdata, "object"))
                        {
                            var jamm_ap_bobj_len = array_length_1d(jamm_ap_bdata.object);
                            var jamm_ap_ncards = instance_number(jamm_ap_bcard_obj);
                            for (var jamm_ap_ci = 0; jamm_ap_ci < jamm_ap_ncards; jamm_ap_ci += 1)
                            {
                                var jamm_ap_card = instance_find(jamm_ap_bcard_obj, jamm_ap_ci);
                                if (!instance_exists(jamm_ap_card) || !variable_instance_exists(jamm_ap_card, "item_id")) continue;
                                var jamm_ap_iid = jamm_ap_card.item_id;
                                if (jamm_ap_iid < 0 || jamm_ap_iid >= jamm_ap_bobj_len) continue;
                                var jamm_ap_tobj = jamm_ap_bdata.object[jamm_ap_iid];
                                if ((jamm_ap_b7_obj >= 0 && jamm_ap_tobj == jamm_ap_b7_obj) || (jamm_ap_sub_obj >= 0 && jamm_ap_tobj == jamm_ap_sub_obj))
                                {
                                    var jamm_ap_cx = (jamm_ap_card.bbox_right > jamm_ap_card.bbox_left) ? (jamm_ap_card.bbox_left + jamm_ap_card.bbox_right) * 0.5 : jamm_ap_card.x;
                                    var jamm_ap_cy = (jamm_ap_card.bbox_bottom > jamm_ap_card.bbox_top) ? (jamm_ap_card.bbox_top + jamm_ap_card.bbox_bottom) * 0.5 : jamm_ap_card.y;
                                    var jamm_ap_nx = round(1000 * (jamm_ap_cx - jamm_ap_vx) / jamm_ap_vw);
                                    var jamm_ap_ny = round(1000 * (jamm_ap_cy - jamm_ap_vy) / jamm_ap_vh);
                                    if (jamm_ap_nx >= 0 && jamm_ap_nx <= 1000 && jamm_ap_ny >= 0 && jamm_ap_ny <= 1000)
                                    {
                                        var jamm_ap_xy = string(jamm_ap_nx) + ":" + string(jamm_ap_ny);
                                        if (jamm_ap_b7_obj >= 0 && jamm_ap_tobj == jamm_ap_b7_obj) jamm_ap_b7_xy = jamm_ap_xy;
                                        if (jamm_ap_sub_obj >= 0 && jamm_ap_tobj == jamm_ap_sub_obj) jamm_ap_sub_xy = jamm_ap_xy;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            var f = file_text_open_append("jamm_autoplay.csv");
            file_text_write_string(f, string(current_time - jamm_ap_start) + "," + room_get_name(room) + "," + string(fps) + ","
                + string(round(jamm_ap_x)) + "," + string(round(jamm_ap_y)) + "," + string(round(jamm_ap_depth)) + ","
                + string_format(jamm_ap_d, 0, 0) + "," + string(jamm_ap_lv) + "," + string(jamm_ap_ls) + ","
                + string(round(jamm_ap_ly)) + "," + string(instance_count) + "," + jamm_ap_boat + ","
                + string(jamm_ap_chunks) + "," + jamm_ap_weather + "," + string(jamm_ap_has_sub) + ","
                + string(jamm_ap_has_weather) + "," + string(jamm_ap_has_ocean) + "," + string(jamm_ap_has_silo) + ","
                + string(jamm_ap_has_console) + "," + jamm_ap_bicon_xy + "," + jamm_ap_b7_xy + ","
                + jamm_ap_sub_xy + "," + jamm_ap_bmenu + "," + jamm_ap_cicon_xy + "," + string(jamm_ap_silo_x) + "," + string(jamm_ap_ocean_start)
                + "," + string(jamm_ap_has_harbor) + "," + string(jamm_ap_harbor_x) + "," + string(jamm_ap_harbor_y)
                + "," + string(jamm_ap_harbor_ready) + "," + string(jamm_ap_harbor_near) + "," + string(jamm_ap_harbor_menu));
            file_text_writeln(f);
            file_text_close(f);
            """);
        ModObjects.PlaceInstance(data, "NewWorld", probe, first: false);
        ModObjects.Compile(compiler);
    }
}
