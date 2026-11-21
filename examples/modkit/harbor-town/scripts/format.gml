/// format(dollars)
var compact = asset_get_index("jamm_money_format");
if (compact >= 0) return script_execute(compact, argument0);
var value = abs(floor(argument0));
if (value < 10000) return number_to_dollar_string(floor(argument0));
var unit = "K";
var divisor = 1000;
if (value >= 1000000) { unit = "M"; divisor = 1000000; }
if (value >= 1000000000) { unit = "B"; divisor = 1000000000; }
if (value >= 1000 * 1000000000) { unit = "T"; divisor = 1000 * 1000000000; }
if (value >= 1000000 * 1000000000) { unit = "Qa"; divisor = 1000000 * 1000000000; }
var tenths = floor(value / (divisor / 10));
return "$" + string_repeat("-", argument0 < 0) + string(floor(tenths / 10)) + "." + string(tenths mod 10) + unit;
