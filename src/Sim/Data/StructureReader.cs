namespace Sim.Data;

internal static class StructureReader
{
    private const string Roster = "the roster in rules.jsonc";

    /// <summary>Reads one structure file.</summary>
    public static StructureDef Read(JsonValue root, string expectedId, IReadOnlyList<TrackDef> tracks, int tierCount, RuleSet rules)
    {
        var file = root.Object();
        file.Optional(DataRules.SchemaKey)?.String();

        var idValue = file.Required("id");
        var id = idValue.Id();
        var index = 0;
        if (id.Length > 0)
        {
            if (id != expectedId)
            {
                idValue.Error($"must be '{expectedId}' to match the file name, found '{id}'");
            }
            else
            {
                index = idValue.IndexIn(id, rules.Structures, "structure", Roster);
            }
        }

        int? track = null;
        if (file.Optional("track") is { } trackValue)
        {
            var trackIds = new List<string>(tracks.Count);
            foreach (var entry in tracks)
            {
                trackIds.Add(entry.Id);
            }

            track = trackValue.IndexIn(trackValue.Id(), trackIds, "track", DataRules.TracksFile);
        }

        var unlockTier = 1;
        if (file.Optional("unlock_tier") is { } unlockValue)
        {
            var errorsBefore = unlockValue.ErrorCount;
            unlockTier = unlockValue.Integer(int.MinValue, int.MaxValue);
            if (unlockValue.ErrorCount == errorsBefore)
            {
                if (unlockTier < 2 || unlockTier > tierCount)
                {
                    unlockValue.Error(
                        $"tier {unlockTier} cannot be an unlock: tracks have tiers 1 to {tierCount}, and a structure that is there from the start has no \"unlock_tier\"");
                }
                else if (track is null)
                {
                    unlockValue.Error("needs \"track\", the track whose tier unlocks the structure");
                }
            }
        }

        var footprint = file.Required("footprint").Integer(1, DataRules.MaxFootprint);
        var hitPoints = file.Required("hp").Number(DataRules.Zero, DataRules.MaxHitPoints, aboveMin: true);
        var (materials, energy) = ReadCost(file.Required("cost"));
        var buildTicks = file.Required("build_seconds").SecondsAsTicks();
        var sight = file.Required("sight").Integer(1, DataRules.MaxSight);

        var territoryRadius = file.Optional("territory_radius")?.Integer(1, DataRules.MaxTerritoryRadius);
        var researchSlots = file.Optional("research_slots")?.Integer(1, DataRules.MaxResearchSlots) ?? 0;
        var maxPerPlayer = file.Optional("max_per_player")?.Integer(1, DataRules.MaxStructuresPerPlayer);

        var trainsWorkers = false;
        var trains = new List<int>();
        if (file.Optional("trains") is { } trainsValue)
        {
            ReadTrains(trainsValue, rules, trains, out trainsWorkers);
        }

        var isEnergyWorkSite = false;
        if (file.Optional("work_site") is { } workSiteValue)
        {
            var resource = workSiteValue.String();
            if (resource == DataRules.EnergyWorkSite)
            {
                isEnergyWorkSite = true;
            }
            else if (resource.Length > 0)
            {
                workSiteValue.Error($"unknown resource '{resource}'; use {DataRules.EnergyWorkSite}: Materials come from deposits, which are part of a map");
            }
        }

        var attack = file.Optional("attack") is { } attackValue ? ReadAttack(attackValue, rules, tierCount) : null;

        WallDef? wall = null;
        if (file.Optional("wall") is { } wallValue)
        {
            wall = ReadWall(wallValue);
            if (footprint != 1)
            {
                wallValue.Error($"a wall is laid tile by tile, so its \"footprint\" must be 1, found {footprint}");
            }
        }

        file.End();
        return new StructureDef(
            id,
            index,
            track,
            unlockTier,
            footprint,
            hitPoints,
            materials,
            energy,
            buildTicks,
            sight,
            territoryRadius,
            researchSlots,
            maxPerPlayer,
            trainsWorkers,
            trains,
            isEnergyWorkSite,
            attack,
            wall);
    }

    private static (int Materials, int Energy) ReadCost(JsonValue value)
    {
        var cost = value.Object();
        var materials = cost.Required("materials").Integer(0, DataRules.MaxPrice);
        var energy = cost.Required("energy").Integer(0, DataRules.MaxPrice);
        cost.End();
        return (materials, energy);
    }

    private static void ReadTrains(JsonValue value, RuleSet rules, List<int> trains, out bool trainsWorkers)
    {
        trainsWorkers = false;
        foreach (var entryValue in value.Array(1))
        {
            var errorsBefore = entryValue.ErrorCount;
            var entry = entryValue.Id();
            if (entry == DataRules.WorkerUnit)
            {
                if (trainsWorkers)
                {
                    entryValue.Error($"'{entry}' is listed twice");
                }

                trainsWorkers = true;
                continue;
            }

            var line = entryValue.IndexIn(entry, rules.Lines, "line", $"use {DataRules.WorkerUnit} for the worker; {Roster}");
            if (entryValue.ErrorCount != errorsBefore || entry.Length == 0)
            {
                continue;
            }

            if (trains.Contains(line))
            {
                entryValue.Error($"line '{entry}' is listed twice");
            }
            else
            {
                trains.Add(line);
            }
        }
    }

    private static StructureAttackDef ReadAttack(JsonValue value, RuleSet rules, int tierCount)
    {
        var attack = value.Object();
        var damagePerSecond = attack.Required("dps").Number(DataRules.Zero, DataRules.MaxDamagePerSecond, aboveMin: true);
        var intervalTicks = attack.Required("interval_seconds").SecondsAsTicks();
        var range = attack.Required("range").Number(DataRules.Zero, DataRules.MaxDistance, aboveMin: true);
        var projectileSpeed = attack.Required("projectile_speed").Number(DataRules.Zero, DataRules.MaxProjectileSpeed, aboveMin: true);

        var cannotHit = new SortedSet<int>();
        if (attack.Optional("cannot_hit") is { } cannotHitValue)
        {
            foreach (var targetValue in cannotHitValue.Array(1))
            {
                var errorsBefore = targetValue.ErrorCount;
                var target = targetValue.Id();
                var targetIndex = targetValue.IndexIn(target, rules.Lines, "line", Roster);
                if (targetValue.ErrorCount == errorsBefore && target.Length > 0 && !cannotHit.Add(targetIndex))
                {
                    targetValue.Error($"line '{target}' is listed twice");
                }
            }
        }

        int? canHitAllFrom = null;
        if (attack.Optional("can_hit_all_from_protection") is { } fromValue)
        {
            var errorsBefore = fromValue.ErrorCount;
            var tier = fromValue.Integer(int.MinValue, int.MaxValue);
            if (fromValue.ErrorCount == errorsBefore)
            {
                if (tier < 2 || tier > tierCount)
                {
                    fromValue.Error($"tier {tier} changes nothing: tracks have tiers 1 to {tierCount}, and an attack that can hit every line from the start has no \"cannot_hit\"");
                }
                else if (cannotHit.Count == 0)
                {
                    fromValue.Error("needs \"cannot_hit\", the lines the attack cannot hit below this tier");
                }
                else
                {
                    canHitAllFrom = tier;
                }
            }
        }

        attack.End();
        return new StructureAttackDef(
            damagePerSecond,
            new AttackDef(AttackKind.Ranged, intervalTicks, range, projectileSpeed),
            [.. cannotHit],
            canHitAllFrom);
    }

    private static WallDef ReadWall(JsonValue value)
    {
        var wall = value.Object();
        var gate = wall.Required("gate").Object();
        var (materials, energy) = ReadCost(gate.Required("cost"));
        var buildTicks = gate.Required("build_seconds").SecondsAsTicks();
        gate.End();
        wall.End();
        return new WallDef(new GateDef(materials, energy, buildTicks));
    }
}
