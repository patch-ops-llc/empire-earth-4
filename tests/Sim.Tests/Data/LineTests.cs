namespace Sim.Tests.Data;

/// <summary>What the loader rejects in a line file.</summary>
public class LineTests
{
    private const string Path = SampleData.GuardPath;

    [Theory]
    [InlineData("\"primary\": \"protection\"", "\"primary\": \"armor\"", "$.primary", "unknown track 'armor'; tracks.jsonc has: firepower, protection, logistics")]
    [InlineData("\"secondary\": \"firepower\"", "\"secondary\": \"fire_power\"", "$.secondary", "unknown track 'fire_power'")]
    [InlineData("\"built_at\": \"barracks\"", "\"built_at\": \"baracks\"", "$.built_at", "unknown structure 'baracks'; the roster in rules.jsonc has: barracks, capitol")]
    [InlineData("\"cavalry_armor\": \"1.5\"", "\"cavalry\": \"1.5\"", "$.counters.cavalry", "unknown line 'cavalry'; the roster in rules.jsonc has: air, cavalry_armor, guard")]
    [InlineData("\"cannot_hit\": [\"air\"]", "\"cannot_hit\": [\"aircraft\"]", "$.cannot_hit[0]", "unknown line 'aircraft'")]
    public void AReferenceMustResolve(string oldText, string newText, string location, string message)
    {
        DataAssert.RejectedOnce(SampleData.With(Path, oldText, newText), Path, location, message);
    }

    [Fact]
    public void TheIdMustMatchTheFileName()
    {
        var files = SampleData.With(Path, "\"id\": \"guard\"", "\"id\": \"air\"");

        DataAssert.RejectedOnce(files, Path, "$.id", "must be 'guard' to match the file name, found 'air'");
    }

    [Fact]
    public void TheLineMustBeInTheRoster()
    {
        var files = SampleData.With(Path, "\"id\": \"guard\"", "\"id\": \"militia\"");
        files.Set("lines/militia.jsonc", files.ReadFile(Path));
        files.Remove(Path);

        DataAssert.RejectedOnce(files, "lines/militia.jsonc", "$.id", "unknown line 'militia'; the roster in rules.jsonc has: air, cavalry_armor, guard");
    }

    [Theory]
    [InlineData("\"epoch\": 4", "\"epoch\": 5", "$.chassis[2].epoch", "epoch 5 can never unlock: tracks have tiers 1 to 4")]
    [InlineData("\"epoch\": 1", "\"epoch\": 0", "$.chassis[0].epoch", "epoch 0 can never unlock: tracks have tiers 1 to 4")]
    [InlineData("\"epoch\": 2", "\"epoch\": 1", "$.chassis[1].epoch", "list chassis by rising epoch, one per epoch; epoch 1 follows epoch 1")]
    [InlineData("\"epoch\": 4", "\"epoch\": 1", "$.chassis[2].epoch", "list chassis by rising epoch, one per epoch; epoch 1 follows epoch 2")]
    [InlineData("\"epoch\": 2", "\"epoch\": \"2\"", "$.chassis[1].epoch", "expected a whole number, found text")]
    public void AChassisEpochMustBeATierAndUnique(string oldText, string newText, string location, string message)
    {
        DataAssert.RejectedOnce(SampleData.With(Path, oldText, newText), Path, location, message);
    }

    [Fact]
    public void AChassisIdMustBeUniqueInItsLine()
    {
        var files = SampleData.With(Path, "\"id\": \"legionary\"", "\"id\": \"hoplite\"");

        DataAssert.RejectedOnce(files, Path, "$.chassis[1].id", "chassis 'hoplite' is defined twice");
    }

    [Fact]
    public void AChassisIdMustBeUniqueAcrossLines()
    {
        var files = SampleData.Files().Set(
            "lines/cavalry_armor.jsonc",
            SampleData.Guard
                .Replace("\"id\": \"guard\"", "\"id\": \"cavalry_armor\"", StringComparison.Ordinal)
                .Replace("\"cavalry_armor\": \"1.5\", ", "", StringComparison.Ordinal)
                .Replace("\"id\": \"hoplite\"", "\"id\": \"hippeus\"", StringComparison.Ordinal)
                .Replace("\"id\": \"grenadier\"", "\"id\": \"cuirassier\"", StringComparison.Ordinal));

        DataAssert.RejectedOnce(files, Path, "$.chassis[1].id", "chassis 'legionary' is already defined in lines/cavalry_armor.jsonc");
    }

    [Fact]
    public void LinesAreSortedById()
    {
        var files = SampleData.Files().Set(
            "lines/air.jsonc",
            SampleData.Guard
                .Replace("\"id\": \"guard\"", "\"id\": \"air\"", StringComparison.Ordinal)
                .Replace("\"cannot_hit\": [\"air\"],", "", StringComparison.Ordinal)
                .Replace("\"id\": \"hoplite\"", "\"id\": \"biplane\"", StringComparison.Ordinal)
                .Replace("\"id\": \"legionary\"", "\"id\": \"fighter\"", StringComparison.Ordinal)
                .Replace("\"id\": \"grenadier\"", "\"id\": \"jet\"", StringComparison.Ordinal));

        var lines = DataAssert.Valid(files).Lines;

        Assert.Equal([("air", 0), ("guard", 2)], lines.Select(line => (line.Id, line.Index)));
        Assert.Empty(lines[0].CannotHit);
    }

    [Theory]
    [InlineData("\"interval_seconds\": \"1\"", "\"interval_seconds\": \"0.33\"", "$.attack.interval_seconds", "0.33 s is not a whole number of ticks; there are 20 ticks per second")]
    [InlineData("\"interval_seconds\": \"1\"", "\"interval_seconds\": \"0\"", "$.attack.interval_seconds", "must be above 0 and at most 3600, found 0")]
    [InlineData("\"build_seconds\": 18", "\"build_seconds\": \"7.68\"", "$.base.build_seconds", "7.68 s is not a whole number of ticks")]
    [InlineData("\"build_seconds\": 18", "\"build_seconds\": 4000", "$.base.build_seconds", "must be above 0 and at most 3600, found 4000")]
    public void ATimeMustBeAWholeNumberOfTicks(string oldText, string newText, string location, string message)
    {
        DataAssert.RejectedOnce(SampleData.With(Path, oldText, newText), Path, location, message);
    }

    [Fact]
    public void ATimeOfOneTickIsAccepted()
    {
        var guard = DataAssert.Valid(SampleData.With(Path, "\"interval_seconds\": \"1\"", "\"interval_seconds\": \"0.05\"")).Lines[0];

        Assert.Equal(1, guard.Attack.IntervalTicks);
    }

    [Theory]
    [InlineData("\"hp\": 130", "\"hp\": 0", "$.base.hp", "must be above 0 and at most 1000000, found 0")]
    [InlineData("\"hp\": 130", "\"hp\": -130", "$.base.hp", "must be above 0 and at most 1000000, found -130")]
    [InlineData("\"hp\": 130", "\"hp\": 13000000", "$.base.hp", "must be above 0 and at most 1000000, found 13000000")]
    [InlineData("\"dps\": 8", "\"dps\": 0", "$.base.dps", "must be above 0 and at most 100000, found 0")]
    [InlineData("\"speed\": \"2.6\"", "\"speed\": \"260\"", "$.base.speed", "must be above 0 and at most 100, found 260")]
    [InlineData("\"sight\": 8", "\"sight\": 80", "$.base.sight", "must be between 1 and 64, found 80")]
    [InlineData("\"supply\": 2", "\"supply\": 0", "$.base.supply", "must be between 1 and 100, found 0")]
    [InlineData("\"spacing\": \"1\"", "\"spacing\": \"0\"", "$.base.spacing", "must be above 0 and at most 64, found 0")]
    [InlineData("\"materials\": 70", "\"materials\": -70", "$.base.cost.materials", "must be between 0 and 1000000, found -70")]
    [InlineData("\"energy\": 10", "\"energy\": 10000000", "$.base.cost.energy", "must be between 0 and 1000000, found 10000000")]
    [InlineData("\"range\": 3", "\"range\": 300", "$.chassis[2].attack.range", "must be above 0 and at most 64, found 300")]
    [InlineData("\"projectile_speed\": \"20\"", "\"projectile_speed\": \"0\"", "$.chassis[2].attack.projectile_speed", "must be above 0 and at most 1000, found 0")]
    [InlineData("\"cavalry_armor\": \"1.5\"", "\"cavalry_armor\": \"0\"", "$.counters.cavalry_armor", "must be above 0 and at most 100, found 0")]
    public void AStatMustBeInBounds(string oldText, string newText, string location, string message)
    {
        DataAssert.RejectedOnce(SampleData.With(Path, oldText, newText), Path, location, message);
    }

    [Theory]
    [InlineData("{ \"kind\": \"melee\", \"interval_seconds\": \"1\" }", "{ \"kind\": \"ranged\", \"interval_seconds\": \"1\" }", "$.attack", "a ranged attack needs \"range\" and \"projectile_speed\"")]
    [InlineData("{ \"kind\": \"melee\", \"interval_seconds\": \"1\" }", "{ \"kind\": \"melee\", \"interval_seconds\": \"1\", \"range\": 2 }", "$.attack", "a melee attack has no \"range\" and no \"projectile_speed\"")]
    [InlineData("{ \"kind\": \"melee\", \"interval_seconds\": \"1\" }", "{ \"kind\": \"thrown\", \"interval_seconds\": \"1\" }", "$.attack.kind", "unknown attack kind 'thrown'; use melee or ranged")]
    [InlineData("{ \"kind\": \"melee\", \"interval_seconds\": \"1\" }", "{ \"kind\": \"melee\" }", "$.attack", "missing key \"interval_seconds\"")]
    [InlineData("{ \"kind\": \"ranged\", \"range\": 3, \"projectile_speed\": \"20\" }", "{ \"kind\": \"ranged\", \"range\": 3 }", "$.chassis[2].attack", "a ranged attack needs \"range\" and \"projectile_speed\"")]
    [InlineData("{ \"kind\": \"ranged\", \"range\": 3, \"projectile_speed\": \"20\" }", "{ \"range\": 3 }", "$.chassis[2].attack", "a melee attack has no \"range\" and no \"projectile_speed\"")]
    [InlineData("{ \"kind\": \"ranged\", \"range\": 3, \"projectile_speed\": \"20\" }", "{ \"kind\": \"ranged\", \"range\": 3, \"projectile_speed\": \"20\", \"min_range\": 1 }", "$.chassis[2].attack.min_range", "unknown key \"min_range\"")]
    public void AnAttackMustBeComplete(string oldText, string newText, string location, string message)
    {
        DataAssert.RejectedOnce(SampleData.With(Path, oldText, newText), Path, location, message);
    }

    [Fact]
    public void ALineCannotCounterALineItCannotHit()
    {
        var files = SampleData.With(Path, "\"cannot_hit\": [\"air\"]", "\"cannot_hit\": [\"air\", \"cavalry_armor\"]");

        DataAssert.RejectedOnce(files, Path, "$.cannot_hit[1]", "line 'cavalry_armor' is also in counters");
    }

    [Fact]
    public void ALineCannotBeListedTwiceInCannotHit()
    {
        var files = SampleData.With(Path, "\"cannot_hit\": [\"air\"]", "\"cannot_hit\": [\"air\", \"air\"]");

        DataAssert.RejectedOnce(files, Path, "$.cannot_hit[1]", "line 'air' is listed twice");
    }

    [Fact]
    public void ALineNeedsAtLeastOneChassis()
    {
        var start = SampleData.Guard.IndexOf("\"chassis\": [", StringComparison.Ordinal);
        var end = SampleData.Guard.LastIndexOf(']');
        var files = SampleData.With(Path, SampleData.Guard[start..(end + 1)], "\"chassis\": []");

        DataAssert.RejectedOnce(files, Path, "$.chassis", "must have at least 1 entries, found 0");
    }

    [Fact]
    public void CountersAndCannotHitAreOptional()
    {
        var files = SampleData.With(Path, "\"counters\": { \"cavalry_armor\": \"1.5\", \"structures\": \"0.5\" },", "");
        var guard = DataAssert.Valid(files).Lines[0];

        Assert.Empty(guard.Counters);
        Assert.Null(guard.StructureCounter);
        Assert.Equal([0], guard.CannotHit);
    }
}
