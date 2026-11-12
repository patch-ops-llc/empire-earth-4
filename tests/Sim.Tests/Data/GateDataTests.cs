using Sim.Data;
using Sim.Tech;

namespace Sim.Tests.Data;

/// <summary>
/// The gate in the data (design 2.5; design 8.4, rows 11, 22 and 41): the wall's file describes
/// it, so it is a structure with no file and no roster entry of its own, named by the number
/// that follows the roster's last.
/// </summary>
public class GateDataTests
{
    [Fact]
    public void TheGateIsTheWallWithAPriceAndABuildTimeOfItsOwn()
    {
        var data = DataAssert.Valid(SampleStructures.Files());
        var wall = data.StructureOf("wall");

        var gate = data.Gate;

        Assert.NotNull(gate);
        Assert.Equal(
            wall with { Id = "gate", Index = 4, Materials = 40, Energy = 0, BuildTicks = 200, IsGate = true },
            gate);
        Assert.Equal((1, 2, wall.HitPoints, wall.Track, wall.UnlockTier), (gate.Footprint, gate.Sight, gate.HitPoints, gate.Track, gate.UnlockTier));
        Assert.NotNull(gate.Wall);
        Assert.False(wall.IsGate);
        Assert.Equal((20, 100), (wall.Materials, wall.BuildTicks));
    }

    [Fact]
    public void ItIsFoundByItsNumberAndItsNameAndIsNoEntryOfTheRoster()
    {
        var data = DataAssert.Valid(SampleStructures.Files());

        Assert.Equal(4, data.Rules.Structures.Count);
        Assert.Equal(5, data.StructureKinds);
        Assert.Same(data.Gate, data.StructureOf(4));
        Assert.Same(data.Gate, data.StructureOf("gate"));
        Assert.Null(data.StructureOf(5));
        Assert.DoesNotContain(data.Gate, data.Structures);
        Assert.DoesNotContain("gate", data.Rules.Structures);
        Assert.All(data.Structures, structure => Assert.False(structure.IsGate));
    }

    [Fact]
    public void DataWithoutAWallHasNoGate()
    {
        var files = SampleStructures.Files();
        files.Remove(SampleStructures.WallPath);

        var data = DataAssert.Valid(files);

        Assert.Null(data.Gate);
        Assert.Null(data.StructureOf(data.Rules.Structures.Count));
        Assert.Throws<ArgumentException>(() => data.StructureOf("gate"));
        Assert.Null(TechResolver.Resolve(data, [1, 1, 1, 1, 1, 1]).Gate);
    }

    [Fact]
    public void TheRosterMayNotNameAStructureGate()
    {
        var files = SampleStructures.With(
            SampleData.RulesPath,
            "\"structures\": [\"capitol\", \"barracks\", \"tower\", \"wall\"]",
            "\"structures\": [\"capitol\", \"barracks\", \"tower\", \"wall\", \"gate\"]");

        DataAssert.RejectedOnce(files, SampleData.RulesPath, "$.roster.structures[4]", "'gate' is reserved");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void APlayersGateHasTheHitPointsOfThePlayersWall(int protection)
    {
        var data = DataAssert.Valid(SampleStructures.Files());
        int[] tiers = [1, 1, 1, 1, 1, 1];
        tiers[data.TrackRoles.Protection] = protection;

        var tech = TechResolver.Resolve(data, tiers);

        var wall = tech.StructureOf(data.StructureOf("wall").Index)!;
        var gate = tech.Gate;
        Assert.NotNull(gate);
        Assert.Same(data.Gate, gate.Structure);
        Assert.Same(gate, tech.StructureOf(data.Gate!.Index));
        Assert.Equal((wall.HitPoints, wall.Unlocked, wall.ProtectionTier), (gate.HitPoints, gate.Unlocked, gate.ProtectionTier));
        Assert.Null(gate.Attack);
        Assert.DoesNotContain(gate, tech.Structures);
    }
}
