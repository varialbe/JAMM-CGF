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
    const assets = [...species, ...rocks, ...decor, ground, underlay, 'Light', 'Collidable',
        'obj_jmk_ocean_decor', 'spr_jmk_ocean_slope_down', 'spr_jmk_ocean_slope_up', 'spr_jmk_ocean_wreck'];
    const context = vm.createContext({
        real: Number, floor: Math.floor, round: Math.round, abs: Math.abs, min: Math.min, max: Math.max,
        lerp: (a, b, t) => a + (b - a) * t, clamp: (v, lo, hi) => Math.min(Math.max(v, lo), hi),
        dcos: degrees => Math.cos(degrees * Math.PI / 180), dsin: degrees => Math.sin(degrees * Math.PI / 180),
        ...Object.fromEntries(assets.map(name => [name, name])),
        noone: null, ocean_seed: seed, ocean_start: start, ocean_anchor_y: anchor, room_height: bottom,
        ocean_content_chunk: content,
        make_color_rgb: (r, g, b) => r + g * 256 + b * 65536,
        merge_color: (a, b, t) => a + (b - a) * t,
        object_get_sprite: kind => kind,
        sprite_get_width: () => 1024,
        sprite_get_height: () => 1024,
        sprite_get_xoffset: () => 0,
        sprite_get_yoffset: () => 0,
        instance_create(x, y, kind) {
            const member = { x, y, kind, sprite_index: kind === ground ? underlay : kind,
                image_xscale: 1, image_yscale: 1 };
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
        ...['floor', 'height', 'biome', 'poi', 'species', 'layout', 'ground', 'scenery', 'load']
            .map(name => 'jmk_endless_ocean_' + name)]) {
        const path = new URL(`../../src/Jamm.Core/Builtins/Worldgen/${name}.gml`, import.meta.url);
        // The legacy VM allocates local arrays on their first indexed assignment.
        const body = readFileSync(path, 'utf8').replace(/var (layout|pool|kinds|plants);/g, 'var $1 = [];').replace(/\bexit;/g, 'return;');
        new vm.Script(`function ${name}(argument0, argument1, argument2, argument3, argument4) { ${body} }`,
            { filename: path.pathname }).runInContext(context);
    }
    return {
        live, blockers,
        layout: (seed, index, seam = anchor, height = bottom, reserve = content) =>
            context.jmk_endless_ocean_layout(seed, index, seam, height, reserve),
        heightAt: (offset, seedToUse = seed) => context.jmk_endless_ocean_height(seedToUse, offset, anchor, bottom, content),
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
                assert.ok(['Geiger', 'Ancient_Kingfish'].includes(chunk[17]));
            }
            for (let band = 0; band < 3; band++) {
                const slot = 7 + band * 4;
                assert.ok(chunk[slot] >= 600 && chunk[slot] <= 1448);
                assert.ok(chunk[slot + 1] > 1244 + 260);
                assert.ok(chunk[slot + 1] < Math.min(...chunk.slice(0, 5)) - 1000);
                assert.ok(species.includes(chunk[slot + 2]));
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
