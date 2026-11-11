using Sim.Data;
using Sim.Tech;

namespace Tools.Tests;

/// <summary>The real <c>game/data</c>, read from disk through the loader.</summary>
public class GameDataTests
{
    [Fact]
    public void TheGameDataLoadsWithoutErrors()
    {
        var result = DataLoader.Load(new DiskDataFiles(Repository.DataFolder));

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
        Assert.NotEmpty(result.Data.Tracks);
        Assert.NotEmpty(result.Data.Lines);
        Assert.NotEmpty(result.Data.Maps);
    }

    [Fact]
    public void EveryTierAboveTheFirstCanBeResearchedAndCostsSomething()
    {
        var data = DataLoader.Load(new DiskDataFiles(Repository.DataFolder)).Data!;

        Assert.Equal(Enumerable.Range(2, data.TierCount - 1), data.Research.Select(research => research.Tier));
        Assert.All(data.Research, research => Assert.True(research.Materials + research.Energy > 0, $"tier {research.Tier} is free"));
    }

    [Fact]
    public void EveryAccentGivesSomethingAtBothLevels()
    {
        var data = DataLoader.Load(new DiskDataFiles(Repository.DataFolder)).Data!;

        Assert.NotEmpty(data.Accents);
        Assert.All(data.Accents, accent =>
        {
            Assert.NotEmpty(accent.LevelOne.Kinds);
            Assert.NotEmpty(accent.LevelTwo.Kinds);
        });
    }

    /// <summary>
    /// Every tier mix a match can reach resolves, and gives sane values for every chassis. This is
    /// the whole space: each track from the start tier to the epoch cap.
    /// </summary>
    [Fact]
    public void EveryTierMixWithinTheEpochCapResolves()
    {
        var data = DataLoader.Load(new DiskDataFiles(Repository.DataFolder)).Data!;
        var (lowest, highest) = (data.Rules.Match.StartEpoch, data.Rules.Match.EpochCap);
        var tiers = Enumerable.Repeat(lowest, data.Tracks.Count).ToArray();
        var mixes = 0;
        while (true)
        {
            var tech = TechResolver.Resolve(data, tiers);
            mixes++;
            Assert.InRange(tech.Epoch, lowest, highest);
            Assert.All(tech.Research, offer => Assert.True(offer.Tier <= highest || !offer.IsAvailable));
            foreach (var blueprint in tech.Blueprints)
            {
                Assert.True(blueprint.HitPoints > 0 && blueprint.DamagePerSecond > 0 && blueprint.SpeedPerTick.Raw > 0, blueprint.Chassis.Id);
                Assert.True(blueprint.Materials + blueprint.Energy > 0 && blueprint.BuildTicks > 0, blueprint.Chassis.Id);
                Assert.True(blueprint.Supply >= 1 && blueprint.BatchBuildTicks >= blueprint.BuildTicks, blueprint.Chassis.Id);
                Assert.True(blueprint.Accents.Count <= data.Rules.AccentSlots, blueprint.Chassis.Id);
            }

            var track = 0;
            while (track < tiers.Length && tiers[track] == highest)
            {
                tiers[track++] = lowest;
            }

            if (track == tiers.Length)
            {
                break;
            }

            tiers[track]++;
        }

        var perTrack = highest - lowest + 1;
        Assert.Equal((int)Math.Pow(perTrack, data.Tracks.Count), mixes);
    }

    /// <summary>The first playable has four lines with 16 chassis (design 6); Air and Support come later.</summary>
    [Fact]
    public void EveryLineOfTheFirstPlayableHasAChassisForEachEpochUpToTheCap()
    {
        var data = DataLoader.Load(new DiskDataFiles(Repository.DataFolder)).Data!;

        Assert.Equal(["artillery", "cavalry_armor", "guard", "line_infantry"], data.Lines.Select(line => line.Id));
        Assert.All(data.Lines, line => Assert.Equal(Enumerable.Range(1, data.Rules.Match.EpochCap), line.Chassis.Select(chassis => chassis.Epoch)));
        Assert.Equal(data.Chassis.Select(chassis => chassis.Id).Order(StringComparer.Ordinal), data.Chassis.Select(chassis => chassis.Id));
    }

    [Fact]
    public void TheHashDoesNotDependOnLineEndingsOnDisk()
    {
        using var copy = Repository.CopyOfData();
        foreach (var path in Directory.EnumerateFiles(copy.Path, "*", SearchOption.AllDirectories))
        {
            File.WriteAllText(path, File.ReadAllText(path).Replace("\n", "\r\n", StringComparison.Ordinal));
        }

        var original = DataLoader.Load(new DiskDataFiles(Repository.DataFolder));
        var converted = DataLoader.Load(new DiskDataFiles(copy.Path));

        Assert.True(original.IsValid);
        Assert.True(converted.IsValid, string.Join(Environment.NewLine, converted.Errors));
        Assert.Equal(original.Data.ContentHash, converted.Data.ContentHash);
    }

    [Fact]
    public void DiskFilesAreListedWithForwardSlashesAndDotFilesAreSkipped()
    {
        using var folder = new TemporaryFolder();
        folder.Write("tracks.jsonc", "{}");
        folder.Write("lines/guard.jsonc", "{}");
        folder.Write("maps/ridge.jsonc", "{}");
        folder.Write(".DS_Store", "x");
        folder.Write("lines/.hidden.jsonc", "{}");
        folder.Write(".cache/rules.jsonc", "{}");

        var files = new DiskDataFiles(folder.Path);

        Assert.Equal(["lines/guard.jsonc", "maps/ridge.jsonc", "tracks.jsonc"], files.ListFiles());
        Assert.Equal("{}"u8.ToArray(), files.ReadFile("lines/guard.jsonc").ToArray());
    }
}
