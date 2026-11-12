using Sim.Core;
using Sim.Numerics;
using Sim.Setup;

namespace Tools.Tests.ReplayScripts;

/// <summary>
/// The script <c>blast</c> of <c>replay record</c> and the golden replay recorded for it: what
/// the script must give, and what that replay must reach.
/// </summary>
public class BlastScriptTests
{
    private const string GoldenReplay = "switchback-blast.replay";

    private static readonly string[] EveryKindOfOrder =
    [
        "a clump made",
        "a unit with a blast made",
        "a unit made on the spot of an enemy",
        "a unit made inside the minimum range of an enemy with a blast",
        "an attack by units with a blast",
        "a stop for units with a blast",
    ];

    /// <summary>
    /// The script gives every kind of order it is meant to: here in one long match on each map.
    /// The rarest kind comes about once in 400 ticks, so a shorter match can miss it.
    /// </summary>
    [Theory]
    [InlineData("ridge")]
    [InlineData("switchback")]
    public void TheScriptGivesEveryKindOfItsOrders(string map)
    {
        var replay = ReplayRuns.Record("--map", map, "--ticks", "8000", "--orders", "33", "--script", "blast");

        Assert.All(replay.Commands, entry => Assert.True(IsOfThisScript(entry.Command), $"The script gave a command of type {entry.Command.Type}."));
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

    /// <summary>
    /// What this recorded match is for: it must reach the rules of the blast that a change could
    /// break. The match has move orders beside the script's own, so that targets walk while the
    /// shells fly, and three players, so that one blast reaches the units of two.
    /// </summary>
    [Fact]
    public void ItsGoldenReplayReachesTheRulesItGuards()
    {
        var replay = ReplayRuns.ReadGolden(GoldenReplay);
        var match = ReplayRuns.Start(replay);
        var due = replay.Commands.ToLookup(entry => entry.Tick, entry => entry.Command);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var before = new Before();
        for (var tick = 0; tick < replay.TickCount; tick++)
        {
            seen.UnionWith(KindsOfOrders(match, [.. due[tick].Where(IsOfThisScript)]));
            before.Take(match);
            match.Simulation.Tick();
            seen.UnionWith(WhatTheBlastsDid(match, before));
        }

        string[] expected =
        [
            .. EveryKindOfOrder,
            "a blast",
            "a blast that reaches several enemies",
            "a blast that reaches enemies of two players",
            "a blast that reaches nobody",
            "a blast that reaches a worker",
            "a blast whose enemies take different damage",
            "a blast with a unit of its owner's side inside it",
            "a blast on a target that has moved since the shell was fired",
            "a shell that lands where its dead target stood",
            "an enemy killed by a blast",
            "an enemy inside the minimum range of a unit with a blast",
        ];
        Assert.Equal(expected.Order(StringComparer.Ordinal), seen.Order(StringComparer.Ordinal));
    }

    private static bool IsOfThisScript(Command command) =>
        command.Type is CommandType.DebugSpawn or CommandType.Attack or CommandType.Stop;

    private static bool HasBlast(Match match, int chassis) => match.Research.Tech(0).Blueprints[chassis].BlastRadius is not null;

    private static bool HasBlast(Match match, EntityId entity) =>
        match.Combatants.TryGet(entity, out var unit) && HasBlast(match, unit.Chassis);

    /// <summary>
    /// What the commands of this script that are due in the next tick are, in the words of
    /// <see cref="EveryKindOfOrder"/>. The match is as it is before that tick, which is what the
    /// script saw when it gave them.
    /// </summary>
    private static HashSet<string> KindsOfOrders(Match match, IReadOnlyList<Command> commands)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var command in commands)
        {
            var own = command.Entities.ToArray().Where(entity => match.Bodies.TryGet(entity, out var body) && body.Owner == command.Player).ToList();
            var allHaveABlast = own.Count > 0 && own.All(entity => HasBlast(match, entity));
            switch (command.Type)
            {
                case CommandType.DebugSpawn when IsMade(match, command):
                    seen.Add(KindOfSpawn(match, command, commands));
                    break;
                case CommandType.Attack when allHaveABlast && match.Bodies.TryGet(command.TargetEntity, out var target) && target.Owner != command.Player:
                    seen.Add("an attack by units with a blast");
                    break;
                case CommandType.Stop when allHaveABlast:
                    seen.Add("a stop for units with a blast");
                    break;
                default:
                    break;
            }
        }

        seen.Remove("");
        return seen;
    }

    private static bool IsMade(Match match, Command command) =>
        command.Type == CommandType.DebugSpawn
        && command.Player < match.Players.Count
        && command.ContentId >= 0
        && command.ContentId < match.Data.Chassis.Count
        && match.Grid.IsPassable(new FixedVector2(command.TargetX, command.TargetY));

    private static string KindOfSpawn(Match match, Command command, IReadOnlyList<Command> commands)
    {
        if (HasBlast(match, command.ContentId))
        {
            return "a unit with a blast made";
        }

        var point = new FixedVector2(command.TargetX, command.TargetY);
        var insideAMinimumRange = false;
        for (var i = 0; i < match.Bodies.Count; i++)
        {
            var entity = match.Bodies.EntityAt(i);
            var body = match.Bodies.ValueAt(i);
            if (body.Owner == command.Player)
            {
                continue;
            }

            if (body.Position == point)
            {
                return "a unit made on the spot of an enemy";
            }

            insideAMinimumRange |= HasBlast(match, entity)
                && FixedVector2.DistanceSquaredRaw(body.Position, point)
                    < match.Combat.ProfileOf(body.Owner, match.Combatants.Get(entity).Chassis).MinRangeSquared;
        }

        if (insideAMinimumRange)
        {
            return "a unit made inside the minimum range of an enemy with a blast";
        }

        var together = commands.Count(other => IsMade(match, other) && other.Player == command.Player && other.ContentId == command.ContentId);
        return together >= 3 ? "a clump made" : "";
    }

    /// <summary>
    /// What the shells that landed in the tick that has just run did, from its events and from
    /// the state after it, in the words of the list of
    /// <see cref="ItsGoldenReplayReachesTheRulesItGuards"/>.
    /// </summary>
    private static HashSet<string> WhatTheBlastsDid(Match match, Before before)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var events = match.Simulation.Events;
        foreach (var blast in events.Where(reported => reported.Kind == SimEventKind.Blast))
        {
            seen.Add("a blast");
            var hits = events.Where(reported => reported.Kind == SimEventKind.ShotHit && reported.Other == blast.Entity).ToList();
            if (hits.Count == 0)
            {
                seen.Add("a blast that reaches nobody");
            }

            if (hits.Count > 1)
            {
                seen.Add("a blast that reaches several enemies");
            }

            if (hits.Select(hit => OwnerOf(match, before, hit.Entity)).Distinct().Count() > 1)
            {
                seen.Add("a blast that reaches enemies of two players");
            }

            if (hits.Select(hit => hit.Amount).Distinct().Count() > 1)
            {
                seen.Add("a blast whose enemies take different damage");
            }

            if (hits.Any(hit => before.Workers.Contains(hit.Entity)))
            {
                seen.Add("a blast that reaches a worker");
            }

            if (hits.Any(hit => events.Any(reported => reported.Kind == SimEventKind.Death && reported.Entity == hit.Entity)))
            {
                seen.Add("an enemy killed by a blast");
            }

            var (target, firedAt) = before.Shells[blast.Entity];
            if (!before.Owners.ContainsKey(target))
            {
                seen.Add("a shell that lands where its dead target stood");
            }
            else if (blast.Position != firedAt)
            {
                seen.Add("a blast on a target that has moved since the shell was fired");
            }

            var radiusSquared = FixedVector2.SquareRaw(Fixed.FromRaw((int)blast.Amount));
            for (var i = 0; i < match.Bodies.Count; i++)
            {
                var body = match.Bodies.ValueAt(i);
                if (body.Owner == blast.Player && FixedVector2.DistanceSquaredRaw(body.Position, blast.Position) <= radiusSquared)
                {
                    seen.Add("a blast with a unit of its owner's side inside it");
                }
            }
        }

        for (var i = 0; i < match.Combatants.Count; i++)
        {
            var entity = match.Combatants.EntityAt(i);
            if (!HasBlast(match, entity))
            {
                continue;
            }

            var body = match.Bodies.Get(entity);
            var minimum = match.Combat.ProfileOf(body.Owner, match.Combatants.ValueAt(i).Chassis).MinRangeSquared;
            for (var other = 0; other < match.Bodies.Count; other++)
            {
                var enemy = match.Bodies.ValueAt(other);
                if (enemy.Owner != body.Owner && FixedVector2.DistanceSquaredRaw(body.Position, enemy.Position) < minimum)
                {
                    seen.Add("an enemy inside the minimum range of a unit with a blast");
                }
            }
        }

        return seen;
    }

    /// <summary>The owner of something a blast reached, which may have been made in this tick or have died in it.</summary>
    private static int OwnerOf(Match match, Before before, EntityId entity) =>
        before.Owners.TryGetValue(entity, out var owner) ? owner : match.Bodies.Get(entity).Owner;

    /// <summary>What a test needs of the match as it was before a tick, to say what the tick did.</summary>
    private sealed class Before
    {
        /// <summary>Everything on the map, with its owner.</summary>
        public Dictionary<EntityId, int> Owners { get; } = [];

        public HashSet<EntityId> Workers { get; } = [];

        /// <summary>Each shell in the air, with its target and where the target stood when it was first seen in the air.</summary>
        public Dictionary<EntityId, (EntityId Target, FixedVector2 FiredAt)> Shells { get; } = [];

        public void Take(Match match)
        {
            Owners.Clear();
            for (var i = 0; i < match.Bodies.Count; i++)
            {
                Owners[match.Bodies.EntityAt(i)] = match.Bodies.ValueAt(i).Owner;
            }

            Workers.Clear();
            for (var i = 0; i < match.Workers.Count; i++)
            {
                Workers.Add(match.Workers.EntityAt(i));
            }

            for (var i = 0; i < match.Shots.Count; i++)
            {
                var shot = match.Shots.ValueAt(i);
                if (shot.Blast > Fixed.Zero)
                {
                    Shells.TryAdd(match.Shots.EntityAt(i), (shot.Target, shot.Aim));
                }
            }
        }
    }
}
