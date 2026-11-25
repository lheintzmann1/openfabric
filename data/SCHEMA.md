# OpenFabric data contract

All gameplay content lives in `data/*.json` (comments `//` and trailing commas allowed). The simulation in
`src/OpenFabric.Core` implements only the **kinds** below; content must stick to them. Field names are camelCase.
Schemas: `src/OpenFabric.Core/Data/Defs.cs`. Validation: `GameData.Validate()` (test `DataIsConsistent`).

Units: items/s, tiles, seconds, kW (power), MJ (energy). Fluids are items with `"fluid": true`; amounts are "units"
(recipes use units per craft; pipes/tanks hold units). Icons are Material Icons names (see
`src/OpenFabric.Game/Assets/fonts/MaterialIcons-Regular.codepoints`; validate every icon name exists).

## Placement / logistics model (important for balance)
* No inserters needed: a machine **takes** items from any belt/loader/pipe that faces *into* one of its tiles, and
  **pushes** outputs into any adjacent belt (not facing into it), loader-less chest, or acceptor machine.
* Belt: 4 items per tile; throughput = 4 x `speed` items/s. Mk1 speed 1.875 (7.5/s), Mk2 3.75 (15/s), Mk3 5.625 (22.5/s),
  Mk4 7.5 (30/s), Mk5 11.25 (45/s).
* Fluids: adjacent pipes + fluid buildings form a network; pipe `Props.flow` = units/s capacity.

## Building kinds (`kind`) and their `props`
| kind | meaning | props |
|---|---|---|
| `hub` | HQ (3x3). Storage + milestone terminal | `slots` |
| `drill` | mines solid resource tiles under its footprint, outputs items | `rate` items/s at full output (per whole drill); `fuelKw` >0 => burner drill (consumes fuel items, no electricity); drills extracting fluids (pumpjack/gas) set `fluidOnly: 1` and mine `kind: fluid` resources |
| `pump` | pulls water from water terrain under footprint | `rate` units/s; `fluid` field = "water" |
| crafter | **any building whose `kind` equals some recipe's `machine`** runs those recipes. `speed` multiplies recipe time^-1; `powerKw` electric draw; `fuelKw`>0 => burner (needs fuel items, e.g. stone furnace) | optional `autoRecipe: 1` (picks recipe from inputs, e.g. furnaces), `slotsIn`, `modules` |
| `belt` | conveyor | `speed` tiles/s |
| `splitter` | 1x1 router: input from back, outputs round-robin to 3 other sides | none |
| `merger` | accepts from 3 sides, outputs forward | none |
| `underground` | belt tunnel; entrance+exit pair in line | `range` (tiles), `speed` |
| `loader` | 1x1 arm: moves items/fluid from the tile behind to the tile in front | `rate` items/s |
| `chest` | item storage; accepts from belts/loaders | `slots` |
| `sink` | destroys items, converts to `Props.points` research coupons... (kind `sink`) | `rate` |
| `pole` | power pole | `radius` (supply area radius), `link` (max distance to other poles) |
| `generator_fuel` | burns items with `fuelMJ` (restricted by `accepts` if non-empty) | `outputKw` |
| `generator_fluid` | consumes fluid `fluid` | `outputKw`, `fluidPerSec` |
| `generator_passive` | constant output (solar x daylight); `requires` a resource under footprint (geothermal) | `outputKw`, `daylight` (1=solar) |
| `generator_reactor` | consumes fuel-rod items (`accepts`) with `fuelMJ`, plus coolant `fluid` at `fluidPerSec`, emits `waste` item | `outputKw` |
| `battery` | stores energy | `capacityMJ`, `rateKw` |
| `pipe` | fluid conduit | `flow`, `capacity` |
| `tank` | fluid storage | `capacity` |
| `lab` | consumes science packs for the current research | `speed` |
| `link_port` | long-distance item link (cargo rail / drones): same `channel` ports exchange items | `rate` items/s, `range` tiles |

Every building needs: `id, name, icon, color, category, kind, w, h, cost`. Categories (build menu tabs):
`production, logistics, power, fluids, storage, research, special`. Buildings with `"start": true` are available at game start.

## Recipes
`machine` = building kind that crafts it, or `"hand"`... use `"hand": true` for player hand-crafting (also give `machine`
of the building kind if both). Alternate recipes: `"alternate": true`, unlocked by research/milestones only.
Every non-`start` recipe/building must be unlocked by a milestone or research (test enforces this).
Every item must be obtainable via the recipes available at or before its tier (test enforces this).

## Progression
* `tiers.json`: array of `{index, name, description, milestones:[{id,name,description,cost,unlocks}]}`.
  Tier N+1 milestones become available when ALL tier N milestones are done. Players submit `cost` items from their
  inventory at the hub. `unlocks`: building ids and recipe ids.
* `research.json`: tree of nodes (`requires`, `cost` in science-pack items, `time`, `unlocks`, `mods`). Needs a `lab`.
  Science packs are ordinary items crafted in factories. Allowed `mods` keys: `beltSpeed`, `drillSpeed`, `craftSpeed`,
  `inventorySlots`, `mineSpeed`, `powerEff` (additive fractions, e.g. 0.25 = +25%), `handCraftSpeed`.
* Tier 0 must be completable using only `start` content.

## Resources (world gen) `resources.json`
`item` must exist; `kind: fluid` for oil/gas/geothermal vents (point wells). Resource items used by `requires`
(e.g. `geothermal_vent`) must exist as items too.
