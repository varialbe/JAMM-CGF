// Run synchronously in the grid controller, before the benchmark takes its first sample.
random_set_seed(4531);
var spawned;
for (var i = 0; i < 100; i += 1)
    spawned[i] = instance_create(irandom_range(-2048, 2048), irandom_range(-2048, 2048), obj_jamm_fg_fixture);
spawned[100] = instance_create(-512, -512, obj_jamm_fg_fixture);
spawned[101] = instance_create(512, 512, obj_jamm_fg_fixture);
spawned[102] = instance_create(1200000, -1200000, obj_jamm_fg_fixture);
var centers;
centers[0] = spawned[100];
centers[1] = spawned[101];
centers[2] = spawned[102];
global.jamm_fg_scope = 1;
var expected = ds_list_create();
var actual = ds_list_create();

for (var trial = 0; trial < 500; trial += 1)
{
    var center = centers[trial mod 3];
    var px = center.x + irandom_range(-520, 520);
    var py = center.y + irandom_range(-520, 520);
    var radius = irandom_range(1, 1024);
    if (trial mod 25 == 0)
    {
        with (spawned[trial div 25])
        {
            x += irandom_range(-1400, 1400);
            y += irandom_range(-1400, 1400);
            jamm_fg_touch();
        }
    }
    var cx = x;
    var cy = y;
    x = px;
    y = py;
    if (!jamm_fg_ready(radius)) show_error("Grid unexpectedly unavailable in synthetic query", true);
    var count = jamm_fg_collect(radius);
    ds_list_clear(expected);
    ds_list_clear(actual);
    with (FishLarge)
    {
        var dx = x - px;
        var dy = y - py;
        if ((dx * dx) + (dy * dy) < radius * radius) ds_list_add(expected, id);
    }
    for (var j = 0; j < count; j += 1)
    {
        var candidate = global.jamm_fg_ids[ds_list_find_value(global.jamm_fg_orders, j)];
        var dx = candidate.x - px;
        var dy = candidate.y - py;
        if ((dx * dx) + (dy * dy) < radius * radius) ds_list_add(actual, candidate);
    }
    if (ds_list_size(expected) != ds_list_size(actual)) show_error("Grid candidate count differs", true);
    for (var j = 0; j < ds_list_size(expected); j += 1)
        if (ds_list_find_value(expected, j) != ds_list_find_value(actual, j))
            show_error("Grid candidate order differs", true);
    x = cx;
    y = cy;
}

with (spawned[102])
{
    x += 32;
    jamm_fg_touch();
    if (!jamm_fg_ready(512)) show_error("Grid rejected a fish within its drift bound", true);
    x += 3072;
    jamm_fg_touch();
    if (!jamm_fg_ready(1024)) show_error("Grid rejected a fish after relinking its cell", true);
    var count = jamm_fg_collect(0);
    var found = false;
    for (var j = 0; j < count; j += 1)
        if (global.jamm_fg_ids[ds_list_find_value(global.jamm_fg_orders, j)] == id) found = true;
    if (!found) show_error("Grid lost a fish after moving it to another cell", true);
    var old_x = x;
    // Use a value beyond the bound even if the runner rounds x to a float.
    x = 33555456;
    if (x <= 33554432) show_error("Fixture could not set the out-of-bounds coordinate", true);
    jamm_fg_touch();
    if (global.jamm_fg_valid) show_error("Grid accepted a fish outside the supported coordinates", true);
    x = old_x;
}
global.jamm_fg_built = false;
if (!jamm_fg_ready(100)) show_error("Grid failed to rebuild for lifecycle check", true);
jamm_fg_instance_destroy_1(spawned[102]);
spawned[102] = jamm_fg_instance_create_3(1200000, -1200000, obj_jamm_fg_fixture);
if (jamm_fg_ready(100)) show_error("Grid accepted same-count instance replacement", true);
global.jamm_fg_built = false;
if (!jamm_fg_ready(100)) show_error("Grid failed to rebuild for activation check", true);
jamm_fg_instance_deactivate_object_1(spawned[102]);
jamm_fg_instance_activate_object_1(spawned[102]);
if (jamm_fg_ready(100)) show_error("Grid accepted deactivate/reactivate", true);

ds_list_destroy(expected);
ds_list_destroy(actual);
with (obj_jamm_fg_fixture) instance_destroy();
global.jamm_fg_scope = 0;
show_debug_message("[CGF-PERF-GRID-TEST] 500 ordered brute-force comparisons and lifecycle guards passed");
