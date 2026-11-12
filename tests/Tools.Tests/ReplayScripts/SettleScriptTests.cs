using Sim.Construction;
using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Map;
using Sim.Numerics;
using Sim.Setup;
using Sim.Vision;

namespace Tools.Tests.ReplayScripts;

/// <summary>
/// The script <c>settle</c> of <c>replay record</c> and the golden replay recorded for it: what
/// the script must give, and what that replay must reach.
/// </summary>
public class SettleScriptTests
{
    private const string GoldenReplay = "ridge-settlers.replay";

    private static readonly string[] EveryKindOfOrder =
    [
        "workers sent to work at a deposit",
        "workers sent out to explore",
        "workers sent to look at a structure of another player",
        "workers sent home",
        "an Outpost ordered on ground the player has explored",
        "an Outpost ordered on ground the player has not explored",
        "a Capitol ordered on ground the player has not explored",
        "a Generator ordered on ground the player has explored",
        "a Generator ordered on ground of the player's territory that it has not explored",
        "an Outpost ordered onto a structure the player remembers",
        "a foundation cancelled",
        "a foundation cancelled that another player remembers and does not see",
    ];

    /// <summary>What the golden replay must reach: every kind of order, and what the orders lead to.</summary>
    private static readonly string[] Expected =
    [
        .. EveryKindOfOrder,
        "a structure that is building itself, whose owner sees its tiles and no further",
        "a finished structure that sees as far as its file says with no unit of its owner near",
        "a structure of another player seen",
        "a structure seen by a tile of its footprint and not by the tile under its center",
        "a structure remembered and not seen now",
        "a structure remembered as it was building itself that is finished by now",
        "a structure that is gone and still remembered",
        "a structure forgotten when its tiles were seen without it",
        "a structure the player has never seen on ground it has explored",
        "a build order refused: NotExplored",
        "a build order refused: SiteNotFree",
        "a build order refused: CannotAfford",
        "a build order refused for a structure the player remembers",
        "a build order refused for a structure that is gone and still remembered",
        "a build order taken that the true state would refuse",
        "a build order ended: SiteNotFree",
        "a build order ended: TooCloseToEnemyTerritory",
        "a build order ended: OtherOrder",
        "a foundation laid",
        "a Generator finished",
        "an Outpost finished",
        "a territory that an Outpost gave",
    ];

    /// <summary>
    /// The script gives every kind of order it is meant to: here in one long match on each map.
    /// </summary>
    /// <remarks>
    /// The rarest kind needs a worker to have walked to a foundation of another player and away
    /// again before the foundation is finished. On <c>switchback</c> the way across is long, and
    /// not every match of this length has one: the orders here are from a seed whose match has.
    /// </remarks>
    [Theory]
    [InlineData("ridge", "33")]
    [InlineData("switchback", "58")]
    public void TheScriptGivesEveryKindOfItsOrders(string map, string orders)
    {
        var replay = ReplayRuns.Record("--map", map, "--ticks", "12000", "--orders", orders, "--script", "settle");

        var match = ReplayRuns.Start(replay);
        var due = replay.Commands.ToLookup(entry => entry.Tick, entry => entry.Command);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var tick = 0; tick < replay.TickCount; tick++)
        {
            seen.UnionWith(KindsOfOrders(match, [.. due[tick]]));
            match.Simulation.Tick();
        }

        Assert.Equal(EveryKindOfOrder.Order(StringComparer.Ordinal), seen.Order(StringComparer.Ordinal));
        Assert.True(replay.Run(ReplayRuns.GoldenData).Reproduced);
    }

    /// <summary>A match without workers has nobody to explore or build, and the script gives no orders.</summary>
    [Fact]
    public void TheScriptGivesNothingInAMatchWithoutWorkers()
    {
        var replay = ReplayRuns.Record("--map", "ridge", "--starts", "-,-", "--ticks", "600", "--orders", "33", "--script", "settle");

        Assert.Empty(replay.Commands);
    }

    /// <summary>
    /// What this recorded match is for: it must reach the rules of the fog of war with
    /// structures that a change could break (design 5.3, 5.4 and 2.5; design 8.4, rows 11, 23,
    /// 24, 29 and 39). The match has the orders of this script alone. It also has what the
    /// replay of the builders had before orders were judged by what a player has explored: an
    /// Outpost built, the territory it gives, and one refused on arrival for the enemy's
    /// territory.
    /// </summary>
    [Fact]
    public void ItsGoldenReplayReachesTheRulesItGuards()
    {
        var replay = ReplayRuns.ReadGolden(GoldenReplay);
        var (match, seen) = Reach(replay);

        Assert.Equal(Expected.Order(StringComparer.Ordinal), seen.Order(StringComparer.Ordinal));

        // By the end every player has been to more of the map than it sees, and none has seen all of it.
        var grid = match.Vision.Grid;
        for (var player = 0; player < match.Players.Count; player++)
        {
            Assert.True(grid.CountExplored(player) > 2 * grid.CountVisible(player), $"Player {player} has explored {grid.CountExplored(player)} tiles.");
            Assert.True(grid.CountExplored(player) < grid.Width * grid.Height);
        }
    }

    /// <summary>Runs a replay and gives the match at its end with everything its ticks did.</summary>
    private static (Match Match, HashSet<string> Seen) Reach(Sim.Serialization.Replay replay)
    {
        var match = ReplayRuns.Start(replay);
        var due = replay.Commands.ToLookup(entry => entry.Tick, entry => entry.Command);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var watch = new Watch(match);
        for (var tick = 0; tick < replay.TickCount; tick++)
        {
            Command[] commands = [.. due[tick]];
            seen.UnionWith(KindsOfOrders(match, commands));
            watch.BeforeTheTick(commands);
            match.Simulation.Tick();
            seen.UnionWith(watch.WhatTheTickDid(tick));
        }

        return (match, seen);
    }

    /// <summary>
    /// What the commands that are due in the next tick are, in the words of
    /// <see cref="EveryKindOfOrder"/>. The match is as it is before that tick, which is what the
    /// script saw when it gave them, and what the players know is what they know when the commands run.
    /// </summary>
    private static HashSet<string> KindsOfOrders(Match match, IReadOnlyList<Command> commands)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var vision = match.Vision;
        foreach (var command in commands)
        {
            var player = command.Player;
            switch (command.Type)
            {
                case CommandType.Gather:
                    Assert.Equal(WorkSiteKind.Deposit, match.WorkSites.Get(command.TargetEntity).Kind);
                    seen.Add("workers sent to work at a deposit");
                    break;
                case CommandType.Move:
                    seen.Add(IsHome(match, command) ? "workers sent home"
                        : IsBesideAStructureOfAnother(match, command) ? "workers sent to look at a structure of another player"
                        : "workers sent out to explore");
                    break;
                case CommandType.CancelBuild:
                    var foundation = Assert.Single(command.Entities.ToArray());
                    Assert.True(match.Foundations.Contains(foundation));
                    var remembered = Enumerable.Range(0, match.Players.Count)
                        .Any(other => other != player && vision.Memory.TryGet(other, foundation, out _) && !vision.Sees(other, foundation));
                    seen.Add(remembered ? "a foundation cancelled that another player remembers and does not see" : "a foundation cancelled");
                    break;
                case CommandType.Build:
                    var structure = match.Data.StructureOf(command.ContentId)!;
                    var tiles = new TileSquare(command.TargetX.FloorToInt(), command.TargetY.FloorToInt(), structure.Footprint);
                    var explored = vision.Grid.AreAllExplored(player, tiles);
                    var name = structure.Id == "generator" ? "a Generator" : structure.Id == "capitol" ? "a Capitol" : "an Outpost";
                    Assert.True(structure.Id is "generator" or "capitol" or "outpost", $"The script ordered '{structure.Id}'.");
                    if (structure.Id == "outpost" && RemembersUnseenOn(match, player, tiles))
                    {
                        seen.Add("an Outpost ordered onto a structure the player remembers");
                    }
                    else if (structure.Id == "generator" && !explored)
                    {
                        Assert.Equal(BuildProblem.None, match.Construction.CheckSiteAsKnown(player, structure, tiles.X, tiles.Y));
                        seen.Add("a Generator ordered on ground of the player's territory that it has not explored");
                    }
                    else
                    {
                        seen.Add($"{name} ordered on ground the player has {(explored ? "explored" : "not explored")}");
                    }

                    break;
                default:
                    Assert.Fail($"The script gave a command of type {command.Type}.");
                    break;
            }
        }

        return seen;
    }

    /// <summary>The order sends its units to the center of one of the player's territories.</summary>
    private static bool IsHome(Match match, Command command)
    {
        for (var i = 0; i < match.Territories.Count; i++)
        {
            var territory = match.Territories.ValueAt(i);
            if (territory.Owner == command.Player
                && command.TargetX == Fixed.FromInt(territory.CenterX2) / Fixed.FromInt(2)
                && command.TargetY == Fixed.FromInt(territory.CenterY2) / Fixed.FromInt(2))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The order sends its units within a worker's sight of a structure of another player, standing or remembered.</summary>
    private static bool IsBesideAStructureOfAnother(Match match, Command command)
    {
        var x = command.TargetX.FloorToInt();
        var y = command.TargetY.FloorToInt();
        bool IsNear(TileSquare tiles) => x >= tiles.X - 3 && x < tiles.X + tiles.Size + 3 && y >= tiles.Y - 3 && y < tiles.Y + tiles.Size + 3;

        for (var i = 0; i < match.Structures.Count; i++)
        {
            var structure = match.Structures.EntityAt(i);
            if (match.World.Entities.IsAlive(structure) && match.Bodies.Get(structure).Owner != command.Player && IsNear(match.Structures.ValueAt(i).Tiles))
            {
                return true;
            }
        }

        var memory = match.Vision.Memory;
        for (var i = 0; i < memory.Count(command.Player); i++)
        {
            if (IsNear(memory.At(command.Player, i).Tiles))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Overlap(TileSquare a, TileSquare b) =>
        a.X < b.X + b.Size && b.X < a.X + a.Size && a.Y < b.Y + b.Size && b.Y < a.Y + a.Size;

    /// <summary>The player remembers a structure on one of these tiles and does not see it now.</summary>
    private static bool RemembersUnseenOn(Match match, int player, TileSquare tiles) =>
        RememberedOn(match, player, tiles).Any();

    private static IEnumerable<RememberedStructure> RememberedOn(Match match, int player, TileSquare tiles)
    {
        var memory = match.Vision.Memory;
        for (var i = 0; i < memory.Count(player); i++)
        {
            var seen = memory.At(player, i);
            if (Overlap(seen.Tiles, tiles) && !match.Vision.Sees(player, seen.Entity))
            {
                yield return seen;
            }
        }
    }

    /// <summary>Follows a match tick by tick and says what each tick did, in the words of the golden replay's list.</summary>
    private sealed class Watch(Match match)
    {
        private readonly List<(Command Command, string What)> _expected = [];
        private readonly HashSet<EntityId> _standing = [];
        private readonly HashSet<EntityId> _finished = [];
        private HashSet<(int Player, EntityId Entity)> _memories = [];

        /// <summary>Notes what the build orders of the next tick are judged against: what their player knows, and the truth.</summary>
        public void BeforeTheTick(IReadOnlyList<Command> commands)
        {
            _expected.Clear();
            foreach (var command in commands)
            {
                if (command.Type != CommandType.Build)
                {
                    continue;
                }

                var structure = match.Data.StructureOf(command.ContentId)!;
                var x = command.TargetX.FloorToInt();
                var y = command.TargetY.FloorToInt();
                var known = match.Construction.CheckSiteAsKnown(command.Player, structure, x, y);
                var truth = match.Construction.CheckSite(command.Player, structure, x, y);
                if (known == BuildProblem.None && truth != BuildProblem.None)
                {
                    _expected.Add((command, "a build order taken that the true state would refuse"));
                }
                else if (known == BuildProblem.SiteNotFree)
                {
                    var remembered = RememberedOn(match, command.Player, new TileSquare(x, y, structure.Footprint)).ToArray();
                    if (remembered.Any(seen => !match.World.Entities.IsAlive(seen.Entity)) && truth != BuildProblem.SiteNotFree)
                    {
                        _expected.Add((command, "a build order refused for a structure that is gone and still remembered"));
                    }
                    else if (remembered.Length > 0)
                    {
                        _expected.Add((command, "a build order refused for a structure the player remembers"));
                    }
                }
            }
        }

        public HashSet<string> WhatTheTickDid(int tick)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var roster = match.Data.Rules.Structures;
            var events = match.Simulation.Events;
            var vision = match.Vision;
            var grid = vision.Grid;
            foreach (var simEvent in events)
            {
                switch (simEvent.Kind)
                {
                    case SimEventKind.BuildRefused:
                        seen.Add($"a build order refused: {(BuildProblem)simEvent.Amount}");
                        break;
                    case SimEventKind.BuildOrderEnded:
                        seen.Add($"a build order ended: {(BuildProblem)simEvent.Amount}");
                        break;
                    case SimEventKind.StructurePlaced:
                        seen.Add("a foundation laid");
                        break;
                    case SimEventKind.StructureFinished:
                        seen.Add(roster[(int)simEvent.Amount] switch
                        {
                            "generator" => "a Generator finished",
                            "outpost" => "an Outpost finished",
                            var other => $"a structure finished: {other}",
                        });
                        if (match.Territories.Contains(simEvent.Entity) && roster[(int)simEvent.Amount] == "outpost")
                        {
                            seen.Add("a territory that an Outpost gave");
                        }

                        break;
                    default:
                        break;
                }
            }

            // What was noted of the build orders of this tick: taken, or refused for the site.
            foreach (var (command, what) in _expected)
            {
                var worker = command.Entities.IsEmpty ? EntityId.None : command.Entities[0];
                var refusedForTheSite = events.Any(simEvent =>
                    simEvent.Kind == SimEventKind.BuildRefused && simEvent.Player == command.Player && simEvent.Entity == worker && (BuildProblem)simEvent.Amount == BuildProblem.SiteNotFree);
                var refused = events.Any(simEvent => simEvent.Kind == SimEventKind.BuildRefused && simEvent.Player == command.Player && simEvent.Entity == worker);
                if (what.StartsWith("a build order taken", StringComparison.Ordinal) ? !refused : refusedForTheSite)
                {
                    seen.Add(what);
                }
            }

            var worked = tick % vision.RefreshTicks == 0;
            for (var i = 0; i < match.Structures.Count; i++)
            {
                var entity = match.Structures.EntityAt(i);
                if (!match.World.Entities.IsAlive(entity))
                {
                    continue;
                }

                var structure = match.Structures.ValueAt(i);
                var body = match.Bodies.Get(entity);
                var def = match.Data.StructureOf(structure.Type)!;
                var isFinished = !match.Foundations.Contains(entity);

                // What it sees, once sight has been worked out with it there and as it is now.
                if (worked && _standing.Contains(entity))
                {
                    Assert.True(
                        Enumerable.Range(0, structure.Size * structure.Size).All(tile => grid.IsVisible(body.Owner, structure.X + (tile % structure.Size), structure.Y + (tile / structure.Size))),
                        $"Player {body.Owner} does not see the tiles of its structure {entity.Value} in tick {tick}.");
                    if (isFinished && _finished.Contains(entity))
                    {
                        AssertSeesAround(body.Owner, body.Position, def.Sight, tick);
                        if (!IsAUnitNear(body.Owner, body.Position, def.Sight + 8))
                        {
                            seen.Add("a finished structure that sees as far as its file says with no unit of its owner near");
                        }
                    }
                    else if (!isFinished && !grid.IsVisible(body.Owner, structure.X - 1, structure.Y) && !grid.IsVisible(body.Owner, structure.X + structure.Size, structure.Y))
                    {
                        seen.Add("a structure that is building itself, whose owner sees its tiles and no further");
                    }
                }

                for (var player = 0; player < match.Players.Count; player++)
                {
                    if (player == body.Owner)
                    {
                        Assert.True(vision.Sees(player, entity));
                        Assert.False(vision.Memory.TryGet(player, entity, out _), $"Player {player} remembers its own structure {entity.Value}.");
                        continue;
                    }

                    var sees = grid.IsAnyVisible(player, structure.Tiles);
                    Assert.True(sees == vision.Sees(player, entity), $"Player {player} and structure {entity.Value} in tick {tick}: one of its tiles is {(sees ? "visible" : "not visible")}.");
                    var remembers = vision.Memory.TryGet(player, entity, out var memory);
                    if (sees)
                    {
                        seen.Add("a structure of another player seen");
                        if (!grid.IsVisible(player, body.Position))
                        {
                            seen.Add("a structure seen by a tile of its footprint and not by the tile under its center");
                        }

                        // Sight was worked out in this tick, and a structure that stood then was written down.
                        if (worked && _standing.Contains(entity))
                        {
                            Assert.True(remembers && memory.Tick == tick, $"Player {player} sees structure {entity.Value} in tick {tick} and has not written it down.");
                            Assert.Equal((body.Owner, structure.Type, structure.Tiles), (memory.Owner, memory.Type, memory.Tiles));
                        }
                    }
                    else if (remembers)
                    {
                        seen.Add("a structure remembered and not seen now");
                        if (!memory.IsFinished && isFinished)
                        {
                            seen.Add("a structure remembered as it was building itself that is finished by now");
                        }
                    }
                    else if (grid.AreAllExplored(player, structure.Tiles))
                    {
                        seen.Add("a structure the player has never seen on ground it has explored");
                    }
                }
            }

            // What each player remembers: a structure that is gone stays until sight is worked out with one of its tiles seen.
            var memories = new HashSet<(int Player, EntityId Entity)>();
            for (var player = 0; player < match.Players.Count; player++)
            {
                for (var i = 0; i < vision.Memory.Count(player); i++)
                {
                    var memory = vision.Memory.At(player, i);
                    memories.Add((player, memory.Entity));
                    if (!match.World.Entities.IsAlive(memory.Entity))
                    {
                        seen.Add("a structure that is gone and still remembered");
                        Assert.False(worked && grid.IsAnyVisible(player, memory.Tiles), $"Player {player} sees the tiles of structure {memory.Entity.Value}, which is gone, in tick {tick} and still remembers it.");
                    }
                }
            }

            foreach (var (player, entity) in _memories)
            {
                if (!memories.Contains((player, entity)))
                {
                    Assert.True(worked && !match.World.Entities.IsAlive(entity), $"Player {player} forgot structure {entity.Value} in tick {tick}.");
                    seen.Add("a structure forgotten when its tiles were seen without it");
                }
            }

            _memories = memories;
            _standing.Clear();
            _finished.Clear();
            for (var i = 0; i < match.Structures.Count; i++)
            {
                var entity = match.Structures.EntityAt(i);
                if (match.World.Entities.IsAlive(entity))
                {
                    _standing.Add(entity);
                    if (!match.Foundations.Contains(entity))
                    {
                        _finished.Add(entity);
                    }
                }
            }

            return seen;
        }

        /// <summary>Every tile whose center is within the sight of a point is visible to the player.</summary>
        private void AssertSeesAround(int player, FixedVector2 center, int sight, int tick)
        {
            var grid = match.Vision.Grid;
            var reach = FixedVector2.SquareRaw(Fixed.FromInt(sight));
            for (var y = Math.Max(0, center.Y.FloorToInt() - sight - 1); y <= Math.Min(grid.Height - 1, center.Y.FloorToInt() + sight + 1); y++)
            {
                for (var x = Math.Max(0, center.X.FloorToInt() - sight - 1); x <= Math.Min(grid.Width - 1, center.X.FloorToInt() + sight + 1); x++)
                {
                    if (FixedVector2.DistanceSquaredRaw(center, MapGrid.TileCenter(x, y)) <= reach)
                    {
                        Assert.True(grid.IsVisible(player, x, y), $"Player {player} does not see the tile ({x}, {y}) within {sight} of its structure at {center} in tick {tick}.");
                    }
                }
            }
        }

        private bool IsAUnitNear(int player, FixedVector2 center, int tiles)
        {
            var reach = FixedVector2.SquareRaw(Fixed.FromInt(tiles));
            for (var i = 0; i < match.Movers.Count; i++)
            {
                var unit = match.Movers.EntityAt(i);
                if (match.World.Entities.IsAlive(unit)
                    && match.Bodies.Get(unit) is var body
                    && body.Owner == player
                    && FixedVector2.DistanceSquaredRaw(center, body.Position) <= reach)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
