using Sim.Core;
using Sim.Map;
using Sim.Movement;
using Sim.Setup;

namespace Tools.Tests.ReplayScripts;

/// <summary>
/// The script <c>capitol</c> of <c>replay record</c> and the golden replay recorded for it: what
/// the script must give, and what that replay must reach.
/// </summary>
public class CapitolScriptTests
{
    private const string GoldenReplay = "ridge-capitols.replay";

    private static readonly string[] EveryKindOfOrder =
    [
        "a move onto a Capitol",
        "a move to a tile beside a Capitol",
        "a move past a Capitol",
        "an attack-move onto a Capitol",
        "an order to attack another player's Capitol",
        "an order to attack the player's own Capitol",
        "an order for one unit",
        "an order for a group",
    ];

    /// <summary>
    /// The script gives every kind of order it is meant to: here in one long match on each map.
    /// The rarest kind comes about once in 600 ticks.
    /// </summary>
    [Theory]
    [InlineData("ridge")]
    [InlineData("switchback")]
    public void TheScriptGivesEveryKindOfItsOrders(string map)
    {
        var replay = ReplayRuns.Record("--map", map, "--ticks", "6000", "--orders", "33", "--script", "capitol");

        var seen = KindsOfOrders(replay, replay.Description.CreateMatch(ReplayRuns.GoldenData));
        Assert.Equal(EveryKindOfOrder.Order(StringComparer.Ordinal), seen.Order(StringComparer.Ordinal));
        Assert.True(replay.Run(ReplayRuns.GoldenData).Reproduced);
    }

    /// <summary>A match without a Capitol has nothing for the script to aim at, and it gives no orders.</summary>
    [Fact]
    public void TheScriptGivesNothingInAMatchWithoutACapitol()
    {
        var replay = ReplayRuns.Record("--map", "ridge", "--starts", "-,-", "--ticks", "600", "--orders", "33", "--script", "capitol");

        Assert.Empty(replay.Commands);
    }

    /// <summary>
    /// What this recorded match is for: it must reach the rules of structures on the map that a
    /// change could break. Throughout it no unit stands on a tile under a Capitol, and no Capitol
    /// loses a hit point.
    /// </summary>
    [Fact]
    public void ItsGoldenReplayReachesTheRulesItGuards()
    {
        var replay = ReplayRuns.ReadGolden(GoldenReplay);
        var match = ReplayRuns.Start(replay);
        var seen = KindsOfOrders(replay, match);
        var before = new MoveState[match.Movers.Count];
        var sentOntoACapitol = new HashSet<EntityId>();
        for (var tick = 0; tick < replay.TickCount; tick++)
        {
            // Which units the orders of this tick send onto a Capitol, and which somewhere else.
            foreach (var (_, command) in replay.Commands.Where(entry => entry.Tick == tick && entry.Command.Type != CommandType.Attack))
            {
                var onto = IsOnACapitol(match, new Sim.Numerics.FixedVector2(command.TargetX, command.TargetY));
                foreach (var entity in command.Entities)
                {
                    _ = onto ? sentOntoACapitol.Add(entity) : sentOntoACapitol.Remove(entity);
                }
            }

            match.Simulation.Tick();
            foreach (var refused in match.Simulation.Events.Where(simEvent => simEvent.Kind == SimEventKind.OrderRefused))
            {
                if (match.Structures.Contains(refused.Other))
                {
                    seen.Add("an order to attack a Capitol refused");
                }
            }

            for (var i = 0; i < before.Length; i++)
            {
                var unit = match.Movers.EntityAt(i);
                var mover = match.Movers.ValueAt(i);
                var position = match.Bodies.Get(unit).Position;
                var (x, y) = (position.X.FloorToInt(), position.Y.FloorToInt());
                Assert.True(match.Grid.IsPassable(x, y), $"Unit {unit.Value} stands on blocked ground at ({x}, {y}) in tick {tick}.");

                var beside = IsBesideACapitol(match, x, y);
                if (beside && mover.State == MoveState.Following)
                {
                    seen.Add("a unit on a route that leads round a Capitol");
                }

                if (beside && mover.State == MoveState.Idle && before[i] != MoveState.Idle)
                {
                    seen.Add(sentOntoACapitol.Contains(unit)
                        ? "a unit sent onto a Capitol that stops beside it"
                        : "a unit that ends its walk beside a Capitol");
                }

                if (mover.State != MoveState.Idle
                    && before[i] == MoveState.Idle
                    && !match.Grid.IsSegmentClear(position, mover.Destination)
                    && CrossesACapitol(match, position, mover.Destination))
                {
                    seen.Add("a unit whose straight way leads through a Capitol");
                }

                before[i] = mover.State;
            }
        }

        Assert.Equal(2, match.Structures.Count);
        for (var i = 0; i < match.Structures.Count; i++)
        {
            var health = match.Health.Get(match.Structures.EntityAt(i));
            Assert.Equal(health.Maximum, health.HitPoints);
        }

        string[] expected =
        [
            .. EveryKindOfOrder,
            "an order to attack a Capitol refused",
            "a unit on a route that leads round a Capitol",
            "a unit sent onto a Capitol that stops beside it",
            "a unit that ends its walk beside a Capitol",
            "a unit whose straight way leads through a Capitol",
        ];
        Assert.Equal(expected.Order(StringComparer.Ordinal), seen.Order(StringComparer.Ordinal));
    }

    /// <summary>What the commands of this script in a replay are, in the words of <see cref="EveryKindOfOrder"/>.</summary>
    private static HashSet<string> KindsOfOrders(Sim.Serialization.Replay replay, Match match)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, command) in replay.Commands)
        {
            var target = new Sim.Numerics.FixedVector2(command.TargetX, command.TargetY);
            switch (command.Type)
            {
                case CommandType.Attack:
                    Assert.True(match.Structures.Contains(command.TargetEntity), "The script ordered an attack at something that is no Capitol.");
                    seen.Add(match.Bodies.Get(command.TargetEntity).Owner == command.Player
                        ? "an order to attack the player's own Capitol"
                        : "an order to attack another player's Capitol");
                    break;
                case CommandType.AttackMove:
                    Assert.True(IsOnACapitol(match, target), "The script ordered an attack-move to somewhere else than a Capitol.");
                    seen.Add("an attack-move onto a Capitol");
                    break;
                case CommandType.Move:
                    seen.Add(IsOnACapitol(match, target) ? "a move onto a Capitol"
                        : IsBesideACapitol(match, target.X.FloorToInt(), target.Y.FloorToInt()) ? "a move to a tile beside a Capitol"
                        : "a move past a Capitol");
                    break;
                default:
                    Assert.Fail($"The script gave a command of type {command.Type}.");
                    break;
            }

            seen.Add(command.Entities.Length == 1 ? "an order for one unit" : "an order for a group");
        }

        return seen;
    }

    private static bool IsOnACapitol(Match match, Sim.Numerics.FixedVector2 point)
    {
        var (x, y) = (point.X.FloorToInt(), point.Y.FloorToInt());
        return Enumerable.Range(0, match.Structures.Count).Any(index => match.Structures.ValueAt(index).Tiles.Covers(x, y));
    }

    /// <summary>True for a tile that is not under a Capitol and touches one, by a side or a corner.</summary>
    private static bool IsBesideACapitol(Match match, int x, int y)
    {
        for (var i = 0; i < match.Structures.Count; i++)
        {
            var tiles = match.Structures.ValueAt(i).Tiles;
            var grown = new TileSquare(tiles.X - 1, tiles.Y - 1, tiles.Size + 2);
            if (grown.Covers(x, y) && !tiles.Covers(x, y))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when the straight line between two points passes over a tile under a Capitol.</summary>
    private static bool CrossesACapitol(Match match, Sim.Numerics.FixedVector2 from, Sim.Numerics.FixedVector2 to)
    {
        const int Steps = 400;
        for (var step = 0; step <= Steps; step++)
        {
            var x = from.X.Raw + (((long)to.X.Raw - from.X.Raw) * step / Steps);
            var y = from.Y.Raw + (((long)to.Y.Raw - from.Y.Raw) * step / Steps);
            if (IsOnACapitol(match, new Sim.Numerics.FixedVector2(Sim.Numerics.Fixed.FromRaw((int)x), Sim.Numerics.Fixed.FromRaw((int)y))))
            {
                return true;
            }
        }

        return false;
    }
}
