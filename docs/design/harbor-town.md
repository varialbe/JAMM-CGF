# Harbor Town – design (v0.1 → v1.0)

Status: design draft by the orchestrator, 2026-11-21. Separate, optional ModKit/API mod `harbor-town`
(`examples/modkit/harbor-town`), never enabled by default, not part of any other mod.

## Vision
Late-game Cat Goes Fishing has nothing left to buy: once the best rod, boat and upgrades are owned, money
only piles up (and with JAMM big money it can now pile up to $1.0Qa). Harbor Town turns the dock into a small
town the player grows with that money. Every building has unlimited levels, grows visibly, and gives either
purely cosmetic prestige or small, hard-capped comfort bonuses. A player with $1T has clear goals for many
hours; nothing ever trivializes fishing.

Design rules:
* **Vanilla look.** All art is composed at build time from the player's own vanilla sprites (dock houses,
  shop signs, rocks, lights, boat parts) via sprite recipes, like the Silo fish. Flat CGF style, vanilla
  outline width and palette, no seams, no foreign colours, no shipped vanilla pixels.
* **Never OP.** Bonuses grow logarithmically and stop at a hard cap; the fair-play guard stays meaningful.
* **No grind walls.** Costs grow exponentially up to a knee, then linearly, so “unlimited levels” stays
  reachable below the $1.0Qa wallet cap.
* **Small footprint.** Buildings sit on the land west of the dock. The cat's walking path, the shops and the
  boat launch stay free; collisions are never added on the path.

## Buildings
| Building | Effect per level | Visible growth | v0.1 |
|---|---|---|---|
| Lighthouse | Lure light radius at night and in deep water +3 % per level, logarithmic, capped at +40 % (uses the vanilla lure/Geiger light system, no new shader) | taller tower, more windows, rotating beam at night | ✅ |
| Aquarium | Shows caught species alive: 1 tank per 2 levels, each tank shows up to 3 caught species swimming (vanilla fish sprites/animation, no AI) | wider building, more tank windows | ✅ (1 tank, up to 6 species) |
| Golden cat statue | Pure prestige; title text under the wallet at milestones | bigger, more golden (palette shift towards the vanilla gold of coins/rewards), pedestal tiers | ✅ |
| Research station | Sell price, bite rate and lure luck, logarithmic, hard caps (table below) | antenna/dish parts from vanilla props | v0.2 |
| Shipyard | Cosmetic boat colours/designs (palette variants of the owned boat) | slipway, crane | v0.3 |

### Cost curve
`cost(L) = base × 1.6^L` up to the knee `L = 40`, then `cost(L) = base × 1.6^40 × (1 + 0.25 × (L − 40))`.
Displayed with the compact money format (`number_to_dollar_string` from big money).

| Level | Lighthouse | Aquarium | Research station | Shipyard | Golden cat statue |
|---:|---:|---:|---:|---:|---:|
| 0 | $25.0K | $40.0K | $100.0K | $15.0K | $10.0K |
| 1 | $40.0K | $64.0K | $160.0K | $24.0K | $16.0K |
| 5 | $262.1K | $419.4K | $1.0M | $157.2K | $104.8K |
| 10 | $2.7M | $4.3M | $10.9M | $1.6M | $1.0M |
| 15 | $28.8M | $46.1M | $115.2M | $17.2M | $11.5M |
| 20 | $302.2M | $483.5M | $1.2B | $181.3M | $120.8M |
| 25 | $3.1B | $5.0B | $12.6B | $1.9B | $1.2B |
| 30 | $33.2B | $53.1B | $132.9B | $19.9B | $13.2B |
| 35 | $348.4B | $557.5B | $1.3T | $209.0B | $139.3B |
| 40 | $3.6T | $5.8T | $14.6T | $2.1T | $1.4T |
| 50 | $12.7T | $20.4T | $51.1T | $7.6T | $5.1T |
| 75 | $35.6T | $56.9T | $142.4T | $21.3T | $14.2T |
| 100 | $58.4T | $93.5T | $233.8T | $35.0T | $23.3T |

All five buildings to level 100 cost about $14.4Qa in total, so a $1T player buys ~level 35–40 everywhere and
keeps a long linear tail; a $1Qa player still has goals. The vanilla economy (best rod/boat in the low millions)
is untouched.

### Research bonuses (v0.2)
`bonus(L) = min(cap, a × ln(1 + L))`

| Research level | Sell price (a=2, cap 10 %) | Bite rate (a=1.5, cap 8 %) | Lure luck (a=1, cap 5 %) |
|---:|---:|---:|---:|
| 1 | +1.4 % | +1.0 % | +0.7 % |
| 5 | +3.6 % | +2.7 % | +1.8 % |
| 10 | +4.8 % | +3.6 % | +2.4 % |
| 20 | +6.1 % | +4.6 % | +3.0 % |
| 50 | +7.9 % | +5.9 % | +3.9 % |
| 100 | +9.2 % | +6.9 % | +4.6 % |
| ≥ 200 | +10.0 % | +8.0 % | +5.0 % |

Sell price bonus applies after the 100M fish value cap, so a single fish never exceeds $110M.

### Donation milestones (v0.2)
Donate to the town (money disappears, nothing else): $1M, $1B, $1T, $1Qa unlock a hat recolour, a title under the
wallet (“Harbor Patron”, “Harbor Baron”, “Harbor Tycoon”, “Harbor Legend”) and dock decorations (flags, lanterns).

### Expeditions (v0.4, needs Endless Ocean)
Pay for fuel/supplies to start at a chosen distance east in Endless Ocean: price `5M × 1.35^(km)`; rare fish and
POIs are more likely the further out. Requires the Endless Ocean floating origin from its v2 plan.

## Interaction / UI
* A vanilla-style sign post (“Harbor Town”) near the dock. Walking the cat to it and pressing the shop key opens a
  menu built like the vanilla shop cards (same panel sprite, fonts `fnt_babySmall`/`fnt_babyMoney`, colours from
  `Colors`): one card per building with level, next cost, effect and a Buy button. Gamepad/keyboard like vanilla shops.
* Console `harbor` (JAMM Console present): `harbor` lists levels; `harbor <building> <level>` sets a level;
  `harbor money <n>` gives money for tests.

## Required JAMM APIs
| API | Exists? | Needed for |
|---|---|---|
| Area API (pieces, anchors, follow) | yes (Silo) | placing building pieces on the dock land |
| Sprite recipes | yes (fish, silo) | composing buildings from vanilla parts |
| Items API (shop cards) | partly (rods/boats/upgrades) | reusing the vanilla card look for the menu |
| **Building API** (new) | no | `jamm_building_register(id, name, base_cost, growth, knee, max_visual_stage, effect_script)`, level storage, buy logic, growth stages → sprite swap, decision lines; documented so other mods can add buildings |
| **Aquarium display** (new, small) | no | list of caught species from the Fishalog/catalog data, spawn display-only fish instances (no AI, no catch) inside tank bounds |
| Expedition API (new, later) | no | depends on Endless Ocean floating origin |
| Money format | yes (big money) | `number_to_dollar_string` |

## Save format
Per save slot, in the vanilla save INI next to the slot's other data: section `JAMM_Harbor`, keys
`<building>=<level>` (integers), `donated=<real>`. Missing section = all zero. A save opened without the mod
keeps the keys untouched (vanilla ignores unknown sections).

## Phase plan
| Version | Content | Exit criteria |
|---|---|---|
| v0.1 | Sign post + menu, Lighthouse, Aquarium (1 tank), Golden statue, unlimited levels, cost curve, save/load, console `harbor` | builds solo + personal build, no CODE ERROR, art review ≥ 2 rounds, screenshots per stage |
| v0.2 | Research station with capped bonuses, donation milestones + titles | balancing check with $1B/$1T saves |
| v0.3 | Shipyard cosmetics | palette variants verified on all vanilla boats |
| v0.4 | Expeditions into Endless Ocean | needs Endless Ocean v2 floating origin |
| v1.0 | Polish, mod-site card + detail page, docs | user review |

## Risks
* **Art quality** is the main risk (user rejected “AI slop” before). Mitigation: recipes only from vanilla parts,
  side-by-side reviews next to the vanilla dock buildings at 1× zoom, iterate.
* **Dock space / collisions** with other mods that edit the dock (Cave/Verdant/Colour). Mitigation: Area API
  `follow` anchors, no solid pieces, compatibility check in all-7.
* **Performance:** aquarium fish are display-only and capped (≤ 18 instances), no AI step.
* **Balance:** bonuses capped; sell bonus applied after the fish value cap.
* **Save compatibility:** own INI section only.
