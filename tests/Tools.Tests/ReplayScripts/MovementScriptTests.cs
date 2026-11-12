using Sim.Core;
using Sim.Movement;
using Sim.Numerics;
using Sim.Setup;

namespace Tools.Tests.ReplayScripts;

/// <summary>
/// The script <c>movement</c> of <c>replay record</c> and the three golden replays recorded with
/// it alone: what the script must give, and what those replays must reach.
/// </summary>
public class MovementScriptTests
{
    private static readonly string[] Replays = ["ridge-skirmish.replay", "switchback-crossing.replay", "switchback-three-players.replay"];

    private static readonly string[] EveryKindOfOrder =
    [
        "a move",
        "a command of a kind without a handler",
        "a command from nobody in the match",
        "an order for one unit",
        "an order for a group",
        "an order for each unit of every player in one tick",
        "a target off the map",
        "a target as far off the map as a number goes",
        "an order for units that are not the player's",
        "an order for an entity that never existed",
    ];

    /// <summary>
    /// The script gives every kind of order it is meant to, so that a match recorded with it
    /// reaches the rules: here in one long match on each map. The rarest kinds come about once in
    /// 4,000 ticks, so a shorter match can miss one.
    /// </summary>
    [Theory]
    [InlineData("ridge")]
    [InlineData("switchback")]
    public void TheScriptGivesEveryKindOfItsOrders(string map)
    {
        var replay = ReplayRuns.Record("--map", map, "--ticks", "20000", "--orders", "33", "--script", "movement");

        Assert.All(replay.Commands, entry => Assert.True(IsOfThisScript(entry.Command), $"The script gave a command of type {entry.Command.Type}."));
        var seen = KindsOfOrders(replay, replay.Description.CreateMatch(ReplayRuns.GoldenData));
        Assert.Equal(EveryKindOfOrder.Order(StringComparer.Ordinal), seen.Order(StringComparer.Ordinal));
        Assert.True(replay.Run(ReplayRuns.GoldenData).Reproduced);
    }

    /// <summary>
    /// What these recorded matches are for: between them they must reach the rules of setting up
    /// a match and of movement that a change could break.
    /// </summary>
    [Fact]
    public void ItsGoldenReplaysReachTheRulesTheyGuard()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in Replays)
        {
            var replay = ReplayRuns.ReadGolden(name);
            seen.Add($"map {replay.Description.MapId}");
            seen.Add($"{replay.Description.Players.Count} players");
            if (replay.Description.Players.Any(player => !player.HasStart))
            {
                seen.Add("a player without a start");
            }

            if (replay.Description.Players[0].Start > replay.Description.Players[^1].Start)
            {
                seen.Add("starts swapped");
            }

            var match = ReplayRuns.Start(replay);
            seen.UnionWith(KindsOfOrders(replay, match));
            var waited = new int[match.Movers.Count];
            for (var tick = 0; tick < replay.TickCount; tick++)
            {
                match.Simulation.Tick();
                for (var i = 0; i < waited.Length; i++)
                {
                    var state = match.Movers.ValueAt(i).State;
                    waited[i] = state == MoveState.Planning ? waited[i] + 1 : 0;
                    seen.Add(state switch
                    {
                        MoveState.Idle => "a unit standing",
                        MoveState.Moving => "a unit walking straight",
                        MoveState.Following => "a unit walking a route",
                        _ => waited[i] > 1 ? "a unit waiting more than a tick for a route" : "a unit waiting for a route",
                    });
                }
            }
        }

        string[] expected =
        [
            "map ridge",
            "map switchback",
            "2 players",
            "3 players",
            "a player without a start",
            "starts swapped",
            .. EveryKindOfOrder,
            "a unit standing",
            "a unit walking straight",
            "a unit walking a route",
            "a unit waiting for a route",
            "a unit waiting more than a tick for a route",
        ];
        Assert.Equal(expected.Order(StringComparer.Ordinal), seen.Order(StringComparer.Ordinal));
    }

    /// <summary>A move, or a command of a kind that nothing handles, which this script gives as well.</summary>
    private static bool IsOfThisScript(Command command) => command.Type == CommandType.Move || !Enum.IsDefined(command.Type);

    /// <summary>What the commands of this script in a replay are, in the words of <see cref="EveryKindOfOrder"/>.</summary>
    private static HashSet<string> KindsOfOrders(Sim.Serialization.Replay replay, Match match)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var players = match.Players.Count;
        var unitsWithAnOrderOfTheirOwn = new HashSet<EntityId>();
        var tickOfThose = -1;
        foreach (var (tick, command) in replay.Commands.Where(entry => IsOfThisScript(entry.Command)))
        {
            seen.Add(command.Type == CommandType.Move ? "a move" : "a command of a kind without a handler");
            if (command.Player >= players)
            {
                seen.Add("a command from nobody in the match");
            }

            if (command.Type != CommandType.Move || command.Player >= players)
            {
                continue;
            }

            seen.Add(command.Entities.Length == 1 ? "an order for one unit" : "an order for a group");
            if (!match.Grid.Contains(new FixedVector2(command.TargetX, command.TargetY)))
            {
                seen.Add(command.TargetX == Fixed.MaxValue ? "a target as far off the map as a number goes" : "a target off the map");
            }

            foreach (var entity in command.Entities)
            {
                if (!match.Bodies.TryGet(entity, out var body))
                {
                    seen.Add("an order for an entity that never existed");
                }
                else if (body.Owner != command.Player)
                {
                    seen.Add("an order for units that are not the player's");
                }
            }

            if (command.Entities.Length == 1)
            {
                if (tick != tickOfThose)
                {
                    unitsWithAnOrderOfTheirOwn.Clear();
                    tickOfThose = tick;
                }

                unitsWithAnOrderOfTheirOwn.Add(command.Entities[0]);
                if (unitsWithAnOrderOfTheirOwn.Count == match.Movers.Count)
                {
                    seen.Add("an order for each unit of every player in one tick");
                }
            }
        }

        return seen;
    }
}
