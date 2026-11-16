// Execute the injected GML's JS-compatible statement subset with fake draw builtins.
// This tests the shipped script bodies, rather than a second allocator implementation.
// Usage: node scripts/test-hud-runtime.mjs (no packages or game files needed).
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import assert from 'node:assert/strict';
const source = readFileSync(new URL('../src/Jamm.Core/Building/HudRuntime.cs', import.meta.url), 'utf8');
const scripts = [...source.matchAll(/\["(jamm_hud_\w+)"\] = """\s*([\s\S]*?)\s*"""/g)];
assert.equal(scripts.length, 4);
let state = { font: 10, color: 123, alpha: 0.7, halign: 1, valign: 0 };
let drawn = [];
const context = {
    global: { jamm_hud_rows: new Proxy([], { get: (a, k) => a[k] ??= [] }) },
    view_current: 0, view_xview: [100, 200], view_yview: [300, 400], view_wview: [960, 480],
    ExperienceBar: { offset_y: 450 }, Colors: { ui_light: 999 }, fa_right: 2, fa_middle: 1, fa_left: 0, fa_top: 0,
    variable_global_exists: name => name in context.global,
    instance_exists: () => true, max: Math.max, abs: Math.abs, string: String,
    dcos: a => Math.cos(a * Math.PI / 180), dsin: a => Math.sin(a * Math.PI / 180),
    string_height: s => state.font * s.split('#').length,
    string_width: s => state.font * Math.max(...s.split('#').map(s => s.length)),
};
for (const key of ['font', 'color', 'alpha', 'halign', 'valign']) {
    if (key === 'color' || key === 'alpha') context['draw_get_' + key] = () => state[key];
    context['draw_set_' + key] = v => { state[key] = v; };
}
function draw(x, y, text, sx, sy, shadow) {
    const h = context.string_height(text) * sy;
    drawn.push({ text, x, y, top: y - h / 2, bottom: y + h / 2 + shadow, ...state });
}
context.draw_text_transformed = (x, y, text, sx, sy) => draw(x, y, text, sx, sy, 0);
context.draw_text_shadowed = (x, y, text) => { draw(x, y, text, 1, 1, 2); state.color = 999; };
vm.createContext(context);
for (const [, name, body] of [...scripts, ...source.matchAll(/\["(jamm_hud_set_\w+)"\] = "([^"\n]+)"/g)]) {
    // Only GML's 2D array indexing differs from JS in this deliberately small subset.
    const js = body.replace(/global\.jamm_hud_rows\[([^,\]]+),(\d+)\]/g, 'global.jamm_hud_rows[$1][$2]');
    vm.runInContext(`function ${name}(argument0, argument1, argument2, argument3, argument4, argument5) { ${js} }`, context);
}
context.jamm_hud_set_font(state.font);
context.jamm_hud_set_halign(state.halign);
context.jamm_hud_set_valign(state.valign);
function frame() {
    drawn = [];
    context.jamm_hud_begin();
    context.jamm_hud_set_font(30);
    context.jamm_hud_text(2, 0, 'Quest A#two lines', .5, .5, 0);
    context.jamm_hud_text(2, 0, 'Quest B', .5, .5, 0);
    context.jamm_hud_set_font(12);
    context.jamm_hud_text(1, 1, 'FPS', 1, 1, 0);
    context.jamm_hud_text(1, 1, 'X', 1, 1, 0);
    context.jamm_hud_text(1, 1, 'Y', 1, 1, 0);
    const before = { ...state };
    context.jamm_hud_flush();
    assert.deepEqual(state, before, 'flush restores draw state');
    assert.deepEqual(drawn.map(d => d.text), ['FPS', 'X', 'Y', 'Quest A#two lines', 'Quest B']);
    assert.ok(drawn[0].bottom <= context.view_yview[0] + context.ExperienceBar.offset_y - 4);
    for (let i = 1; i < drawn.length; i++) assert.ok(drawn[i].bottom <= drawn[i-1].top - 4, 'measured bounds have a gap');
    assert.ok(drawn.every(d => d.x === 1035));
    assert.equal(drawn[3].font, 30, 'font captured at enqueue');
    return drawn.map(d => d.y);
}
const expected = frame();
for (let i = 0; i < 20; i++) assert.deepEqual(frame(), expected, 'extra render frames cannot accumulate reservations');
context.view_current = 1;
context.jamm_hud_begin();
drawn = [];
context.jamm_hud_text(1, 0, 'other view', 1, 1, 0);
context.jamm_hud_flush();
assert.equal(drawn.length, 0);
context.view_current = 0;
assert.deepEqual(frame(), expected, 'other views cannot contaminate view zero');
context.view_yview[0] += 80;
assert.deepEqual(frame(), expected.map(y => y + 80), 'camera-relative anchor moves every draw pass');
context.jamm_hud_begin(); drawn = []; context.jamm_hud_flush();
assert.equal(drawn.length, 0, 'hidden/absent rows consume no space');
const cursor = context.global.jamm_hud_cursor;
assert.equal(context.jamm_hud_reserve(24, 'author'), cursor - 12);
assert.equal(context.global.jamm_hud_cursor, cursor - 28);
console.log('PASS: shipped HUD scripts: bounds/gaps, lanes, captured state, repeated frames, views, camera and hidden rows.');
