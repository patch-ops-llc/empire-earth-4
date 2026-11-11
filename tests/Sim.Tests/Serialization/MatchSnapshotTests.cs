using Sim.Core;
using Sim.Movement;
using Sim.Setup;
using Sim.Tests.Setup;

namespace Sim.Tests.Serialization;

/// <summary>
/// The snapshot round trip for a real match, built by <see cref="MatchSetup.Create"/> from the
/// sample data: twelve workers, six for each of two players, on the sample map.
/// </summary>
public class MatchSnapshotTests
{
    internal const int LastTick = 400;

    private static readonly PlayerSetup[] TwoPlayers = [new(0), new(1)];

    internal static Match Build() => MatchSetup.Create(TestMatches.Data, TestMatches.Data.Maps[0], 7, TwoPlayers);

    /// <summary>
    /// Both players send their workers across the map, and the first player turns three of them
    /// round while they walk. The last workers arrive well before <see cref="LastTick"/>.
    /// </summary>
    internal static (int Tick, Command Command)[] Script()
    {
        var match = Build();
        var units = Enumerable.Range(0, match.World.Entities.Count).Select(index => match.World.Entities[index]).ToArray();
        var far = TestMatches.Point("30.5", "12.25");
        var near = TestMatches.Point("6.75", "3.5");
        return
        [
            (2, new Command(CommandType.Move, 0, units.AsSpan(0, 6), far.X, far.Y)),
            (5, new Command(CommandType.Move, 1, units.AsSpan(6, 6), near.X, near.Y)),
            (60, new Command(CommandType.Move, 0, units.AsSpan(0, 3), near.X, near.Y)),
        ];
    }

    /// <summary>
    /// Before any order, with an order waiting, with every unit walking, just after some were
    /// turned round, with some arrived and others walking, and with all at rest.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(40)]
    [InlineData(61)]
    [InlineData(150)]
    [InlineData(LastTick)]
    public void AMatchRestoresAndContinues(int snapshotTick) =>
        SnapshotAssert.RestoresAndContinues(() => Build().Simulation, Script(), snapshotTick, LastTick);

    /// <summary>The script does what the test above says it does, so that the test means something.</summary>
    [Fact]
    public void TheScriptHasUnitsWalkingAtTheSnapshotsAndAtRestAtTheEnd()
    {
        var match = Build();
        foreach (var (tick, command) in Script())
        {
            match.Simulation.Commands.Enqueue(tick, command);
        }

        match.Simulation.Run(40);
        Assert.Equal(12, Walking(match));
        match.Simulation.Run(110);
        Assert.InRange(Walking(match), 1, 11);
        match.Simulation.Run(LastTick - 150);
        Assert.Equal(0, Walking(match));
        Assert.Equal(TestMatches.Point("6.75", "3.5"), match.PositionOf(match.World.Entities[0]));
        Assert.Equal(TestMatches.Point("30.5", "12.25"), match.PositionOf(match.World.Entities[5]));
    }

    private static int Walking(Match match) =>
        Enumerable.Range(0, match.Movers.Count).Count(index => match.Movers.ValueAt(index).State == MoveState.Moving);
}
