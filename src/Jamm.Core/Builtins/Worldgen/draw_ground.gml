var is_underlay = (sprite_index == spr_jmk_ocean_underlay);
var is_slope = (sprite_index == spr_jmk_ocean_slope_down || sprite_index == spr_jmk_ocean_slope_up);
if (!is_underlay && !is_slope)
{
    draw_self();
    exit;
}

var vx = view_xview[view_current];
var vy = view_yview[view_current];
var vw = view_wview[view_current];
var vh = view_hview[view_current];
if (x + 512 <= vx || x >= vx + vw) exit;

var ctrl = instance_find(obj_jmk_endless_ocean, 0);
if (ctrl == noone) exit;
var seed = ctrl.ocean_seed;
var cs = 480;

if (is_underlay)
{
    var top = max(y, vy);
    var bottom = min(room_height + 2, vy + vh);
    if (top >= bottom) exit;

    var cx0 = floor(x / cs);
    var cx1 = floor((x + 511) / cs);
    var cy0 = floor(top / cs);
    var cy1 = floor((bottom - 0.001) / cs);
    var prim_open = false;
    var active_tex = 0;

    for (var cy = cy0; cy <= cy1; cy += 1)
    {
        var cell_y0 = cy * cs;
        var iy0 = max(top, cell_y0);
        var iy1 = min(bottom, cell_y0 + cs);
        if (iy0 >= iy1) continue;

        for (var cx = cx0; cx <= cx1; cx += 1)
        {
            var cell_x0 = cx * cs;
            var ix0 = max(x, cell_x0);
            var ix1 = min(x + 512, cell_x0 + cs);
            if (ix0 >= ix1) continue;

            var v = jmk_endless_ocean_underlay_cell(seed, cx, cy);
            var tex = ctrl.ocean_tex[v];
            if (!prim_open || tex != active_tex)
            {
                if (prim_open) draw_primitive_end();
                active_tex = tex;
                prim_open = true;
                draw_primitive_begin_texture(pr_trianglelist, active_tex);
            }

            var u0 = ctrl.ocean_u0[v];
            var v0 = ctrl.ocean_v0[v];
            var uw = ctrl.ocean_u1[v] - u0;
            var vh_uv = ctrl.ocean_v1[v] - v0;
            var tu0 = u0 + ((ix0 - cell_x0) / cs) * uw;
            var tu1 = u0 + ((ix1 - cell_x0) / cs) * uw;
            var tv0 = v0 + ((iy0 - cell_y0) / cs) * vh_uv;
            var tv1 = v0 + ((iy1 - cell_y0) / cs) * vh_uv;

            draw_vertex_texture_colour(ix0, iy0, tu0, tv0, c_white, 1);
            draw_vertex_texture_colour(ix1, iy0, tu1, tv0, c_white, 1);
            draw_vertex_texture_colour(ix1, iy1, tu1, tv1, c_white, 1);
            draw_vertex_texture_colour(ix0, iy0, tu0, tv0, c_white, 1);
            draw_vertex_texture_colour(ix1, iy1, tu1, tv1, c_white, 1);
            draw_vertex_texture_colour(ix0, iy1, tu0, tv1, c_white, 1);
        }
    }
    if (prim_open) draw_primitive_end();
    exit;
}

var y_top = min(slope_west, slope_east);
var y_bot = max(slope_west, slope_east);
if (y_bot <= vy || y_top >= vy + vh) exit;

var tri_x0 = x;
var tri_y0 = slope_west;
var tri_x1 = x + 512;
var tri_y1 = slope_east;
var tri_x2 = slope_west < slope_east ? x : x + 512;
var tri_y2 = y_bot;

var cx0 = floor(x / cs);
var cx1 = floor((x + 511) / cs);
var cy0 = floor(y_top / cs);
var cy1 = floor((y_bot - 0.001) / cs);

var px;
var py;
var qx;
var qy;
px[7] = 0;
py[7] = 0;
qx[7] = 0;
qy[7] = 0;

var prim_open = false;
var active_tex = 0;

for (var cy = cy0; cy <= cy1; cy += 1)
{
    var cell_y0 = cy * cs;
    var cell_y1 = cell_y0 + cs;
    for (var cx = cx0; cx <= cx1; cx += 1)
    {
        var cell_x0 = cx * cs;
        var cell_x1 = cell_x0 + cs;

        px[0] = tri_x0; py[0] = tri_y0;
        px[1] = tri_x1; py[1] = tri_y1;
        px[2] = tri_x2; py[2] = tri_y2;
        var n = 3;

        for (var edge = 0; edge < 4; edge += 1)
        {
            if (n < 3) break;
            var m = 0;
            for (var i = 0; i < n; i += 1)
            {
                var j = (i + 1) % n;
                var ax = px[i]; var ay = py[i];
                var bx = px[j]; var by = py[j];
                var in_a = false;
                var in_b = false;
                if (edge == 0) { in_a = (ax >= cell_x0); in_b = (bx >= cell_x0); }
                else if (edge == 1) { in_a = (ax <= cell_x1); in_b = (bx <= cell_x1); }
                else if (edge == 2) { in_a = (ay >= cell_y0); in_b = (by >= cell_y0); }
                else { in_a = (ay <= cell_y1); in_b = (by <= cell_y1); }

                if (in_a)
                {
                    qx[m] = ax; qy[m] = ay; m += 1;
                    if (!in_b)
                    {
                        var t = 0;
                        if (edge == 0) t = (cell_x0 - ax) / (bx - ax);
                        else if (edge == 1) t = (cell_x1 - ax) / (bx - ax);
                        else if (edge == 2) t = (cell_y0 - ay) / (by - ay);
                        else t = (cell_y1 - ay) / (by - ay);
                        qx[m] = ax + (bx - ax) * t;
                        qy[m] = ay + (by - ay) * t;
                        m += 1;
                    }
                }
                else if (in_b)
                {
                    var t = 0;
                    if (edge == 0) t = (cell_x0 - ax) / (bx - ax);
                    else if (edge == 1) t = (cell_x1 - ax) / (bx - ax);
                    else if (edge == 2) t = (cell_y0 - ay) / (by - ay);
                    else t = (cell_y1 - ay) / (by - ay);
                    qx[m] = ax + (bx - ax) * t;
                    qy[m] = ay + (by - ay) * t;
                    m += 1;
                }
            }
            for (var i = 0; i < m; i += 1)
            {
                px[i] = qx[i];
                py[i] = qy[i];
            }
            n = m;
        }

        if (n < 3) continue;

        var v = jmk_endless_ocean_underlay_cell(seed, cx, cy);
        var tex = ctrl.ocean_tex[v];
        if (!prim_open || tex != active_tex)
        {
            if (prim_open) draw_primitive_end();
            active_tex = tex;
            prim_open = true;
            draw_primitive_begin_texture(pr_trianglelist, active_tex);
        }

        var u0 = ctrl.ocean_u0[v];
        var v0 = ctrl.ocean_v0[v];
        var uw = ctrl.ocean_u1[v] - u0;
        var vh_uv = ctrl.ocean_v1[v] - v0;

        var ux0 = u0 + ((px[0] - cell_x0) / cs) * uw;
        var vy0 = v0 + ((py[0] - cell_y0) / cs) * vh_uv;
        for (var i = 1; i < n - 1; i += 1)
        {
            var ux1 = u0 + ((px[i] - cell_x0) / cs) * uw;
            var vy1 = v0 + ((py[i] - cell_y0) / cs) * vh_uv;
            var ux2 = u0 + ((px[i + 1] - cell_x0) / cs) * uw;
            var vy2 = v0 + ((py[i + 1] - cell_y0) / cs) * vh_uv;
            draw_vertex_texture_colour(px[0], py[0], ux0, vy0, c_white, 1);
            draw_vertex_texture_colour(px[i], py[i], ux1, vy1, c_white, 1);
            draw_vertex_texture_colour(px[i + 1], py[i + 1], ux2, vy2, c_white, 1);
        }
    }
}
if (prim_open) draw_primitive_end();
