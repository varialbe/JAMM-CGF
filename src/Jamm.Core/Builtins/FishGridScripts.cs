namespace Jamm.ModMaker;

internal static class FishGridScripts
{
    internal const int CellSize = 1024;
    internal const int Drift = 32;
    internal const int CoordinateLimit = 33554432;
    // Signed cell coordinates within CoordinateLimit occupy fewer than 18 bits per axis.
    // The packed integer is exactly representable in GM1's doubles.
    private const int RowStride = 262144;

    internal const string Reset = """
        global.jamm_fg_serial += 1;
        global.jamm_fg_built = false;
        global.jamm_fg_valid = false;
        global.jamm_fg_scope = 0;
        """;

    internal const string Available = "variable_global_exists(\"jamm_fg_enabled\")";
    internal static string GuardedCall(string helper) => $"if (global.jamm_fg_enabled) {helper}();";

    internal static readonly IReadOnlyDictionary<string, string> Scripts = new Dictionary<string, string>
    {
        ["jamm_fg_enter"] = "global.jamm_fg_scope += 1;",
        ["jamm_fg_leave"] = "global.jamm_fg_scope -= 1;",
        ["jamm_fg_dirty"] = "global.jamm_fg_valid = false;",
        ["jamm_fg_touch"] = $$"""
            if (!global.jamm_fg_valid) exit;
            if (!variable_instance_exists(id, "jamm_fg_serial_seen")) exit;
            if (jamm_fg_serial_seen != global.jamm_fg_serial) exit;
            if (abs(x - jamm_fg_origin_x) <= {{Drift}} && abs(y - jamm_fg_origin_y) <= {{Drift}}) exit;
            if (!(abs(x) <= {{CoordinateLimit}} && abs(y) <= {{CoordinateLimit}}))
            {
                global.jamm_fg_valid = false;
                exit;
            }
            var key = floor(x / {{CellSize}}) + (floor(y / {{CellSize}}) * {{RowStride}});
            var old_key = jamm_fg_cell;
            jamm_fg_origin_x = x;
            jamm_fg_origin_y = y;
            if (key == old_key) exit;

            // Relink the cell node while keeping the order captured by with (FishLarge).
            var order = jamm_fg_order;
            var previous = global.jamm_fg_previous[order];
            var next = global.jamm_fg_links[order];
            if (previous >= 0)
                global.jamm_fg_links[previous] = next;
            else if (next >= 0)
                ds_map_set(global.jamm_fg_heads, old_key, next);
            else
                ds_map_delete(global.jamm_fg_heads, old_key);
            if (next >= 0) global.jamm_fg_previous[next] = previous;
            jamm_fg_cell = key;
            {{LinkCellHead}}
            """,
        ["jamm_fg_type"] = """
            return argument0 == FishLarge || object_is_ancestor(argument0, FishLarge);
            """,
        ["jamm_fg_membership"] = $$"""
            if (!({{Available}})) exit;
            if (!global.jamm_fg_valid) exit;
            var selector = argument0;
            if (selector < 0 && selector != self) { global.jamm_fg_valid = false; exit; }
            if (selector == id || selector == self)
            {
                if (jamm_fg_type(object_index)) global.jamm_fg_valid = false;
                exit;
            }
            if (selector >= 0 && selector < 100000)
            {
                if (object_exists(selector))
                    if (jamm_fg_type(selector) || object_is_ancestor(FishLarge, selector))
                        global.jamm_fg_valid = false;
                exit;
            }
            // Activation can receive the ID of an inactive fish. An unresolvable ID must
            // invalidate the snapshot even when the eventual total count stays unchanged.
            if (!instance_exists(selector))
                global.jamm_fg_valid = false;
            else if (jamm_fg_type(selector.object_index))
                global.jamm_fg_valid = false;
            """,
        ["jamm_fg_build"] = $$"""
            global.jamm_fg_built = true;
            global.jamm_fg_valid = true;
            global.jamm_fg_count = 0;
            global.jamm_fg_room = room;
            ds_map_clear(global.jamm_fg_heads);
            with (FishLarge)
            {
                if (!(abs(x) <= {{CoordinateLimit}} && abs(y) <= {{CoordinateLimit}}))
                {
                    global.jamm_fg_valid = false;
                    break;
                }
                jamm_fg_serial_seen = global.jamm_fg_serial;
                jamm_fg_origin_x = x;
                jamm_fg_origin_y = y;
                var key = floor(x / {{CellSize}}) + (floor(y / {{CellSize}}) * {{RowStride}});
                var order = global.jamm_fg_count;
                jamm_fg_order = order;
                jamm_fg_cell = key;
                global.jamm_fg_ids[order] = id;
                {{LinkCellHead}}
                global.jamm_fg_count += 1;
            }
            """,
        ["jamm_fg_collect"] = $$"""
            var radius = argument0 + {{Drift + 1}};
            var min_x = floor((x - radius) / {{CellSize}});
            var max_x = floor((x + radius) / {{CellSize}});
            var min_y = floor((y - radius) / {{CellSize}});
            var max_y = floor((y + radius) / {{CellSize}});
            var orders = global.jamm_fg_orders;
            ds_list_clear(orders);
            for (var cy = min_y; cy <= max_y; cy += 1)
            {
                for (var cx = min_x; cx <= max_x; cx += 1)
                {
                    var order = ds_map_find_value(global.jamm_fg_heads, cx + (cy * {{RowStride}}));
                    if (is_undefined(order)) continue;
                    while (order >= 0)
                    {
                        ds_list_add(orders, order);
                        order = global.jamm_fg_links[order];
                    }
                }
            }
            // The per-cell lists contain ordinals from the original with. Sorting their
            // union is an ordered merge and does not assume order correlates with IDs.
            ds_list_sort(orders, true);
            return ds_list_size(orders);
            """,
        ["jamm_fg_ready"] = $$"""
            if (!({{Available}})) return false;
            if (!global.jamm_fg_enabled || global.jamm_fg_scope <= 0) return false;
            if (!(abs(argument0) <= {{CellSize}} && abs(x) <= {{CoordinateLimit}} && abs(y) <= {{CoordinateLimit}}))
                return false;
            if (!global.jamm_fg_built) jamm_fg_build();
            if (!global.jamm_fg_valid) return false;
            if (room != global.jamm_fg_room || instance_number(FishLarge) != global.jamm_fg_count)
            {
                global.jamm_fg_valid = false;
                return false;
            }
            return true;
            """
    };

    private const string LinkCellHead = """
        var head = ds_map_find_value(global.jamm_fg_heads, key);
        if (is_undefined(head)) head = -1;
        if (head >= 0) global.jamm_fg_previous[head] = order;
        global.jamm_fg_previous[order] = -1;
        global.jamm_fg_links[order] = head;
        ds_map_set(global.jamm_fg_heads, key, order);
        """;

    internal static string Avoidance(string original)
    {
        int start = original.IndexOf("with (argument0)", StringComparison.Ordinal);
        string prefix = original[..start];
        string loop = original[start..];
        // The matched loop writes sdist for every non-self visit, even when no impulse
        // is applied. Reproduce the final write using the last non-self snapshot entry.
        return $$"""
            {{prefix}}if (argument0 == FishLarge)
            {
                if (jamm_fg_ready(argument1))
                {
                    var jamm_fg_n = jamm_fg_collect(abs(argument1));
                    for (var jamm_fg_i = 0; jamm_fg_i < jamm_fg_n; jamm_fg_i += 1)
                    {{loop.Replace("with (argument0)", "with (global.jamm_fg_ids[ds_list_find_value(global.jamm_fg_orders, jamm_fg_i)])", StringComparison.Ordinal)}}
                    var jamm_fg_last = global.jamm_fg_count - 1;
                    if (jamm_fg_last >= 0)
                        if (global.jamm_fg_ids[jamm_fg_last] == id) jamm_fg_last -= 1;
                    if (jamm_fg_last >= 0)
                    {
                        var jamm_fg_final = global.jamm_fg_ids[jamm_fg_last];
                        var jamm_fg_dx = jamm_fg_final.x - x;
                        var jamm_fg_dy = jamm_fg_final.y - y;
                        sdist = (jamm_fg_dx * jamm_fg_dx) + (jamm_fg_dy * jamm_fg_dy);
                    }
                    exit;
                }
            }
            {{loop}}
            """;
    }
}
