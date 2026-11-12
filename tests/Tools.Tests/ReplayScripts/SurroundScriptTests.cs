using Sim.Combat;
using Sim.Core;
using Sim.Map;
using Sim.Movement;
using Sim.Numerics;
using Sim.Setup;

namespace Tools.Tests.ReplayScripts;

/// <summary>
/// The script <c>surround</c> of <c>replay record</c> and the golden replay recorded for it: what
/// the script must give, and what that replay must reach.
/// </summary>
public class SurroundScriptTests
{
    private const string GoldenReplay = "ridge-surround.replay";

    /// <summary>The replay of the same script that was recorded with places round structures.</summary>
    private const string GoldenReplayRoundStructures = "ridge-footprints.replay";

    /// <summary>A unit that has stood this many turns to look out of reach of a target that stands still has looked for a place and found none.</summary>
    private const int LooksOfWaiting = 2;

    /// <summary>The rules of the places round a unit that both golden replays of the script reach.</summary>
    private static readonly string[] RulesOfPlacesByUnits =
    [
        "a unit walking to its place",
        "a unit on its place",
        "a unit on its place that attacks from it",
        "a unit that enemies stopped on the way to its place",
        "a unit that gave up its place by a target that still stands",
        "a unit that waits out of reach because every place is taken",
        "a unit that waited and then had a place",
        "a unit with a place by a worker",
        "units of two players with places by each other",
    ];

    private static readonly string[] EveryKindOfOrder =
    [
        "a band made",
        "a target made",
        "an attack by a band on one target",
        "an attack by more units than one target has places for",
        "an attack-move to where a target stands",
    ];

    /// <summary>The script gives every kind of order it is meant to: here in one long match on each map.</summary>
    [Theory]
    [InlineData("ridge")]
    [InlineData("switchback")]
    public void TheScriptGivesEveryKindOfItsOrders(string map)
    {
        var replay = ReplayRuns.Record("--map", map, "--ticks", "6000", "--orders", "33", "--script", "surround");

        Assert.All(replay.Commands, entry => Assert.True(IsOfThisScript(entry.Command), $"The script gave a command of type {entry.Command.Type}."));
        Assert.Equal(EveryKindOfOrder.Order(StringComparer.Ordinal), KindsOfOrders(replay).Order(StringComparer.Ordinal));
        Assert.True(replay.Run(ReplayRuns.GoldenData).Reproduced);
    }

    /// <summary>
    /// What this recorded match is for: it must reach the rules of the places round one target
    /// that a change could break. The match has the orders of <c>gather</c> as well, so that
    /// workers stand at their deposits when a band comes for them.
    /// </summary>
    /// <remarks>
    /// The match was recorded when only a unit had places. Since a structure has them too, its
    /// bands that come to a Capitol stand round it, the match takes another course from there,
    /// and it no longer has seven units on their places by one target. The match of
    /// <see cref="TheGoldenReplayWithPlacesRoundStructuresReachesTheRulesItGuards"/> has.
    /// </remarks>
    [Fact]
    public void ItsGoldenReplayReachesTheRulesItGuards()
    {
        string[] expected =
        [
            .. EveryKindOfOrder,
            .. RulesOfPlacesByUnits,
            "a unit on its place by a structure that attacks from it",
        ];
        Assert.Equal(expected.Order(StringComparer.Ordinal), Reached(ReplayRuns.ReadGolden(GoldenReplay)).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// What the second recorded match of this script is for. It was recorded with places round
    /// structures, with the same orders but for their seed, and reaches every rule of the places
    /// round a unit and the rules of the places round a footprint: its bands stand round the two
    /// Capitols, on every side of one, and wait behind when they have no place.
    /// </summary>
    [Fact]
    public void TheGoldenReplayWithPlacesRoundStructuresReachesTheRulesItGuards()
    {
        string[] expected =
        [
            .. EveryKindOfOrder,
            .. RulesOfPlacesByUnits,
            "seven units on their places by one target",
            "a unit on its place by a structure that attacks from it",
            "nine units on their places by one structure",
            "units on their places on all four sides of one structure",
            "a unit that waits out of reach of a structure while five of its side are on their places by it",
        ];
        Assert.Equal(expected.Order(StringComparer.Ordinal), Reached(ReplayRuns.ReadGolden(GoldenReplayRoundStructures)).Order(StringComparer.Ordinal));
    }

    /// <summary>What the match of a replay reaches, in the words of the list of <see cref="ItsGoldenReplayReachesTheRulesItGuards"/>.</summary>
    private static HashSet<string> Reached(Sim.Serialization.Replay replay)
    {
        var match = ReplayRuns.Start(replay);
        var seen = KindsOfOrders(replay);
        var before = new Dictionary<EntityId, Combatant>();
        var waited = new Dictionary<EntityId, int>();
        for (var tick = 0; tick < replay.TickCount; tick++)
        {
            before.Clear();
            for (var i = 0; i < match.Combatants.Count; i++)
            {
                before[match.Combatants.EntityAt(i)] = match.Combatants.ValueAt(i);
            }

            match.Simulation.Tick();
            seen.UnionWith(WhatTheTickDid(match, before, waited));
        }

        return seen;
    }

    private static bool IsOfThisScript(Command command) =>
        command.Type is CommandType.DebugSpawn or CommandType.AttackMove or CommandType.Attack;

    /// <summary>What the commands of this script in a replay are, in the words of <see cref="EveryKindOfOrder"/>.</summary>
    private static HashSet<string> KindsOfOrders(Sim.Serialization.Replay replay)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (tick, command) in replay.Commands.Where(entry => IsOfThisScript(entry.Command)))
        {
            switch (command.Type)
            {
                case CommandType.DebugSpawn:
                    var together = replay.Commands.Count(other =>
                        other.Tick == tick && other.Command.Type == CommandType.DebugSpawn && other.Command.Player == command.Player);
                    seen.Add(together > 1 ? "a band made" : "a target made");
                    break;
                case CommandType.Attack:
                    // Seven is the most that the design counts round one unit, ten round a wider one.
                    seen.Add(command.Entities.Length > 7 ? "an attack by more units than one target has places for" : "an attack by a band on one target");
                    break;
                default:
                    seen.Add("an attack-move to where a target stands");
                    break;
            }
        }

        return seen;
    }

    /// <summary>
    /// What the tick that has just run left the fighting units in, in the words of the list of
    /// <see cref="ItsGoldenReplayReachesTheRulesItGuards"/>.
    /// </summary>
    private static HashSet<string> WhatTheTickDid(Match match, Dictionary<EntityId, Combatant> before, Dictionary<EntityId, int> waited)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var onPlaces = new Dictionary<EntityId, int>();
        for (var i = 0; i < match.Combatants.Count; i++)
        {
            var entity = match.Combatants.EntityAt(i);
            var unit = match.Combatants.ValueAt(i);
            var body = match.Bodies.Get(entity);
            var mover = match.Movers.Get(entity);
            var targetStands = match.Bodies.TryGet(unit.Target, out var target)
                && match.World.Entities.IsAlive(unit.Target)
                && unit.Engaged
                && unit.AimedAt == target.Position;
            var hadWaited = waited.GetValueOrDefault(entity) >= LooksOfWaiting * match.Combat.LookInterval;
            if (unit.HasPlace)
            {
                if (hadWaited)
                {
                    seen.Add("a unit that waited and then had a place");
                }

                if (match.Workers.Contains(unit.PlaceFor))
                {
                    seen.Add("a unit with a place by a worker");
                }

                if (match.Combatants.TryGet(unit.PlaceFor, out var other) && other.HasPlace && other.PlaceFor == entity)
                {
                    seen.Add("units of two players with places by each other");
                }

                if (body.Position != unit.Place)
                {
                    seen.Add(mover.Waiting ? "a unit that enemies stopped on the way to its place" : "a unit walking to its place");
                }
                else
                {
                    seen.Add("a unit on its place");
                    onPlaces[unit.PlaceFor] = onPlaces.GetValueOrDefault(unit.PlaceFor) + 1;
                    if (unit.Wait > Fraction.Zero && unit.Fought == unit.Place)
                    {
                        seen.Add("a unit on its place that attacks from it");
                    }
                }
            }
            else if (before.TryGetValue(entity, out var was) && was.HasPlace && was.Target == unit.Target && targetStands)
            {
                seen.Add("a unit that gave up its place by a target that still stands");
            }

            // Out of reach of a target that has not moved since the unit came to a stop, with
            // nowhere to go: on its turn to look it finds its own side on every place.
            var profile = match.Combat.ProfileOf(body.Owner, unit.Chassis);
            var waits = profile.Melee
                && !unit.HasPlace
                && targetStands
                && match.Movers.Contains(unit.Target)
                && mover.State == MoveState.Idle
                && FixedVector2.DistanceSquaredRaw(body.Position, target.Position) > match.Combat.ReachSquared(profile.Line, LineOf(match, unit.Target));
            waited[entity] = waits ? waited.GetValueOrDefault(entity) + 1 : 0;
            if (waited[entity] >= LooksOfWaiting * match.Combat.LookInterval)
            {
                seen.Add("a unit that waits out of reach because every place is taken");
            }
        }

        foreach (var (target, count) in onPlaces)
        {
            if (!match.Structures.TryGet(target, out var structure))
            {
                if (count >= 7)
                {
                    seen.Add("seven units on their places by one target");
                }

                continue;
            }

            seen.UnionWith(RoundAStructure(match, target, structure.Tiles, count));
        }

        return seen;
    }

    /// <summary>
    /// What the units on their places by one structure come to after a tick, in the words of the
    /// lists of the tests of the golden replays.
    /// </summary>
    private static HashSet<string> RoundAStructure(Match match, EntityId target, TileSquare tiles, int onPlaces)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var (west, east) = (Fixed.FromInt(tiles.X), Fixed.FromInt(tiles.X + tiles.Size));
        var (north, south) = (Fixed.FromInt(tiles.Y), Fixed.FromInt(tiles.Y + tiles.Size));
        var sides = new HashSet<string>(StringComparer.Ordinal);
        var waiting = false;
        for (var i = 0; i < match.Combatants.Count; i++)
        {
            var entity = match.Combatants.EntityAt(i);
            var unit = match.Combatants.ValueAt(i);
            if (unit.Target != target)
            {
                continue;
            }

            var at = match.Bodies.Get(entity).Position;
            if (!unit.HasPlace)
            {
                waiting |= unit.Engaged
                    && match.Movers.Get(entity).State == MoveState.Idle
                    && match.Combat.ProfileOf(match.Bodies.Get(entity).Owner, unit.Chassis).Melee
                    && tiles.DistanceSquaredRawTo(at) > match.Combat.StructureReachSquared;
                continue;
            }

            Assert.True(tiles.DistanceSquaredRawTo(unit.Place) <= match.Combat.StructureReachSquared, "A place by a structure is out of reach of it.");
            if (at != unit.Place)
            {
                continue;
            }

            if (unit.Wait > Fraction.Zero && unit.Fought == unit.Place)
            {
                seen.Add("a unit on its place by a structure that attacks from it");
            }

            // A place round a corner is on neither of the two sides that meet there.
            var alongX = at.X >= west && at.X <= east;
            var alongY = at.Y >= north && at.Y <= south;
            if (alongX != alongY)
            {
                sides.Add(alongY ? (at.X < west ? "west" : "east") : (at.Y < north ? "north" : "south"));
            }
        }

        if (onPlaces >= 9)
        {
            seen.Add("nine units on their places by one structure");
        }

        if (sides.Count == 4)
        {
            seen.Add("units on their places on all four sides of one structure");
        }

        if (waiting && onPlaces >= 5)
        {
            seen.Add("a unit that waits out of reach of a structure while five of its side are on their places by it");
        }

        return seen;
    }

    private static int LineOf(Match match, EntityId entity) =>
        match.Combatants.TryGet(entity, out var unit) ? match.Data.Chassis[unit.Chassis].Line : CombatRules.NoLine;
}
