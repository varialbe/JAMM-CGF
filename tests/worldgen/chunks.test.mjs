import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import vm from 'node:vm';

const start = 65935;
const anchor = 12991;
const bottom = 13000;
const content = 17;
const seed = 271828;
const species = [
    'Swooper', 'Roundfin', 'Garump', 'Spinefish', 'Yellowjack', 'Puff', 'Celad', 'Inno',
    'Garder', 'Pink_Kisser', 'Pennyfin', 'Lionfish', 'Mustardfish', 'Elder_Mustardfish',
    'Felish', 'Torby', 'Purdy', 'Leofish', 'Sassy', 'Boom_Puff', 'Dijon', 'Stankfin', 'Cavin',
    'Twohead', 'Garm', 'Jackfish', 'Primal_Turgeon', 'Mudback', 'Cuddlecanth', 'Buffalofish',
    'Geiger', 'Ancient_Kingfish', 'Underfin'
];
const rocks = ['CaveRockBig', 'CaveRockWide', 'CaveRockLeg', 'CaveRockSig', 'CaveRockTear', 'CaveRockTee', 'CaveRockWedge'];
const decor = ['JWeedSmall', 'JWeedMedium', 'Coral', 'Coral2', 'CoralDark', 'CoralFG3', 'GlowPlant', 'BulbPlantShort', 'ChainPlant'];
const ground = 'obj_jmk_ocean_ground';
const underlay = 'spr_jmk_ocean_underlay';
const cellSize = 480;
const variantCount = 16;

function topOf(member, x) {
    const localX = Math.floor(x - member.x);
    switch (member.sprite_index) {
        case 'spr_jmk_ocean_slope_down': return member.y + Math.floor(localX / 2) * member.image_yscale;
        case 'spr_jmk_ocean_slope_up': return member.y + Math.floor((511 - localX) / 2) * member.image_yscale;
        default: return member.y;
    }
}

function setup() {
    const live = new Set();
    const blockers = [];
    const drawCalls = [];
    let currentBatch = null;
    const assets = [...species, ...rocks, ...decor, ground, underlay, 'Light', 'Collidable',
        'obj_jmk_ocean_decor', 'obj_jmk_endless_ocean', 'spr_jmk_ocean_slope_down', 'spr_jmk_ocean_slope_up', 'spr_jmk_ocean_wreck'];
    const controller = {
        ocean_seed: seed,
        ocean_u0: Array.from({ length: variantCount }, (_, i) => i),
        ocean_v0: Array.from({ length: variantCount }, () => 0),
        ocean_u1: Array.from({ length: variantCount }, (_, i) => i + 1),
        ocean_v1: Array.from({ length: variantCount }, () => 1),
        ocean_tex: Array.from({ length: variantCount }, () => 0),
    };
    const view = { x: [0], y: [0], w: [1200], h: [720], current: 0 };
    const context = vm.createContext({
        real: Number, floor: Math.floor, round: Math.round, abs: Math.abs, min: Math.min, max: Math.max,
        lerp: (a, b, t) => a + (b - a) * t, clamp: (v, lo, hi) => Math.min(Math.max(v, lo), hi),
        dcos: degrees => Math.cos(degrees * Math.PI / 180), dsin: degrees => Math.sin(degrees * Math.PI / 180),
        ...Object.fromEntries(assets.map(name => [name, name])),
        noone: null, ocean_seed: seed, ocean_start: start, ocean_anchor_y: anchor, room_height: bottom,
        ocean_content_chunk: content,
        pr_trianglelist: 4,
        c_white: 16777215,
        view_xview: view.x, view_yview: view.y, view_wview: view.w, view_hview: view.h, view_current: 0,
        make_color_rgb: (r, g, b) => r + g * 256 + b * 65536,
        merge_color: (a, b, t) => a + (b - a) * t,
        object_get_sprite: kind => kind,
        sprite_get_width: () => 1024,
        sprite_get_height: () => 1024,
        sprite_get_xoffset: () => 0,
        sprite_get_yoffset: () => 0,
        instance_find: obj => (obj === 'obj_jmk_endless_ocean' ? controller : null),
        draw_self: () => {},
        draw_primitive_begin_texture: (kind, tex) => {
            currentBatch = { kind, tex, vertices: [] };
        },
        draw_vertex_texture_colour: (x, y, u, v, col, alpha) => {
            currentBatch.vertices.push({ x, y, u, v, col, alpha });
        },
        draw_primitive_end: () => {
            drawCalls.push(currentBatch);
            currentBatch = null;
        },
        instance_create(x, y, kind) {
            const member = { x, y, kind, sprite_index: kind === ground ? underlay : kind,
                image_xscale: 1, image_yscale: 1, image_blend: 16777215, object_id: kind, display_size: 1, size: 1 };
            live.add(member);
            return member;
        },
        collision_rectangle(x1, y1, x2, y2, parent, precise, excludeSelf) {
            assert.equal(parent, 'Collidable');
            assert.equal(precise, true);
            assert.equal(excludeSelf, false);
            assert.equal(x2 - x1, 520);
            assert.equal(y2 - y1, 520);
            return [...live, ...blockers].find(member => {
                if (member.kind !== ground || x2 < member.x || x1 >= member.x + 512) return false;
                const a = topOf(member, Math.max(x1, member.x));
                const b = topOf(member, Math.min(x2, member.x + 511));
                return y2 >= Math.min(a, b);
            }) ?? null;
        },
        asset_get_index: () => -1,
        instance_exists: instance => live.has(instance),
        variable_instance_exists: () => false,
        power: Math.pow,
        jamm_fish_sonar_init: member => {
            member.s1 = -1;
            member.s2 = member.display_size - 1;
            member.s3 = 10 + 5 * (member.display_size - 1);
            member.s4 = 10 + 3 * (member.display_size - 1);
            member.s5 = (member.size + 0.5) / 3.5;
        },
        script_exists: script => typeof script === 'function',
        fish_object_to_name: object => object,
        get_index_for_name: object => species.indexOf(object),
        is_undefined: value => value === undefined,
        ds_map_find_first: map => map.keys().next().value,
        ds_map_find_next: (map, key) => [...map.keys()][[...map.keys()].indexOf(key) + 1],
        ds_map_exists: (map, key) => map.has(key),
        ds_map_add: (map, key, value) => map.set(key, value),
        ds_map_find_value: (map, key) => map.get(key),
        ds_map_delete: (map, key) => map.delete(key),
        ds_list_create: () => [],
        ds_list_add: (list, value) => list.push(value),
        ds_list_find_value: (list, index) => list[index],
        ds_list_size: list => list.length,
        ds_list_destroy: list => { list.length = 0; },
        script_execute: (script, ...args) => script(...args)
    });
    for (const name of ['jamm_chunk_unit', 'jamm_chunk_stream',
        ...['floor', 'height', 'biome', 'poi', 'species', 'layout', 'underlay_cell', 'ground', 'scenery', 'load']
            .map(name => 'jmk_endless_ocean_' + name)]) {
        const path = new URL(`../../src/Jamm.Core/Builtins/Worldgen/${name}.gml`, import.meta.url);
        // The legacy VM allocates local arrays on their first indexed assignment.
        const body = readFileSync(path, 'utf8').replace(/var (layout|pool|kinds|plants);/g, 'var $1 = [];').replace(/\bexit;/g, 'return;');
        new vm.Script(`function ${name}(argument0, argument1, argument2, argument3, argument4) { ${body} }`,
            { filename: path.pathname }).runInContext(context);
    }
    const drawGroundPath = new URL('../../src/Jamm.Core/Builtins/Worldgen/draw_ground.gml', import.meta.url);
    const drawGroundBody = readFileSync(drawGroundPath, 'utf8')
        .replace(/var (px|py|qx|qy);/g, 'var $1 = [];')
        .replace(/\bexit;/g, 'return;');
    new vm.Script(`function draw_ground() { with (this) { ${drawGroundBody} } }`, { filename: drawGroundPath.pathname }).runInContext(context);
    return {
        live, blockers, drawCalls, view, controller,
        layout: (seed, index, seam = anchor, height = bottom, reserve = content) =>
            context.jmk_endless_ocean_layout(seed, index, seam, height, reserve),
        heightAt: (offset, seedToUse = seed) => context.jmk_endless_ocean_height(seedToUse, offset, anchor, bottom, content),
        underlayCell: (seedToUse, cx, cy) => context.jmk_endless_ocean_underlay_cell(seedToUse, cx, cy),
        drawGround: member => context.draw_ground.call(member),
        load: context.jmk_endless_ocean_load,
        stream: (map, first, last, create = context.jmk_endless_ocean_load) =>
            context.jamm_chunk_stream(map, first, last, create, member => assert.ok(live.delete(member)))
    };
}

test('same seed reproduces terrain and fish across visitation orders, with variation by seed', () => {
    const { layout } = setup();
    const indices = [0, 1, 82, 117, 364, 487];
    const first = indices.map(index => layout(seed, index));
    assert.deepEqual(first, [...indices].reverse().map(index => layout(seed, index)).reverse());
    for (const index of indices.slice(2)) assert.notDeepEqual(layout(seed, index), layout(seed + 1, index));
});

test('ocean sonar hooks are scoped to the region and preserve basic sonar', () => {
    const root = new URL('../../src/Jamm.Core/Builtins/Worldgen/', import.meta.url);
    const inside = readFileSync(new URL('jmk_endless_ocean_inside.gml', root), 'utf8');
    const sonarView = readFileSync(new URL('jmk_endless_ocean_sonar_view.gml', root), 'utf8');
    const sonarClear = readFileSync(new URL('jmk_endless_ocean_sonar_clear.gml', root), 'utf8');
    const step = readFileSync(new URL('step.gml', root), 'utf8');

    assert.match(inside, /asset_get_index\("obj_jmk_endless_ocean"\)/);
    assert.match(inside, /if \(!instance_exists\(ocean_object\) \|\| !instance_exists\(girl_object\) \|\| !instance_exists\(lure_object\)\) return false;/);
    assert.match(inside, /if \(lure\.state == 1 \|\| lure\.state == 2\) x_to_check = lure\.x;/);
    assert.match(inside, /return x_to_check >= ocean\.ocean_start;/);
    assert.match(sonarView, /if \(lure\.deep && !cave_sonar\)/);
    assert.match(sonarView, /fixed_y = min\(lure\.y, room_height - 1000\);/);
    assert.match(sonarView, /view_xview\[3\] = lure\.x - 1000;/);
    assert.match(sonarView, /view_yview\[3\] = max\(fixed_y - 1000, 4000\);/);
    assert.doesNotMatch(sonarView, /cave_sonar\s*=/);
    assert.doesNotMatch(sonarView, /lure\.y - 1850/); // shallow window stays vanilla (1255..4955)
    assert.match(sonarClear, /return jmk_endless_ocean_inside\(\);/);
    assert.match(step, /with \(Boat7\)[\s\S]*MAX_X = max\(MAX_X, other\.ocean_end - 400\);/);
    assert.doesNotMatch(step, /Boat\.boost_radar\s*=/);
});

test('transition begins at the archive mask and eases over three chunks in both room depths', () => {
    const { layout } = setup();
    for (const [seam, height, reserve] of [[12991, 13000, 17], [20588, 26000, 12]]) {
        const chunks = Array.from({ length: 10 }, (_, index) => layout(seed, index, seam, height, reserve));
        assert.equal(chunks[0][0], seam);
        assert.ok(Math.abs(chunks[0][1] - seam) < 35);
        assert.notEqual(chunks[0][0], chunks[3][0]);
        for (const [index, chunk] of chunks.entries()) {
            if (index > 0) assert.equal(chunks[index - 1][4], chunk[0]);
            assert.ok(chunk.slice(0, 5).every(y => y < height));
            assert.equal(chunk[7], undefined);
        }
    }
});

test('every slope joins its solid underlay and adjacent strips through the room bottom', () => {
    const { load, heightAt, live } = setup();
    for (const index of [0, 1, 2, content, content + 3, content + 47, content + 487]) {
        const owned = [];
        load(index, owned);
        const pieces = owned.filter(piece => piece.kind === ground);
        for (let x = start + index * 2048; x < start + (index + 1) * 2048; x += 1) {
            const covering = pieces.filter(piece => piece.x <= x && x < piece.x + 512);
            const modelTop = Math.min(...covering.map(piece => topOf(piece, x)));
            assert.ok(Math.abs(modelTop - heightAt(x - start)) <= 3);
            const base = covering.find(piece => piece.sprite_index === underlay);
            assert.ok(base.y + base.image_yscale * 256 >= bottom);
            const cap = covering.find(piece => piece.sprite_index !== underlay);
            if (cap) assert.ok(cap.y + cap.image_yscale * 272 >= base.y);
        }
        live.clear();
    }
});

test('each depth band stays in water and the biome and wreck rolls vary within bounds', () => {
    const { layout } = setup();
    for (const testSeed of [0, seed, 2147483646]) {
        const seenSpecies = new Set();
        const seenBiomes = new Set();
        let lastWreck;
        for (let index = content; index < content + 488; index++) {
            const chunk = layout(testSeed, index);
            seenBiomes.add(chunk[5]);
            if (chunk[6]) {
                if (lastWreck !== undefined) assert.ok(index - lastWreck >= 10 && index - lastWreck <= 20);
                lastWreck = index;
                assert.ok(['Geiger', 'Underfin'].includes(chunk[17]));
            }
            for (let band = 0; band < 3; band++) {
                const slot = 7 + band * 4;
                assert.ok(chunk[slot] >= 600 && chunk[slot] <= 1448);
                assert.ok(chunk[slot + 1] > 1244 + 260);
                assert.ok(chunk[slot + 1] < Math.min(...chunk.slice(0, 5)) - 1000);
                assert.ok(species.includes(chunk[slot + 2]));
                assert.ok(!['Torby', 'Ancient_Kingfish'].includes(chunk[slot + 2]));
                seenSpecies.add(chunk[slot + 2]);
            }
        }
        assert.equal(seenBiomes.size, 3);
        assert.ok(seenSpecies.size >= 29);
    }
});

test('a live collision veto skips only the blocked candidate', () => {
    const { layout, load, blockers } = setup();
    const chunk = layout(seed, content + 4);
    // A full-height obstacle at the deep candidate leaves the upper and middle bands untouched.
    blockers.push({ x: start + (content + 4) * 2048 + chunk[15] - 128, y: chunk[16],
        kind: ground, sprite_index: underlay });
    const owned = [];
    load(content + 4, owned);
    const fish = owned.filter(member => member.jmk_endless_ocean_fish);
    assert.equal(fish.length, 2);
    assert.ok(fish.every(member => member.catalog_index >= 0));
});

test('five chunks stream at most fifteen fish and release all owned instances on return west', () => {
    const { stream, live, load } = setup();
    const map = new Map();
    const visits = [];
    const create = (index, owned) => { visits.push(index); load(index, owned); };
    stream(map, content, content + 2, create);
    const retained = map.get(content + 1);
    const firstVisit = retained.map(member => ({ ...member }));
    stream(map, content + 1, content + 5, create);
    assert.equal(map.get(content + 1), retained);
    assert.equal(visits.length, 6);
    assert.equal([...live].filter(member => member.jmk_endless_ocean_fish).length, 15);
    assert.ok(live.size <= 75);
    stream(map, content + 485, content + 487, create);
    assert.equal(map.size, 3);
    assert.equal(retained.length, 0);
    stream(map, content, content + 2, create);
    assert.deepEqual(map.get(content + 1), firstVisit);
    stream(map, 0, -1, create);
    assert.equal(map.size, 0);
    assert.equal(live.size, 0);
});

test('underlay cells are deterministic, vary by seed, and satisfy Wang edge matching', () => {
    const { underlayCell } = setup();
    const seen = new Set();
    for (let cy = 20; cy <= 27; cy++) {
        for (let cx = 130; cx <= 160; cx++) {
            const v = underlayCell(seed, cx, cy);
            assert.ok(Number.isInteger(v) && v >= 0 && v < variantCount);
            assert.equal(v, underlayCell(seed, cx, cy));
            seen.add(v);
            const right = underlayCell(seed, cx + 1, cy);
            assert.equal((v >> 2) & 1, right & 1);
            const below = underlayCell(seed, cx, cy + 1);
            assert.equal((v >> 1) & 1, (below >> 3) & 1);
        }
    }
    assert.ok(seen.size >= 12);
    const otherSeed = Array.from({ length: 16 }, (_, i) => underlayCell(seed + 1, 140 + i, 24));
    const baseSeed = Array.from({ length: 16 }, (_, i) => underlayCell(seed, 140 + i, 24));
    assert.notDeepEqual(baseSeed, otherSeed);
});

test('underlay and slope caps are world-aligned across strip boundaries with no brightness jump', () => {
    const { load, drawGround, drawCalls, view } = setup();
    const owned = [];
    load(content + 2, owned);
    load(content + 3, owned);

    const pieces = owned.filter(piece => piece.kind === ground).sort((a, b) => a.x - b.x);
    assert.ok(pieces.every(piece => piece.image_blend === 16777215));

    const underlays = pieces.filter(piece => piece.sprite_index === underlay);
    const caps = pieces.filter(piece => piece.sprite_index !== underlay);
    assert.ok(caps.length > 0);

    // Inspect two adjacent strips across a chunk boundary (strip 3 of chunk+2 -> strip 0 of chunk+3).
    const leftStrip = underlays[3];
    const rightStrip = underlays[4];
    assert.equal(leftStrip.x + 512, rightStrip.x);

    view.x[0] = leftStrip.x - 100;
    view.y[0] = Math.min(leftStrip.y, rightStrip.y) - 100;
    view.w[0] = 1400;
    view.h[0] = 720;

    drawCalls.length = 0;
    drawGround(leftStrip);
    assert.equal(drawCalls.length, 1);
    const leftVerts = drawCalls[0].vertices;

    drawCalls.length = 0;
    drawGround(rightStrip);
    assert.equal(drawCalls.length, 1);
    const rightVerts = drawCalls[0].vertices;

    const boundaryX = leftStrip.x + 512;
    const sampleY = Math.max(leftStrip.y, rightStrip.y) + 50;
    const findTriAt = (verts, x, y) => {
        for (let i = 0; i < verts.length; i += 3) {
            const tri = [verts[i], verts[i + 1], verts[i + 2]];
            const xs = tri.map(p => p.x);
            const ys = tri.map(p => p.y);
            if (x >= Math.min(...xs) - 1e-6 && x <= Math.max(...xs) + 1e-6 &&
                y >= Math.min(...ys) - 1e-6 && y <= Math.max(...ys) + 1e-6) {
                return tri;
            }
        }
        return null;
    };

    const triL = findTriAt(leftVerts, boundaryX, sampleY);
    const triR = findTriAt(rightVerts, boundaryX, sampleY);
    assert.ok(triL && triR);

    const interpUv = (tri, x, y) => {
        const minX = Math.min(...tri.map(p => p.x)), maxX = Math.max(...tri.map(p => p.x));
        const minY = Math.min(...tri.map(p => p.y)), maxY = Math.max(...tri.map(p => p.y));
        const minU = Math.min(...tri.map(p => p.u)), maxU = Math.max(...tri.map(p => p.u));
        const minV = Math.min(...tri.map(p => p.v)), maxV = Math.max(...tri.map(p => p.v));
        const u = minU + ((x - minX) / (maxX - minX)) * (maxU - minU);
        const v = minV + ((y - minY) / (maxY - minY)) * (maxV - minV);
        return { variant: Math.floor(minU), uLocal: u - Math.floor(minU), vLocal: v };
    };

    const endOfLeft = interpUv(triL, boundaryX, sampleY);
    const startOfRight = interpUv(triR, boundaryX, sampleY);
    assert.ok(Math.abs(endOfLeft.vLocal - startOfRight.vLocal) < 1e-6);
    if (boundaryX % cellSize !== 0) {
        assert.equal(endOfLeft.variant, startOfRight.variant);
        assert.ok(Math.abs(endOfLeft.uLocal - startOfRight.uLocal) < 1e-6);
    } else {
        assert.ok(Math.abs(endOfLeft.uLocal - 1) < 1e-6);
        assert.ok(Math.abs(startOfRight.uLocal) < 1e-6);
        assert.equal((endOfLeft.variant >> 2) & 1, startOfRight.variant & 1);
    }

    // Verify slope cap shares the exact world-aligned cell and UVs with its underlay at y = base_y.
    const cap = caps[0];
    const matchingUnderlay = underlays.find(u => u.x === cap.x);
    view.x[0] = cap.x - 50;
    view.y[0] = cap.y - 50;
    drawCalls.length = 0;
    drawGround(cap);
    assert.equal(drawCalls.length, 1);
    const capBottomVerts = drawCalls[0].vertices.filter(p => Math.abs(p.y - matchingUnderlay.y) < 1e-6);
    assert.ok(capBottomVerts.length >= 2);
    drawCalls.length = 0;
    drawGround(matchingUnderlay);
    const underlayTopVerts = drawCalls[0].vertices.filter(p => Math.abs(p.y - matchingUnderlay.y) < 1e-6);
    for (const cv of capBottomVerts) {
        const match = underlayTopVerts.find(uv => Math.abs(uv.x - cv.x) < 1e-6 && Math.abs(uv.u - cv.u) < 1e-6 && Math.abs(uv.v - cv.v) < 1e-6);
        assert.ok(match, `Expected matching vertex at (${cv.x}, ${cv.y}) with uv (${cv.u}, ${cv.v})`);
    }
});
