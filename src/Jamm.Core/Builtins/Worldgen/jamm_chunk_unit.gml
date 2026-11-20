// Seed, nonnegative sample index, nonnegative channel. No global RNG state.
var modulus = 2147483647;
var h = (real(argument0) + real(argument1) * 104729 + real(argument2) * 8191) % modulus;
var low = h % 65521;
h = (h * 48271 + low * low) % modulus;
low = h % 65521;
return ((h * 48271 + low * low) % modulus) / modulus;
