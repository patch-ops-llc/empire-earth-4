using Sim.Combat;
using Sim.Core;
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

    /// <summary>A unit that has stood this many ticks out of reach of a target that stands still has looked for a place and found none.</summary>
    private static int TicksOfWaiting(Match match) => 2 * match.Combat.LookInterval;

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
    [Fact(Skip = TestBuild.SurroundMeetsFog)]
    public void ItsGoldenReplayReachesTheRulesItGuards()
    {
        var replay = ReplayRuns.ReadGolden(GoldenReplay);
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

        string[] expected =
        [
            .. EveryKindOfOrder,
            "a unit walking to its place",
            "a unit on its place",
            "a unit on its place that attacks from it",
            "a unit that enemies stopped on the way to its place",
            "a unit that gave up its place by a target that still stands",
            "a unit that waits out of reach because every place is taken",
            "a unit that waited and then had a place",
            "a unit with a place by a worker",
            "seven units on their places by one target",
            "units of two players with places by each other",
        ];
        Assert.Equal(expected.Order(StringComparer.Ordinal), seen.Order(StringComparer.Ordinal));
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
            var hadWaited = waited.GetValueOrDefault(entity) >= TicksOfWaiting(match);
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
            if (waited[entity] >= TicksOfWaiting(match))
            {
                seen.Add("a unit that waits out of reach because every place is taken");
            }
        }

        if (onPlaces.Values.Any(count => count >= 7))
        {
            seen.Add("seven units on their places by one target");
        }

        return seen;
    }

    private static int LineOf(Match match, EntityId entity) =>
        match.Combatants.TryGet(entity, out var unit) ? match.Data.Chassis[unit.Chassis].Line : CombatRules.NoLine;
}
