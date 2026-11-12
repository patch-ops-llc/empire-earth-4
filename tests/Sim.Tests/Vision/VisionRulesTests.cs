using Sim.Data;
using Sim.Economy;
using Sim.Setup;
using Sim.Tests.Data;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using Sim.Tests.Tech;

namespace Sim.Tests.Vision;

/// <summary>
/// The <c>vision</c> block of <c>rules.jsonc</c>: how often sight is worked out is data, and the
/// match follows it.
/// </summary>
public class VisionRulesTests
{
    private const string Block = "\"vision\": { \"refresh_ticks\": 4 }";

    [Fact]
    public void SightIsWorkedOutEveryFourTicksInTheDataOfTheGameAndOfTheTests()
    {
        var onDisk = DataLoader.Load(GameDataOnDisk.Files());
        Assert.True(onDisk.IsValid, string.Join(Environment.NewLine, onDisk.Errors));

        Assert.Equal(4, onDisk.Data.Rules.Vision.RefreshTicks);
        Assert.Equal(4, TestMatches.Data.Rules.Vision.RefreshTicks);
        Assert.Equal(4, DesignData.Data.Rules.Vision.RefreshTicks);
    }

    [Theory]
    [InlineData(Block, "\"vision\": { \"refresh_ticks\": 0 }", "$.vision.refresh_ticks", "must be between 1 and 1000, found 0")]
    [InlineData(Block, "\"vision\": { \"refresh_ticks\": 1001 }", "$.vision.refresh_ticks", "must be between 1 and 1000, found 1001")]
    [InlineData(Block, "\"vision\": { \"refresh_ticks\": \"4\" }", "$.vision.refresh_ticks", "found text")]
    [InlineData(Block, "\"vision\": { }", "$.vision", "missing key \"refresh_ticks\"")]
    [InlineData(Block + ",", "", "$", "missing key \"vision\"")]
    [InlineData(Block, "\"vision\": { \"refresh_ticks\": 4, \"shared\": true }", "$.vision.shared", "unknown key \"shared\"")]
    public void TheVisionBlockMustBeThereWithItsNumberAndNothingElse(string oldText, string newText, string location, string message)
    {
        var files = SampleData.With(SampleData.RulesPath, oldText, newText);

        DataAssert.RejectedOnce(files, SampleData.RulesPath, location, message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(7)]
    public void AMatchWorksSightOutAsOftenAsTheDataSays(int refreshTicks)
    {
        var data = DataAssert.Valid(SampleData.With(SampleData.RulesPath, Block, $"\"vision\": {{ \"refresh_ticks\": {refreshTicks} }}"));
        var match = MatchSetup.Create(data, TestMaps.Open(40, 12), 1, [PlayerSetup.WithoutStart, PlayerSetup.WithoutStart]);
        var worker = match.SpawnWorker(0, TestMatches.Point("5.5", "5.5"));
        match.Vision.Refresh();
        Assert.Equal(refreshTicks, match.Vision.RefreshTicks);
        match.OrderMove(0, TestMatches.Point("35.5", "5.5"), worker);

        var seenFrom = match.PositionOf(worker);
        for (var tick = 0; tick < 60; tick++)
        {
            match.Simulation.Tick();
            if (tick % refreshTicks == 0)
            {
                seenFrom = match.PositionOf(worker);
            }

            VisionAssert.SeesExactly(match.Vision.Grid, 0, (seenFrom, data.Resources.Worker.Sight));
        }

        Assert.NotEqual(TestMatches.Point("5.5", "5.5"), seenFrom);
    }

    /// <summary>The number is part of what two machines must agree on before a match.</summary>
    [Fact]
    public void AnotherNumberIsAnotherContentHash()
    {
        var four = DataAssert.Valid(SampleData.Files());
        var five = DataAssert.Valid(SampleData.With(SampleData.RulesPath, Block, "\"vision\": { \"refresh_ticks\": 5 }"));

        Assert.NotEqual(four.ContentHash, five.ContentHash);
    }
}
