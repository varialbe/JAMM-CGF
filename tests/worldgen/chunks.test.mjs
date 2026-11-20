import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import vm from 'node:vm';

const start = 170000;
const seed = 271828;
const species = ['Celad', 'Inno', 'Mustardfish', 'Elder_Mustardfish', 'Dijon', 'Garm'];
const near = (x, y, rock) => x + 180 >= rock.x && x - 180 <= rock.x + rock.image_xscale * 1024 &&
    y + 180 >= rock.y && y - 180 <= rock.y + rock.image_yscale * 1024;

function setup() {
    const live = new Set();
    const blockers = [];
    const context = vm.createContext({
        real: Number, floor: Math.floor, round: Math.round, min: Math.min, max: Math.max,
        lerp: (a, b, t) => a + (b - a) * t,
        ...Object.fromEntries(species.map(name => [name, name])),
        CaveRockBig: 'CaveRockBig', Collidable: 'Collidable', noone: null,
        ocean_seed: seed, ocean_start: start,
        instance_create(x, y, kind) {
            const member = { x, y, kind, image_xscale: 1, image_yscale: 1 };
            live.add(member);
            return member;
        },
        collision_rectangle(x1, y1, x2, y2, parent, precise, excludeSelf) {
            assert.equal(parent, 'Collidable');
            assert.equal(precise, true);
            assert.equal(excludeSelf, false);
            assert.equal(x2 - x1, 360);
            assert.equal(y2 - y1, 360);
            const x = (x1 + x2) / 2;
            const y = (y1 + y2) / 2;
            return [...live, ...blockers].find(rock => rock.kind === 'CaveRockBig' && near(x, y, rock)) ?? null;
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
    for (const name of ['jamm_chunk_unit', 'jamm_chunk_stream', 'jmk_endless_ocean_floor',
        'jmk_endless_ocean_layout', 'jmk_endless_ocean_load']) {
        const path = new URL(`../../src/Jamm.Core/Builtins/Worldgen/${name}.gml`, import.meta.url);
        // GML 1.4 allocates local arrays on the first indexed assignment.
        const body = readFileSync(path, 'utf8').replace('var layout;', 'var layout = [];');
        new vm.Script(`function ${name}(argument0, argument1, argument2, argument3, argument4) { ${body} }`,
            { filename: path.pathname }).runInContext(context);
    }
    return {
        live, blockers,
        layout: context.jmk_endless_ocean_layout,
        load: context.jmk_endless_ocean_load,
        stream: (map, first, last, create = context.jmk_endless_ocean_load) =>
            context.jamm_chunk_stream(map, first, last, create, member => assert.ok(live.delete(member)))
    };
}

test('the same seed reproduces rocks and fish independently of visitation order', () => {
    const { layout } = setup();
    const indices = [0, 1, 82, 117, 364, 487];
    const first = indices.map(index => layout(seed, index, start, 1244));
    const second = [...indices].reverse().map(index => layout(seed, index, start, 1244)).reverse();
    assert.deepEqual(first, second);
    for (const [i, index] of indices.entries()) {
        const next = layout(seed + 1, index, start, 1244);
        assert.notDeepEqual(first[i].slice(0, 16), next.slice(0, 16));
        assert.notDeepEqual(first[i].slice(16), next.slice(16));
    }
});

test('spawn envelopes clear the conservative bounds of rocks, including across seams', () => {
    const { layout } = setup();
    for (const testSeed of [0, seed, 2147483646]) {
        const chunks = Array.from({ length: 488 }, (_, index) => layout(testSeed, index, start, 1244));
        for (const [index, chunk] of chunks.entries()) {
            const rocks = chunks.slice(Math.max(index - 1, 0), index + 2).flatMap(neighbor =>
                [0, 4, 8, 12].map(slot => ({
                    x: neighbor[slot], y: neighbor[slot + 1],
                    image_xscale: neighbor[slot + 2], image_yscale: neighbor[slot + 3]
                })));
            for (let band = 0; band < 3; band++) {
                const slot = 16 + band * 4;
                assert.ok(species.slice(band * 2, band * 2 + 2).includes(chunk[slot + 2]));
                assert.ok(chunk[slot + 1] - 180 > 1244);
                assert.ok(rocks.every(rock => !near(chunk[slot], chunk[slot + 1], rock)));
            }
        }
    }
});

test('live terrain vetoes a fish spawn without affecting the other bands', () => {
    const { layout, load, blockers } = setup();
    const chunk = layout(seed, 4, start, 1244);
    blockers.push({ x: chunk[16], y: chunk[17], image_xscale: 1, image_yscale: 1, kind: 'CaveRockBig' });
    const owned = [];
    load(4, owned);
    assert.equal(owned.length, 6);
    assert.equal(owned.filter(member => member.jmk_endless_ocean_fish).length, 2);
    assert.ok(owned.filter(member => member.jmk_endless_ocean_fish).every(member => member.catalog_index >= 0));
});

test('streaming creates, retains and releases only the requested chunks', () => {
    const { stream, live, load } = setup();
    const map = new Map();
    const visits = [];
    const create = (index, owned) => { visits.push(index); load(index, owned); };
    stream(map, 0, 2, create);
    assert.deepEqual([...map.keys()], [0, 1, 2]);
    const firstVisit = map.get(1).map(member => ({ ...member }));
    const retained = map.get(1);
    stream(map, 1, 5, create);
    assert.equal(map.get(1), retained);
    assert.deepEqual(visits, [0, 1, 2, 3, 4, 5]);
    assert.equal(live.size, 35);
    stream(map, 485, 487, create);
    assert.deepEqual([...map.keys()], [485, 486, 487]);
    assert.equal(retained.length, 0);
    assert.equal(live.size, 21);
    stream(map, 0, 2, create);
    assert.deepEqual(map.get(1), firstVisit);
    stream(map, 0, -1, create);
    assert.equal(map.size, 0);
    assert.equal(live.size, 0);
});
