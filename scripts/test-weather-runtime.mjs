// Runs the branching and stateful parts of the shipped legacy GML with game primitives stubbed.
// This fixture maps the GM 1.4 array and modulo syntax used by these specific scripts.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { runInNewContext } from 'node:vm';

const directory = new URL('../src/Jamm.Core/Building/Weather/', import.meta.url);

function attach(scope, ...scripts) {
    for (const script of scripts) {
        const source = readFileSync(new URL(`${script}.gml`, directory), 'utf8')
            .replace(/\bmod\b/g, '%')
            .replace(/\[([^\[\],]+), ([^\[\],]+)\]/g, '[$1][$2]');
        scope[`jamm_weather_${script}`] = runInNewContext(`(function(argument0, argument1) { ${source}\n })`, scope);
    }
}

function forecast(seed, day, rules) {
    const scope = {
        floor: Math.floor, min: Math.min, weather_seed: seed, weather_day: day,
        weather_count: rules.length, weather_rule: rules
    };
    attach(scope, 'schedule', 'active');
    return scope;
}

test('a slot and day choose the same weighted forecast without using the game RNG', () => {
    const rules = [[3, 7, 240, 400], [8, 2, 600, 1200], [5, 8, 100, 300], [0, 7, 100, 300]];
    const outcomes = new Set();
    for (let day = 1100000; day < 1100064; day++) {
        const first = forecast(698541, day, rules);
        const repeat = forecast(698541, day, rules);
        first.jamm_weather_schedule();
        repeat.jamm_weather_schedule();
        const key = scope => [scope.weather_plan, scope.weather_begin, scope.weather_finish];
        assert.deepEqual(key(first), key(repeat));
        outcomes.add(first.weather_plan);
        if (first.weather_plan === 1) assert.ok(first.weather_begin >= 90 && first.weather_begin < 2940);
        if (first.weather_plan === 2) assert.ok(first.weather_begin >= 3060 && first.weather_begin < 3600);
        if (first.weather_plan >= 0) assert.ok(first.weather_finish > first.weather_begin && first.weather_finish <= 3600);
    }
    assert.deepEqual([...outcomes].sort(), [-1, 0, 1, 2]);
});

test('slot load restores the seed and forecast by qualified id, including after load order changes', () => {
    const files = {
        'jamm-weather-7.ini': { seed: 589, day: 1100000, id: 'other:rainy', start: 120, end: 870 },
        'jamm-weather-8.ini': { seed: 6758, day: 1100000, id: 'pack:rainy', start: 170, end: 970 }
    };
    let opened;
    const scope = forecast(0, 0, [[1, 7, 1, 2], [1, 7, 1, 2]]);
    Object.assign(scope, {
        weather_key: ['other:rainy', 'pack:rainy'], weather_short: ['rainy', 'rainy'],
        string: String, ini_open: file => { opened = files[file]; }, ini_close: () => { opened = undefined; },
        ini_read_real: (_, key, fallback) => opened[key] ?? fallback,
        ini_read_string: (_, key, fallback) => opened[key] ?? fallback
    });
    attach(scope, 'slot', 'find');
    scope.weather_slot_number = 7;
    scope.jamm_weather_slot();
    assert.equal(scope.weather_seed, 589);
    assert.equal(scope.weather_plan, 0);
    assert.equal(scope.weather_begin, 120);
    assert.equal(scope.weather_finish, 870);
    scope.weather_key.reverse();
    scope.jamm_weather_slot();
    assert.equal(scope.weather_plan, 1);
    scope.weather_slot_number = 8;
    scope.jamm_weather_slot();
    assert.equal(scope.weather_seed, 6758);
    assert.equal(scope.weather_plan, 0);
    assert.equal(scope.jamm_weather_find('rainy'), -2);
    assert.equal(scope.jamm_weather_find('other:rainy'), 1);
});

test('dusk, clear, scheduled boundaries and explicit night opt-in use the same activation path', () => {
    const scope = forecast(28, 1100000, [[2, 7, 120, 300], [3, 8, 120, 300]]);
    Object.assign(scope, { weather_plan: 0, weather_override: -2, weather_second: 450,
        weather_begin: 460, weather_finish: 720, weather_night: false, weather_phase: 2 });
    assert.equal(scope.jamm_weather_active(), -1);
    scope.weather_second = 460;
    assert.equal(scope.jamm_weather_active(), 0);
    scope.weather_second = 720;
    assert.equal(scope.jamm_weather_active(), -1);
    scope.weather_override = 0;
    assert.equal(scope.jamm_weather_active(), 0);
    scope.weather_night = true;
    scope.weather_phase = 8;
    assert.equal(scope.jamm_weather_active(), -1);
    scope.weather_override = 1;
    assert.equal(scope.jamm_weather_active(), 1);
    scope.weather_override = -1;
    assert.equal(scope.jamm_weather_active(), -1);
});

test('weather parks eligible vanilla fish once and restores them when cleared', () => {
    const reactivated = [];
    const deactivated = [];
    const parked = [];
    const scope = {
        instance_exists: () => true, variable_instance_exists: () => false, state: 0, id: 901,
        stayOutAtNight: false, TRUE_START_Y: 1410, y: 1490, object_index: 14,
        ds_map_exists: (map, id) => map.has(id),
        ds_list_find_index: (list, id) => list.indexOf(id), ds_list_add: (list, id) => list.push(id),
        ds_list_size: list => list.length, ds_list_find_value: (list, index) => list[index],
        ds_list_clear: list => { list.length = 0; },
        instance_deactivate_object: id => deactivated.push(id), instance_activate_object: id => reactivated.push(id),
        weather_hook: [[-1, -1, -1]], weather_live: 0, weather_alpha: 1, weather_parked: parked,
        weather_voice: [-1, -1, -1, -1], weather_voice_config: [-1, -1, -1, -1],
        weather_voice_time: [0, 0, 0, 0], weather_voice_gain: [0, 0, 0, 0], current_time: 1500,
        jamm_weather_population: () => 0,
        obj_jamm_weather: {
            weather_live: 0, weather_rule: [[1, 7, 1, 1, 1, 1]], weather_night: false,
            weather_vanilla_fish: new Set([14]), weather_parked: parked
        }
    };
    Object.assign(scope, { weather_rule: scope.obj_jamm_weather.weather_rule });
    attach(scope, 'gate', 'change', 'restore_fish');
    scope.obj_jamm_weather.weather_rule[0][5] = 0;
    assert.equal(scope.jamm_weather_gate(), false);
    scope.obj_jamm_weather.weather_rule[0][5] = 1;
    assert.equal(scope.jamm_weather_gate(), true);
    assert.equal(scope.jamm_weather_gate(), true);
    assert.deepEqual(parked, [901]);
    assert.deepEqual(deactivated, [901, 901]);
    scope.id = 902;
    scope.state = 3;
    assert.equal(scope.jamm_weather_gate(), false);
    scope.state = 0;
    scope.obj_jamm_weather.weather_night = true;
    assert.equal(scope.jamm_weather_gate(), false);
    scope.jamm_weather_change(-1, true);
    assert.deepEqual(reactivated, [901]);
    assert.deepEqual(parked, []);
});
