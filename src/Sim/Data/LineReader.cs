namespace Sim.Data;

internal static class LineReader
{
    private const string Roster = "the roster in rules.jsonc";

    private static readonly AttackDef NoAttack = new(AttackKind.Melee, 1, null, null);

    public static LineDef Read(JsonValue root, string expectedId, IReadOnlyList<TrackDef> tracks, int tierCount, RuleSet rules)
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
                index = idValue.IndexIn(id, rules.Lines, "line", Roster);
            }
        }

        var trackIds = new List<string>(tracks.Count);
        foreach (var track in tracks)
        {
            trackIds.Add(track.Id);
        }

        var primaryValue = file.Required("primary");
        var primary = primaryValue.IndexIn(primaryValue.Id(), trackIds, "track", DataRules.TracksFile);
        var secondaryValue = file.Required("secondary");
        var secondary = secondaryValue.IndexIn(secondaryValue.Id(), trackIds, "track", DataRules.TracksFile);
        var builtAtValue = file.Required("built_at");
        var builtAt = builtAtValue.IndexIn(builtAtValue.Id(), rules.Structures, "structure", Roster);

        var profile = ReadProfile(file.Required("base"));
        var errorsBeforeAttack = root.ErrorCount;
        var attack = ReadAttack(file.Required("attack"), lineAttack: null);
        var attackIsValid = root.ErrorCount == errorsBeforeAttack;

        var counters = new List<LineCounter>();
        DataNumber? structureCounter = null;
        if (file.Optional("counters") is { } countersValue)
        {
            foreach (var (target, multiplierValue) in countersValue.Object().Members())
            {
                var multiplier = multiplierValue.Multiplier();
                if (target == DataRules.StructuresTarget)
                {
                    structureCounter = multiplier;
                    continue;
                }

                var errorsBefore = multiplierValue.ErrorCount;
                var targetIndex = multiplierValue.IndexIn(target, rules.Lines, "line", Roster);
                if (multiplierValue.ErrorCount == errorsBefore)
                {
                    counters.Add(new LineCounter(targetIndex, multiplier));
                }
            }

            counters.Sort((left, right) => left.Line.CompareTo(right.Line));
        }

        var cannotHit = new SortedSet<int>();
        if (file.Optional("cannot_hit") is { } cannotHitValue)
        {
            foreach (var targetValue in cannotHitValue.Array())
            {
                var errorsBefore = targetValue.ErrorCount;
                var target = targetValue.Id();
                var targetIndex = targetValue.IndexIn(target, rules.Lines, "line", Roster);
                if (targetValue.ErrorCount != errorsBefore)
                {
                    continue;
                }

                if (!cannotHit.Add(targetIndex))
                {
                    targetValue.Error($"line '{target}' is listed twice");
                }
                else if (counters.Exists(counter => counter.Line == targetIndex))
                {
                    targetValue.Error($"line '{target}' is also in counters, but a line it cannot hit takes no damage from it");
                }
            }
        }

        var chassis = ReadChassis(file.Required("chassis"), attackIsValid ? attack : null, tierCount);

        file.End();
        return new LineDef(id, index, primary, secondary, builtAt, profile, attack, counters, structureCounter, [.. cannotHit], chassis);
    }

    private static LineProfile ReadProfile(JsonValue value)
    {
        var profile = value.Object();
        var hitPoints = profile.Required("hp").Number(DataRules.Zero, DataRules.MaxHitPoints, aboveMin: true);
        var damagePerSecond = profile.Required("dps").Number(DataRules.Zero, DataRules.MaxDamagePerSecond, aboveMin: true);
        var speed = profile.Required("speed").Number(DataRules.Zero, DataRules.MaxSpeed, aboveMin: true);
        var sight = profile.Required("sight").Integer(1, 64);
        var supply = profile.Required("supply").Integer(1, 100);
        var spacing = profile.Required("spacing").Number(DataRules.Zero, DataRules.MaxDistance, aboveMin: true);

        var cost = profile.Required("cost").Object();
        var materials = cost.Required("materials").Integer(0, DataRules.MaxPrice);
        var energy = cost.Required("energy").Integer(0, DataRules.MaxPrice);
        cost.End();

        var buildTicks = profile.Required("build_seconds").SecondsAsTicks();
        profile.End();
        return new LineProfile(hitPoints, damagePerSecond, speed, sight, supply, spacing, materials, energy, buildTicks);
    }

    /// <summary>
    /// Reads a line's attack, or a chassis's override of it when <paramref name="lineAttack"/> is given.
    /// An override replaces only the keys it names. If it changes the kind, the line's range and
    /// projectile speed are not carried over.
    /// </summary>
    private static AttackDef ReadAttack(JsonValue value, AttackDef? lineAttack)
    {
        var errorsBefore = value.ErrorCount;
        var attack = value.Object();

        AttackKind? kind;
        int? intervalTicks;
        if (lineAttack is null)
        {
            kind = ReadAttackKind(attack.Required("kind"));
            intervalTicks = attack.Required("interval_seconds").SecondsAsTicks();
        }
        else
        {
            kind = attack.Optional("kind") is { } kindValue ? ReadAttackKind(kindValue) : null;
            intervalTicks = attack.Optional("interval_seconds")?.SecondsAsTicks();
        }

        var range = attack.Optional("range")?.Number(DataRules.Zero, DataRules.MaxDistance, aboveMin: true);
        var projectileSpeed = attack.Optional("projectile_speed")?.Number(DataRules.Zero, DataRules.MaxProjectileSpeed, aboveMin: true);
        attack.End();

        if (lineAttack is not null)
        {
            if (kind is null || kind == lineAttack.Kind)
            {
                range ??= lineAttack.Range;
                projectileSpeed ??= lineAttack.ProjectileSpeed;
            }

            kind ??= lineAttack.Kind;
            intervalTicks ??= lineAttack.IntervalTicks;
        }

        var result = new AttackDef(kind ?? AttackKind.Melee, intervalTicks ?? 1, range, projectileSpeed);
        if (value.ErrorCount == errorsBefore)
        {
            if (result.Kind == AttackKind.Ranged && (range is null || projectileSpeed is null))
            {
                value.Error("a ranged attack needs \"range\" and \"projectile_speed\"");
            }
            else if (result.Kind == AttackKind.Melee && (range is not null || projectileSpeed is not null))
            {
                value.Error("a melee attack has no \"range\" and no \"projectile_speed\"");
            }
        }

        return result;
    }

    private static AttackKind ReadAttackKind(JsonValue value)
    {
        var text = value.String();
        switch (text)
        {
            case "":
            case "melee":
                return AttackKind.Melee;
            case "ranged":
                return AttackKind.Ranged;
            default:
                value.Error($"unknown attack kind '{text}'; use melee or ranged");
                return AttackKind.Melee;
        }
    }

    /// <summary>Reads the chassis list. <paramref name="lineAttack"/> is null when the line's attack was rejected.</summary>
    private static List<ChassisDef> ReadChassis(JsonValue value, AttackDef? lineAttack, int tierCount)
    {
        var chassis = new List<ChassisDef>();
        var previousEpoch = 0;
        foreach (var entryValue in value.Array(1))
        {
            var entry = entryValue.Object();

            var epochValue = entry.Required("epoch");
            var errorsBefore = epochValue.ErrorCount;
            var epoch = epochValue.Integer(int.MinValue, int.MaxValue);
            if (epochValue.ErrorCount == errorsBefore)
            {
                if (epoch < 1 || epoch > tierCount)
                {
                    epochValue.Error($"epoch {epoch} can never unlock: tracks have tiers 1 to {tierCount}");
                }
                else if (epoch <= previousEpoch)
                {
                    epochValue.Error($"list chassis by rising epoch, one per epoch; epoch {epoch} follows epoch {previousEpoch}");
                }
                else
                {
                    previousEpoch = epoch;
                }
            }

            var idValue = entry.Required("id");
            var id = idValue.Id();
            if (id.Length > 0 && chassis.Exists(other => other.Id == id))
            {
                idValue.Error($"chassis '{id}' is defined twice");
            }

            var attackValue = entry.Optional("attack");
            var attack = lineAttack is not null && attackValue is { } overrideValue ? ReadAttack(overrideValue, lineAttack) : lineAttack;
            entry.End();
            chassis.Add(new ChassisDef(id, epoch, attack ?? NoAttack));
        }

        return chassis;
    }
}
