namespace Sim.Data;

internal static class RulesReader
{
    /// <summary>
    /// Reads <c>rules.jsonc</c>. <paramref name="trackCount"/> and <paramref name="tierCount"/> come from
    /// the tracks file and bound the values that count tracks or tiers.
    /// </summary>
    public static RuleSet Read(JsonValue root, int trackCount, int tierCount)
    {
        var file = root.Object();
        file.Optional(DataRules.SchemaKey)?.String();

        var roster = file.Required("roster").Object();
        var lines = ReadRoster(roster.Required("lines"), "line");
        var structures = ReadRoster(roster.Required("structures"), "structure");
        roster.End();

        var epoch = file.Required("epoch").Object();
        var epochRules = new EpochRules(
            epoch.Required("tracks_needed").Integer(1, trackCount),
            epoch.Required("reach").Integer(0, tierCount));
        epoch.End();

        var price = file.Required("research_price").Object();
        var researchPrice = new ResearchPriceRules(
            ReadPriceMultiplier(price.Required("catch_up")),
            ReadPriceMultiplier(price.Required("frontier")),
            ReadPriceMultiplier(price.Required("ahead")));
        price.End();

        var effectiveTierWindow = file.Required("effective_tier_window").Integer(0, tierCount);

        var perTier = file.Required("per_tier").Object();
        var perTierRules = new PerTierRules(
            perTier.Required("damage").Multiplier(),
            perTier.Required("hp").Multiplier(),
            perTier.Required("speed").Multiplier(),
            perTier.Required("income").Multiplier());
        perTier.End();

        var perStep = file.Required("per_step").Object();
        var perStepRules = new PerStepRules(
            perStep.Required("quality_cost").Multiplier(),
            perStep.Required("manufacturing_cost").Multiplier(),
            perStep.Required("manufacturing_time").Multiplier());
        perStep.End();

        var accentSlots = file.Required("accent_slots").Integer(0, trackCount);

        var supply = file.Required("supply_cap").Object();
        var supplyCap = new SupplyCapRules(
            supply.Required("base").Integer(0, 10_000),
            supply.Required("per_logistics_tier").Integer(0, 1_000));
        supply.End();

        var wallCrossingCost = file.Required("wall_crossing_cost").Integer(1, 1_000);

        var match = file.Required("match").Object();
        var errorsBefore = root.ErrorCount;
        var startEpoch = match.Required("start_epoch").Integer(1, tierCount);
        var epochCapValue = match.Required("epoch_cap");
        var epochCap = epochCapValue.Integer(1, tierCount);
        if (root.ErrorCount == errorsBefore && epochCap < startEpoch)
        {
            epochCapValue.Error($"must not be below start_epoch ({startEpoch}), found {epochCap}");
        }

        match.End();

        var start = file.Required("start").Object();
        var startRules = new StartRules(
            start.Required("workers").Integer(0, 1_000),
            start.Required("materials").Integer(0, DataRules.MaxPrice),
            start.Required("energy").Integer(0, DataRules.MaxPrice));
        start.End();

        var mapRules = ReadMapRules(file.Required("map"));

        file.End();
        return new RuleSet(
            lines,
            structures,
            epochRules,
            researchPrice,
            effectiveTierWindow,
            perTierRules,
            perStepRules,
            accentSlots,
            supplyCap,
            wallCrossingCost,
            new MatchRules(startEpoch, epochCap),
            startRules,
            mapRules);
    }

    private static List<string> ReadRoster(JsonValue value, string what)
    {
        var ids = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var item in value.Array(1))
        {
            var id = item.Id();
            if (id.Length == 0)
            {
                continue;
            }

            if (id == DataRules.StructuresTarget)
            {
                item.Error($"'{id}' is reserved: in a line's counters it means every structure");
            }
            else if (!ids.Add(id))
            {
                item.Error($"{what} '{id}' is listed twice");
            }
        }

        return [.. ids];
    }

    private static PriceMultiplier ReadPriceMultiplier(JsonValue value)
    {
        var multiplier = value.Object();
        var result = new PriceMultiplier(multiplier.Required("cost").Multiplier(), multiplier.Required("time").Multiplier());
        multiplier.End();
        return result;
    }

    private static MapRules ReadMapRules(JsonValue value)
    {
        var map = value.Object();

        var territory = map.Required("start_territory").Object();
        var footprint = territory.Required("footprint").Integer(1, 16);
        var radius = territory.Required("radius").Integer(1, DataRules.MaxMapSize);
        territory.End();

        var depositSize = map.Required("deposit_size").Integer(1, 8);

        var deposits = map.Required("deposits").Object();
        var mainPerPlayer = deposits.Required("main_per_player").Integer(0, 16);
        var naturalPerPlayer = deposits.Required("natural_per_player").Integer(0, 16);
        var contested = deposits.Required("contested").Integer(0, 16);
        deposits.End();

        var errorsBefore = value.ErrorCount;
        var chokepoint = map.Required("chokepoint_width").Object();
        var chokepointMin = chokepoint.Required("min").Integer(1, DataRules.MaxMapSize);
        var chokepointMaxValue = chokepoint.Required("max");
        var chokepointMax = chokepointMaxValue.Integer(1, DataRules.MaxMapSize);
        if (value.ErrorCount == errorsBefore && chokepointMax < chokepointMin)
        {
            chokepointMaxValue.Error($"must not be below min ({chokepointMin}), found {chokepointMax}");
        }

        chokepoint.End();

        var openFieldValue = map.Required("open_field_min_width");
        var openFieldMin = openFieldValue.Integer(1, DataRules.MaxMapSize);
        if (value.ErrorCount == errorsBefore && openFieldMin <= chokepointMax)
        {
            openFieldValue.Error($"must be above the widest chokepoint ({chokepointMax}), found {openFieldMin}");
        }

        map.End();
        return new MapRules(
            footprint,
            radius,
            depositSize,
            mainPerPlayer,
            naturalPerPlayer,
            contested,
            chokepointMin,
            chokepointMax,
            openFieldMin);
    }
}
