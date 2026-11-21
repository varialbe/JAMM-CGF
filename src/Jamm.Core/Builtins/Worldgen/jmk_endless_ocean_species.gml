// Seed, content-relative chunk, depth band, biome, nearby wreck.
var seed = argument0;
var index = argument1;
var band = argument2;
var biome = argument3;
var roll = jamm_chunk_unit(seed, index, 50 + band);
var pool;
if (band == 0)
{
    if (biome == 0)
    {
        pool[0] = Swooper;
        pool[1] = Roundfin;
        pool[2] = Garump;
        pool[3] = Spinefish;
        pool[4] = Yellowjack;
        pool[5] = Puff;
    }
    else
    {
        pool[0] = Celad;
        pool[1] = Inno;
        pool[2] = Garder;
        pool[3] = Pink_Kisser;
        pool[4] = Pennyfin;
        pool[5] = Lionfish;
    }
}
else if (band == 1)
{
    if (biome == 0)
    {
        pool[0] = Mustardfish;
        pool[1] = Elder_Mustardfish;
        pool[2] = Felish;
        pool[3] = Torby;
        pool[4] = Purdy;
        pool[5] = Leofish;
    }
    else
    {
        pool[0] = Sassy;
        pool[1] = Boom_Puff;
        pool[2] = Dijon;
        pool[3] = Stankfin;
        pool[4] = Cavin;
        pool[5] = Twohead;
    }
}
else
{
    if (argument4) return index < 72 ? Geiger : Ancient_Kingfish;
    var rare = jamm_chunk_unit(seed, index, 54);
    if (index >= 72 && biome == 2 && rare < min(0.22, 0.04 + (index - 72) / 1600))
        return rare < 0.04 ? Ancient_Kingfish : Underfin;
    if (biome == 0)
    {
        pool[0] = Dijon;
        pool[1] = Twohead;
        pool[2] = Garm;
        pool[3] = Jackfish;
        pool[4] = Primal_Turgeon;
        pool[5] = Mudback;
    }
    else
    {
        pool[0] = Garm;
        pool[1] = Cuddlecanth;
        pool[2] = Buffalofish;
        pool[3] = Mudback;
        pool[4] = Primal_Turgeon;
        pool[5] = biome == 2 ? Geiger : Dijon;
    }
}
return pool[floor(roll * 6)];
