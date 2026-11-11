namespace Sim.Data;

internal static class AccentsReader
{
    /// <summary>Reads <c>accents.jsonc</c>. The result is in track order.</summary>
    public static List<AccentDef> Read(JsonValue root, IReadOnlyList<TrackDef> tracks)
    {
        var file = root.Object();
        file.Optional(DataRules.SchemaKey)?.String();

        var trackIds = new List<string>(tracks.Count);
        foreach (var track in tracks)
        {
            trackIds.Add(track.Id);
        }

        var accents = new List<AccentDef>();
        var whereKindsAre = new SortedDictionary<PerkKind, string>();
        foreach (var (trackId, accentValue) in file.Required("accents").Object().Members())
        {
            var track = accentValue.IndexIn(trackId, trackIds, "track", DataRules.TracksFile);
            var accent = accentValue.Object();
            var name = accent.Required("name").String();
            var levelOne = ReadLevel(accent.Required("level_1"), whereKindsAre);
            var levelTwo = ReadLevel(accent.Required("level_2"), whereKindsAre);
            accent.End();
            accents.Add(new AccentDef(track, name, levelOne, levelTwo));
        }

        file.End();
        accents.Sort((left, right) => left.Track.CompareTo(right.Track));
        return accents;
    }

    private static PerkSet ReadLevel(JsonValue value, SortedDictionary<PerkKind, string> whereKindsAre)
    {
        var level = value.Object();

        // Reports a kind that an earlier level or accent already gave. Two perks of one kind on a
        // unit would need a rule for how they combine, and the design has none.
        JsonObject? Perk(string key, PerkKind kind)
        {
            if (level.Optional(key) is not { } perkValue)
            {
                return null;
            }

            if (!whereKindsAre.TryAdd(kind, perkValue.Path))
            {
                perkValue.Error($"\"{key}\" is already given at {whereKindsAre[kind]}; a kind of perk may appear only once");
            }

            return perkValue.Object();
        }

        var perks = new PerkSet();

        if (Perk("range_bonus", PerkKind.RangeBonus) is { } rangeBonus)
        {
            perks = perks with { RangeBonus = new(rangeBonus.Required("multiplier").RaisingMultiplier()) };
            rangeBonus.End();
        }

        if (Perk("attack_rate_bonus", PerkKind.AttackRateBonus) is { } attackRateBonus)
        {
            perks = perks with { AttackRateBonus = new(attackRateBonus.Required("multiplier").RaisingMultiplier()) };
            attackRateBonus.End();
        }

        if (Perk("splash", PerkKind.Splash) is { } splash)
        {
            perks = perks with
            {
                Splash = new(
                    splash.Required("share").Share(),
                    splash.Required("radius").Number(DataRules.Zero, DataRules.MaxDistance, aboveMin: true)),
            };
            splash.End();
        }

        if (Perk("blast_radius_bonus", PerkKind.BlastRadiusBonus) is { } blastRadiusBonus)
        {
            perks = perks with { BlastRadiusBonus = new(blastRadiusBonus.Required("multiplier").RaisingMultiplier()) };
            blastRadiusBonus.End();
        }

        if (Perk("area_damage_reduction", PerkKind.AreaDamageReduction) is { } areaDamageReduction)
        {
            perks = perks with { AreaDamageReduction = new(areaDamageReduction.Required("multiplier").Share()) };
            areaDamageReduction.End();
        }

        if (Perk("plating", PerkKind.Plating) is { } plating)
        {
            perks = perks with { Plating = new(plating.Required("multiplier").Share()) };
            plating.End();
        }

        if (Perk("sight_bonus", PerkKind.SightBonus) is { } sightBonus)
        {
            perks = perks with { SightBonus = new(sightBonus.Required("tiles").Integer(1, 64)) };
            sightBonus.End();
        }

        if (Perk("charge", PerkKind.Charge) is { } charge)
        {
            perks = perks with
            {
                Charge = new(
                    charge.Required("distance").Number(DataRules.Zero, DataRules.MaxDistance, aboveMin: true),
                    charge.Required("multiplier").RaisingMultiplier()),
            };
            charge.End();
        }

        if (Perk("batch", PerkKind.Batch) is { } batch)
        {
            perks = perks with { Batch = new(batch.Required("time_multiplier").Multiplier()) };
            batch.End();
        }

        if (Perk("death_refund", PerkKind.DeathRefund) is { } deathRefund)
        {
            perks = perks with { DeathRefund = new(deathRefund.Required("share").Share()) };
            deathRefund.End();
        }

        if (Perk("regeneration", PerkKind.Regeneration) is { } regeneration)
        {
            perks = perks with
            {
                Regeneration = new(
                    regeneration.Required("share_per_second").Share(),
                    regeneration.Required("delay_seconds").SecondsAsTicks()),
            };
            regeneration.End();
        }

        if (Perk("supply_reduction", PerkKind.SupplyReduction) is { } supplyReduction)
        {
            perks = perks with
            {
                SupplyReduction = new(
                    supplyReduction.Required("amount").Integer(1, 100),
                    supplyReduction.Required("minimum").Integer(1, 100)),
            };
            supplyReduction.End();
        }

        level.End();
        return perks;
    }
}
