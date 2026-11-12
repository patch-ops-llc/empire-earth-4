using Sim.Core;
using Sim.Data;
using Sim.Outcome;
using Sim.Setup;
using Sim.Tests.Construction;
using Sim.Tests.Map;
using Sim.Tests.Structures;

namespace Sim.Tests.Outcome;

/// <summary>Matches for the tests of how a match ends: open ground, three starts far apart, the data with an Outpost.</summary>
internal static class OutcomeMatches
{
    public static MapDef Map => TestMaps.Open(60, 30) with { Starts = [new(6, 14), new(52, 14), new(29, 4)] };

    /// <summary>A match in which every player starts as design 5.2 says, player n at start n.</summary>
    public static Match WithStarts(int players = 2) =>
        MatchSetup.Create(ConstructionMatches.Data, Map, 1, [.. Enumerable.Range(0, players).Select(player => new PlayerSetup(player))]);

    /// <summary>The Capitol a player started with.</summary>
    public static EntityId CapitolOf(this Match match, int player) => match.StructuresOf(player)[0];

    /// <summary>Queues an order to concede for the next tick to run.</summary>
    public static void OrderConcede(this Match match, int player) =>
        match.Simulation.Commands.Enqueue(match.World.Tick, new Command(CommandType.Concede, player));

    /// <summary>Where every player stands, in the order of the players.</summary>
    public static PlayerStanding[] All(this Standings standings) =>
        [.. Enumerable.Range(0, standings.PlayerCount).Select(standings.Of)];
}
