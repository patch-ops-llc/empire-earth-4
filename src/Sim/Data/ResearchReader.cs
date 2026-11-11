namespace Sim.Data;

internal static class ResearchReader
{
    /// <summary>
    /// Reads <c>research.jsonc</c>: one entry for each tier from 2 to <paramref name="tierCount"/>, in
    /// that order. Entry i of the result is the research that reaches tier i + 2.
    /// </summary>
    public static List<ResearchTier> Read(JsonValue root, int tierCount)
    {
        var file = root.Object();
        file.Optional(DataRules.SchemaKey)?.String();

        var research = new List<ResearchTier>();
        var tiersValue = file.Required("tiers");
        var errorsBefore = root.ErrorCount;
        foreach (var entryValue in tiersValue.Array())
        {
            var entry = entryValue.Object();

            var tierValue = entry.Required("tier");
            var errorsBeforeTier = root.ErrorCount;
            var tier = tierValue.Integer(int.MinValue, int.MaxValue);
            var expected = research.Count + 2;
            if (root.ErrorCount == errorsBeforeTier)
            {
                if (expected > tierCount)
                {
                    tierValue.Error($"tracks have tiers 1 to {tierCount}, so the last entry is tier {tierCount}; found tier {tier}");
                }
                else if (tier != expected)
                {
                    tierValue.Error($"list every tier from 2 to {tierCount} in order; expected tier {expected} here, found {tier}");
                }
            }

            var cost = entry.Required("cost").Object();
            var materials = cost.Required("materials").Integer(0, DataRules.MaxPrice);
            var energy = cost.Required("energy").Integer(0, DataRules.MaxPrice);
            cost.End();

            var ticks = entry.Required("seconds").SecondsAsTicks();
            entry.End();
            research.Add(new ResearchTier(expected, materials, energy, ticks));
        }

        if (root.ErrorCount == errorsBefore && research.Count < tierCount - 1)
        {
            tiersValue.Error($"needs an entry for every tier from 2 to {tierCount}; tier {research.Count + 2} is missing");
        }

        file.End();
        return research;
    }
}
