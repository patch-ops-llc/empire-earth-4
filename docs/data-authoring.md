# Data authoring guide

Every number, table and roster entry of the game lives in data files under `game/data/`. This guide says how the files are written, what each one holds and how to check them.

## Check your edit

```sh
dotnet run --project src/Tools -- datacheck game/data
```

`datacheck` loads the folder through the same loader the game uses, so data it accepts is data the game can run. It prints every problem it finds in one run, one per line, as the file, the place in the file and what is wrong:

```
game/data/lines/guard.jsonc: $.base.hp: must be above 0 and at most 1000000, found 0
game/data/maps/ridge.jsonc: $.deposits: needs 4 contested deposits, found 3
datacheck: 2 errors in game/data.
```

It exits with 0 when the data is valid, 1 when it found errors and 2 when it could not run, for example because the folder does not exist. CI runs it on every pull request.

## See what an edit does

```sh
dotnet run --project src/Tools -- balance-report swarm=1,1,2,4,4,2 elite=4,4,2,1,1,2
```

`balance-report` prints what the rules make of one or more tier mixes, computed by the same code the game runs. A mix is one tier per track in the order of `tracks.jsonc`, with an optional name before `=`. Without a mix it reports the start of a match. The output is three CSV tables with an empty line between them:

| Table | One row per | Shows |
| --- | --- | --- |
| `mixes` | mix | The tiers, the epoch, the supply cap and the income multiplier. |
| `research` | mix and track | The next tier: whether it may be researched, its price band, price and time. |
| `chassis` | mix and chassis | Whether it is unlocked, its offsets and accents, and every stat and price, with perks applied. |

| Option | Meaning |
| --- | --- |
| `--table mixes`, `research` or `chassis` | Print only that table, for example to redirect it into a file for a spreadsheet. |
| `--epoch-cap <tier>` | Use this epoch cap in place of `match.epoch_cap`. |
| `--data <folder>` | Read another data folder. The default is `game/data`. |

Hit points, damage and distances are shown to three decimal places and times in seconds. The game keeps hit points and damage in thousandths, distances in 1/65,536 of a tile and times in ticks.

## Files

| File | Holds | Schema |
| --- | --- | --- |
| `tracks.jsonc` | The tracks in their fixed order, the epoch names and the tier names. | `schemas/tracks.schema.json` |
| `rules.jsonc` | The constants of the rule set, the roster of line and structure IDs, and the rules every map must satisfy. | `schemas/rules.schema.json` |
| `research.jsonc` | What each tier of a track costs to research and how long it takes. | `schemas/research.schema.json` |
| `accents.jsonc` | The accent each track gives a unit, and the perks of its two levels. | `schemas/accents.schema.json` |
| `resources.jsonc` | What a worker earns at a deposit or a Generator, how many workers each takes, and the worker itself. | `schemas/resources.schema.json` |
| `lines/<id>.jsonc` | One line of units: its tracks, tier-1 values, attack, counters and chassis. | `schemas/line.schema.json` |
| `structures/<id>.jsonc` | One structure: its footprint, price, hit points, build time and sight, and what it does. | `schemas/structure.schema.json` |
| `maps/<id>.jsonc` | One map: terrain, starting positions, deposits and markers. | `schemas/map.schema.json` |

The loader reads exactly these, and the first five are required. It needs at least one line and one map. Structure files are optional: a structure of the roster may have no file yet, and a data folder may have none at all. Any other file in the folder is an error, so a misnamed file is noticed and not skipped. Files and folders whose names start with a dot are ignored.

More kinds of file arrive with the milestones that need them. Adding a kind means adding its reader in `src/Sim/Data/`, its schema in `schemas/`, a row in the table above and tests.

## Conventions

- **JSON with comments.** `//` and `/* */` comments are allowed anywhere. Trailing commas and repeated keys are errors.
- **IDs** are lowercase letters, digits and underscores, start with a letter and have at most 40 characters: `cavalry_armor`. A line's or map's `id` must equal its file name.
- **Unknown keys are errors.** A misspelled key is reported and never ignored.
- **Whole numbers are bare; fractions are text.** Write `130` and `"2.6"`. A bare `2.6` is an error. The loader reads each number from its text into an integer and a count of decimal places, so no value ever passes through a floating-point type and every machine reads the same value. Text numbers are plain decimal notation: an optional minus sign, digits, and optionally a point and up to 9 more digits, with at most 18 digits in total and no exponent. Where a fraction is allowed a whole number may be written either way: `1` and `"1"` are the same.
- **Times are in seconds**, as in the game design, and must be a whole number of ticks. There are 20 ticks per second, so `"1.5"` and `"0.05"` are fine and `"0.33"` is an error. The loader converts them to ticks.
- **Distances are in tiles** and speeds in tiles per second.
- **`$schema`** as the first key of a file points an editor at the file's schema. VS Code then offers autocomplete and underlines mistakes as you type. It is not data: the loader accepts it and leaves it out of the content hash.

## tracks.jsonc

Game design 1.1 and 1.2.

| Key | Meaning |
| --- | --- |
| `epochs` | The name of each epoch, epoch 1 first. Its length is the number of tiers every track has. |
| `tracks` | The tracks. **Their order in the file is the fixed track order** the rules use, for example as the last tie-break when a unit picks its accents. |
| `tracks[].id` | The track's ID. Six IDs are required, because the rules name them: `firepower`, `protection`, `mobility`, `manufacturing`, `logistics` and `extraction`. |
| `tracks[].name`, `short` | Its display name and abbreviation. |
| `tracks[].group` | `better`, `more` or `richer`. |
| `tracks[].tiers` | The name of each tier, tier 1 first: one per epoch. |

The formulas say which track scales what: Firepower scales damage, Logistics sets the supply cap and so on. The code finds each of those tracks by its ID, so renaming one of the six IDs is a code change, while names, abbreviations, tier names and the order are free. More tracks may be listed; they count toward the epoch and can be a line's primary or secondary track.

## rules.jsonc

The values the design says to tune first. Each block carries a comment naming its design section.

| Key | Meaning | Design |
| --- | --- | --- |
| `roster.lines`, `roster.structures` | Every line and structure ID the design names. Files may refer to these before the line or structure has a file of its own. `structures` is reserved as a counter target and cannot be a line ID. | 2.2, 2.5 |
| `epoch.tracks_needed`, `epoch.reach` | The epoch is the highest tier that this many tracks have reached; no track may be more than `reach` tiers above it. | 1.4 |
| `research_price.catch_up`, `frontier`, `ahead` | Multipliers on a research's `cost` and `time` at or below the epoch, one above, and further above up to `reach`. | 1.3 |
| `effective_tier_window` | A chassis uses track tiers within this many tiers of its own epoch. | 2.3 |
| `per_tier.damage`, `hp`, `speed`, `income` | What one tier of Firepower, Protection, Mobility and Extraction multiplies. | 2.3, 4.2 |
| `per_step.quality_cost`, `manufacturing_cost`, `manufacturing_time` | What one step of offset multiplies in a unit's price and build time. | 2.3 |
| `accent_slots` | Most accents one unit carries. | 2.3 |
| `supply_cap.base`, `per_logistics_tier` | The supply cap is the base plus this much per Logistics tier. | 4.1 |
| `wall_crossing_cost` | Tiles of walking that crossing one enemy wall tile costs in the second path search. | 2.5 |
| `match.start_epoch`, `epoch_cap` | The tier all tracks start at and the tier none may pass. | 5.2 |
| `start.workers`, `materials`, `energy` | What every player starts with, besides a Capitol. | 5.2 |
| `map` | The map rules below. | 5.3, 5.4 |
| `combat.look_interval_ticks` | A unit or a Tower without a target looks for one every this many ticks. | 2.4 |
| `combat.unit_reach_margin`, `structure_reach` | Melee reach in tiles: against a unit, the spacing between the two units plus the margin; against a structure, the reach to the nearest edge of its footprint. | 2.4 |
| `combat.fortification` | What multiplies the damage a structure takes from an attacker whose effective Firepower is below the Protection tier of the structure's owner. Above 0 and at most 1. | 2.5 |
| `debug.spawn` | `true` or `false`: whether a match carries out the development spawn, a command that makes units of any chassis from nothing for the player who sends it, and the development placement, which lays a finished structure the same way. Neither is in the design. It is on in `game/data/` until units can be trained and structures built in a match, and stays on in `tests/golden/data/`, whose replays with a fight and with Towers need it. Both players of a match have the same data, so they have the same switch. | |

The footprint and the territory radius of a start are not here: they are the `footprint` and `territory_radius` of `structures/capitol.jsonc`, which every data folder must have. The map checks read them from that file, and a match places that structure, finished, at each player's start.

## research.jsonc

Game design 1.3. Every track uses the same table.

| Key | Meaning |
| --- | --- |
| `tiers` | One entry for each tier from 2 to the last, in that order. Tier 1 is where every track starts and has no entry. |
| `tiers[].tier` | The tier this research reaches. It is written out so that a row cannot slip. |
| `tiers[].cost.materials`, `energy` | The price one tier above the epoch. Whole resources; either may be 0. |
| `tiers[].seconds` | The research time one tier above the epoch. |

The price a player pays is this price times a multiplier from `rules.jsonc` `research_price`, rounded half up to a whole resource, Materials and Energy each on its own. The time is multiplied the same way and rounded half up to a tick, and is never less than one tick.

## accents.jsonc

Game design 2.3. A unit whose chassis uses a track above the chassis's own epoch carries that track's accent: level I at one tier above, level II at two. Level II includes level I.

| Key | Meaning |
| --- | --- |
| `accents` | The accents, keyed by track ID. A track without an entry gives no accent, as Extraction does not. |
| `accents.<track>.name` | The accent's display name. The interface adds I or II. |
| `accents.<track>.level_1`, `level_2` | The perks each level adds, keyed by kind of perk. An empty object is allowed. |

A kind of perk is a rule in the code with numbers from this file. Which accent gives a perk, at which level and how strongly is data; what the perk does and which units it applies to is code. Each kind may appear once in the file, since a unit with the same kind twice would need a rule for how the two combine.

| Kind | Keys | What it does | Applies to |
| --- | --- | --- | --- |
| `range_bonus` | `multiplier`, 1 or more | Multiplies attack range. | Ranged attacks |
| `attack_rate_bonus` | `multiplier`, 1 or more | Multiplies attacks per second. | Melee attacks |
| `splash` | `share`, `radius` | Each hit also deals `share` of its damage to every other enemy within `radius` tiles of the target. | Attacks without a blast radius |
| `blast_radius_bonus` | `multiplier`, 1 or more | Multiplies the blast radius. | Attacks with a blast radius |
| `area_damage_reduction` | `multiplier`, above 0 to 1 | Multiplies area damage taken. | Every unit |
| `plating` | `multiplier`, above 0 to 1 | Multiplies damage taken from an attacker whose effective Firepower is below this unit's effective Protection. | Every unit |
| `sight_bonus` | `tiles`, whole | Adds to sight. | Every unit |
| `charge` | `distance`, `multiplier` 1 or more | The first hit after moving at least `distance` tiles deals `multiplier` times the damage. | Units with an attack |
| `batch` | `time_multiplier` | Each queue slot produces two units in `time_multiplier` times the build time of one. | Every unit |
| `death_refund` | `share`, above 0 to 1 | Returns `share` of the unit's Materials price when it dies. | Every unit |
| `regeneration` | `share_per_second`, `delay_seconds` | Heals `share_per_second` of full hit points each second once the unit has taken no damage for `delay_seconds`. | Every unit |
| `supply_reduction` | `amount`, `minimum` | Lowers supply by `amount`, but not below `minimum`. A unit already below `minimum` keeps its supply. | Every unit |

A multiplier is what the value is multiplied by: 15% more range is `"1.15"`, a quarter less area damage is `"0.75"` and 30% less damage is `"0.7"`.

## resources.jsonc

Game design 4.1. Income is a fixed rate: a worker standing at its place at a deposit or a Generator is paid once a second, and walks nowhere to deliver.

| Key | Meaning |
| --- | --- |
| `materials.per_worker_per_minute` | What one worker at a deposit earns in a minute at Extraction tier 1. |
| `materials.workers_per_deposit` | The workers that can work one deposit at once. Others wait beside it. |
| `materials.full_rate_amount` | The Materials a deposit delivers at the whole rate, in whole resources, Extraction bonus included. |
| `materials.depleted_rate` | The share of the rate a deposit pays after that, above 0 and at most 1. A deposit never runs dry. |
| `energy.per_worker_per_minute` | What one worker at a Generator earns in a minute at Extraction tier 1. |
| `energy.workers_per_generator` | The workers that can work one Generator at once. |
| `worker.hp` | Hit points at Protection tier 1. |
| `worker.speed`, `sight`, `spacing` | Tiles per second, and tiles. No tier changes them. |
| `worker.supply` | The supply one worker uses. |
| `worker.cost.materials`, `energy` | The price of a worker, in whole resources. |
| `worker.build_seconds` | The time a Capitol takes to train one. |

One payment is the rate for a minute divided by 60, times the Extraction multiplier (`per_tier.income` in `rules.jsonc`, once per tier above the first), times `depleted_rate` for a deposit that had delivered its `full_rate_amount` before the payment. The product is exact and is rounded once, half up, to a thousandth of a resource, which is what stockpiles count in. At the design's numbers a worker is paid 0.75 Materials or 0.5 Energy a second at tier 1.

A deposit has as many places as tiles share a side with it, eight for the 2 by 2 of `map.deposit_size`, and a Generator has the same eight. `workers_per_deposit` and `workers_per_generator` say how many workers may use them, so a number above eight changes nothing for a deposit of that size.

The worker is not a chassis: it has no tiers, no accents and no counters, and its speed and sight never change. Only the speed and the supply are in use so far; hit points, sight, spacing, the price and the training time wait for the milestones that fight, see and train.

## lines/&lt;id&gt;.jsonc

Game design 2.2 and 2.4.

| Key | Meaning |
| --- | --- |
| `id` | The line's ID. It must be in `roster.lines`. |
| `primary`, `secondary` | Track IDs. The chassis of epoch T unlocks at primary tier T and secondary tier T - 1. |
| `built_at` | The structure that builds the line, from `roster.structures`. |
| `base` | Values at tier 1: `hp`, `dps`, `speed`, `sight`, `supply`, `spacing`, `cost.materials`, `cost.energy`, `build_seconds`. |
| `attack` | `kind` is `melee` or `ranged`; `interval_seconds` is the time between attacks. A ranged attack also needs `range` and `projectile_speed`. |
| `attack.min_range` | Optional, ranged only. Targets closer than this cannot be fired at. It must be below `range`. |
| `attack.blast_radius` | Optional, ranged only. Every enemy within this distance of the target takes the full damage of a hit. |
| `counters` | Optional. Damage multipliers by target: a line ID, or `structures` for every structure. |
| `cannot_hit` | Optional. Lines this line cannot attack. A line cannot be here and in `counters`. |
| `chassis` | The line's model for each epoch, listed by rising epoch, at most one per epoch. Gaps are allowed. Chassis IDs are unique across all lines. |
| `chassis[].attack` | Optional. Keys of `attack` that this chassis replaces. If it changes `kind`, the line's `range`, `projectile_speed`, `min_range` and `blast_radius` are not carried over. Each chassis states its own override; nothing is inherited from the chassis before it. |

## structures/&lt;id&gt;.jsonc

Game design 2.5. One file for each structure that is built so far: the eight of the first playable. The Airfield and the two strategic structures are in the roster and get their files with the milestones that build them.

| Key | Meaning |
| --- | --- |
| `id` | The structure's ID. It must be in `roster.structures`. |
| `track` | Optional. The track whose tier the structure's look shows. Leave it out for a structure that shows none, such as the Outpost and the Academy. |
| `unlock_tier` | Optional, needs `track`. The tier of that track the owner needs to place the structure, from 2. Without it the structure is there from the start, as all eight are. |
| `footprint` | The side of the square of tiles it stands on, 1 to 8. |
| `hp` | Hit points at Protection tier 1. Each Protection tier of the owner multiplies them by `per_tier.hp`. |
| `cost.materials`, `energy` | The price, in whole resources. For the wall, of one tile. |
| `build_seconds` | The time the structure builds itself in once its foundation is laid. No track changes it. |
| `sight` | Sight in tiles when finished, measured from the center. No track changes it. |
| `territory_radius` | Optional. The radius in tiles of the territory it gives when finished. |
| `research_slots` | Optional. Research slots it gives when finished. |
| `max_per_player` | Optional. The most a player may have, counting those under way. |
| `trains` | Optional. What it trains, in the order it is offered: `worker`, or a line ID from `roster.lines`. |
| `work_site` | Optional. `energy` makes it a place where workers earn Energy. How many it holds and what each earns are in `resources.jsonc`. |
| `attack` | Optional. `dps` at Firepower tier 1, `interval_seconds`, `range` and `projectile_speed`. The attack is always ranged. Each Firepower tier of the owner multiplies the damage by `per_tier.damage`. Range is measured from the nearest edge of the footprint. |
| `attack.cannot_hit` | Optional. Lines the attack cannot hit. |
| `attack.can_hit_all_from_protection` | Optional, needs `cannot_hit`. The owner's Protection tier from which `cannot_hit` no longer applies. |
| `wall.gate` | Makes the structure the wall, which is laid tile by tile and needs a `footprint` of 1. The gate is a wall tile with a `cost` and `build_seconds` of its own; it has the wall's hit points and sight, and it is unlocked when the wall is. It needs no file and no line in the roster: the game makes it from this block, with the ID `gate`, which no structure in the roster may have. |

A line's file names the structure that trains it in `built_at`, and that structure's file lists the line in `trains`. Where both files exist the two must say the same. A structure may list a line that has no file yet, as the Works lists `support`.

`worker` is a reserved word in `trains`, because the worker is not a line; its price and training time are in `resources.jsonc`.

The files are read and checked, and each player has a blueprint for every structure. Nothing in a match places a structure yet.

## maps/&lt;id&gt;.jsonc

Game design 5.3. Tiles are `(x, y)` with `(0, 0)` at the first character of the first terrain row; x grows to the right and y downward. A position names the lowest tile of a footprint, its top-left corner.

| Key | Meaning |
| --- | --- |
| `id` | The map's ID. |
| `width`, `height` | Size in tiles, 16 to 256. |
| `symmetry` | `mirror_x`: the right half reflects the left half. `mirror_y`: the bottom half reflects the top half. |
| `starts` | Two entries: where each player's starting Capitol stands. |
| `deposits` | Each has a `kind` (`main`, `natural` or `contested`) and a position. |
| `markers` | Authored chokepoints and open fields. Each has a `kind` (`chokepoint` or `open_field`), a position, `size_x` and `size_y`, and `across`. |
| `terrain` | One text row per y and one character per x: `.` open ground, `F` forest, `#` cliff. |

A deposit covers `map.deposit_size` tiles a side (`rules.jsonc`). No unit can stand on those tiles, so a deposit is an obstacle like a cliff, and the workers at it stand on the tiles that share a side with it. Leave those tiles open: a place on forest, on a cliff or off the map cannot be used.

A marker is a rectangle covering the passage. `across` is the axis the passage's width runs along: with `"across": "y"` the width is `size_y`, and units pass through along x. Nothing in the game finds chokepoints by itself; the AI and the balance battles use these markers.

## What datacheck checks

Besides the shape of each file:

| Check | Rule |
| --- | --- |
| References | A line's `id`, `primary`, `secondary`, `built_at`, `counters` and `cannot_hit` must name a track in `tracks.jsonc` or an ID in the roster. So must a structure's `id`, `track`, `trains` and `attack.cannot_hit`. |
| Structures | `trains` and the lines' `built_at` agree. `structures/capitol.jsonc` exists and has a `territory_radius`: it is what the map checks and the start of a match use. A wall has a footprint of 1. An unlock tier, or the tier from which an attack hits every line, is a tier a track can reach above the first. |
| Tiers | Counts of tracks and tiers in `rules.jsonc` must fit `tracks.jsonc`; `epoch_cap` is not below `start_epoch`; every track names one tier per epoch. |
| Tracks | The six tracks the rules name are all defined. |
| Research | `research.jsonc` has exactly one entry for each tier from 2 to the last, in order. |
| Accents | Every accent belongs to a track in `tracks.jsonc`. No kind of perk appears twice. |
| Chassis | An epoch outside the tiers could never unlock and is an error. Epochs rise and do not repeat. IDs are unique. |
| Times | Attack intervals, build times of units, structures and gates, research times and perk delays are whole numbers of ticks. |
| Income | A worker's payment for one second can be computed exactly at every Extraction tier and never rounds to nothing, at the whole rate and at the depleted one. |
| Formulas | Every stat and price of every chassis can be computed exactly at every tier mix. The game multiplies exact fractions, and a multiplier with many decimal places raised to a high power no longer fits. `1.15` is fine at any tier; `1.123456789` is not. The error names the chassis. The same holds for a structure's hit points at every Protection tier and its damage at every Firepower tier. |
| Stat bounds | Hit points, damage, speed, sight, supply, spacing, prices, ranges and multipliers must be positive where zero makes no sense and inside wide sanity limits, listed in `src/Sim/Data/DataRules.cs`. The limits catch slips such as a missing decimal point; they are not balance rules. |
| Map symmetry | Terrain, starts, deposits and markers must all mirror under the map's `symmetry`. |
| Map footprints | Starting Capitols, deposits and markers stand on open ground inside the map. Deposits overlap neither each other nor a Capitol. |
| Start distance | The Capitols' centers are at least two territory radii apart, so the two starting territories do not overlap. |
| Deposits | Each start has `main_per_player` main deposits wholly inside its territory. Natural and contested deposits have no tile inside a starting territory. Each start has `natural_per_player` natural deposits nearest to it, and the map has `contested` contested ones. A tile is inside a territory when its center is within the radius of the Capitol's center. |
| Markers | At least one chokepoint and one open field. A chokepoint is `chokepoint_width.min` to `max` tiles wide and spans the whole passage: the tiles just past both ends of its width are impassable or off the map. An open field is at least `open_field_min_width` wide. |

Three things it does not check yet. A marker's passage must run along x or y, so a diagonal chokepoint cannot be marked. It does not test that a chokepoint or open field lies between the bases; that stays with the map author. And it does not test the ground around a deposit: that workers can reach its places, and that a deposit in a narrow passage does not close it.

## Content hash

The loader computes one hash over all the data. It goes into the lobby handshake and the replay header, so peers whose data differs cannot start a match. `datacheck` prints it.

Two data folders have the same hash exactly when they mean the same. Comments, spacing, line endings, the order of keys in an object and the `$schema` key do not count. Every value, the order of every list and every file path do.

The hash is the SHA-256, written as 64 lowercase hex digits, of a canonical text encoded as UTF-8:

1. The line `content-hash-v1`, ended by a line feed.
2. For each file, in ordinal order of its path: the path with `/` separators, a line feed, the file's canonical JSON and a line feed.

Canonical JSON has no spaces or line breaks. Object keys are in ordinal order (by UTF-16 code unit) and the top-level `$schema` key is left out. Strings escape only `"` and `\` with a backslash and characters below U+0020 as `\u00xx` in lowercase hex; everything else is written as it is. Whole numbers are written in decimal without a sign unless negative, and `true` and `false` as they are.

A change to this form changes every hash, so it needs a new version line.

## For programmers

- The loader is `Sim.Data.DataLoader.Load(IDataFiles)`. It returns `GameData`, or the list of errors. `IDataFiles` is the only way it reaches files: the tools read from disk (`Tools.DiskDataFiles`), tests read from memory (`Sim.Data.InMemoryDataFiles`) and the game will read from its packed export.
- Numbers arrive as `DataNumber`, an integer and a count of decimal places. Converting them to the simulation's fixed-point and fraction types belongs to the code that uses them.
- The rules that use the data are in `Sim.Tech`. `TechResolver.Resolve(data, tiers)` is a pure function from a player's track tiers to a `PlayerTech`: their epoch, supply cap, income multiplier, the price of each track's next tier and a `ChassisBlueprint` for every chassis. `TechRules` has the rules that need no chassis, each by itself.
- `PlayerTech.Structures` has a `StructureBlueprint` for every structure with a file, in the order of `GameData.Structures`: its hit points in thousandths at the player's Protection tier and, for one that attacks, its damage at the player's Firepower tier and the lines it cannot hit. A structure has no epoch, so the player's actual tiers count, with no window. `GameData.StructureOf` and `PlayerTech.StructureOf` find a structure by its roster index, and return nothing for one that has no file.
- A `ChassisBlueprint` has the chassis's stats, price, accents and perks, each value rounded once. It is computed for locked chassis too; `Unlocked` says whether the player may build it. `Perks` holds only the perks that apply to the chassis, as typed records with the numbers from `accents.jsonc`.
- `TechResolver.Preview(data, tiers, track)` resolves the tiers with one track raised and lists the chassis that would unlock and every accent that would appear, disappear or change level.
- The economy is in `Sim.Economy`. `GameData.Resources` holds `resources.jsonc`, and `IncomeRules.Payment` is the one place a payment is worked out.
- `DataNumber.ToFraction()` gives a data number as an exact `Fraction`. Multiply everything exactly and round once, at the end.
- `GameData.TrackRoles` holds the index of each track the rules name, so code never searches for a track by ID.
- IDs become dense integers at load. A position in `GameData.Tracks` is a track's index, in file order. A position in `RuleSet.Lines` or `RuleSet.Structures` is a line's or structure's index; those lists are sorted by ID. Lines refer to tracks, structures and other lines by these indexes. A position in `GameData.Chassis` is a chassis's index; that list is sorted by ID over all lines, and `PlayerTech.Blueprints` uses the same positions.
- The loader follows the determinism rules: no floating-point types and no iteration over hash-based collections.
- `schemas/` is for editors. The loader does not read it, and is stricter than a schema can be. Tests in `tests/Tools.Tests/SchemaTests.cs` hold the two together: every data file must satisfy its schema, and each schema must reject a list of typical mistakes.
