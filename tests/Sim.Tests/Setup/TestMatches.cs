using Sim.Core;
using Sim.Data;
using Sim.Numerics;
using Sim.Setup;
using Sim.Tests.Data;

namespace Sim.Tests.Setup;

/// <summary>
/// Matches for scenario tests: built by the real match setup, on a map made in code, with players
/// who start with nothing so that the test places every unit itself.
/// </summary>
internal static class TestMatches
{
    private static readonly Lazy<GameData> SampleGameData = new(() => DataAssert.Valid(SampleData.Files()));

    /// <summary>The small data set of the loader's tests, loaded once.</summary>
    public static GameData Data => SampleGameData.Value;

    public static Match Create(MapDef map, int players = 2, ulong seed = 1) =>
        MatchSetup.Create(Data, map, seed, [.. Enumerable.Repeat(PlayerSetup.WithoutStart, players)]);

    /// <summary>A point from decimal text, such as <c>("2.5", "7.25")</c>.</summary>
    public static FixedVector2 Point(string x, string y) => new(Fraction.Parse(x).ToFixed(), Fraction.Parse(y).ToFixed());

    /// <summary>Queues a move order for the next tick to run.</summary>
    public static void OrderMove(this Match match, int player, FixedVector2 target, params EntityId[] entities) =>
        match.Simulation.Commands.Enqueue(
            match.World.Tick,
            new Command(CommandType.Move, player, entities, target.X, target.Y));

    public static FixedVector2 PositionOf(this Match match, EntityId entity) => match.Bodies.Get(entity).Position;
}
