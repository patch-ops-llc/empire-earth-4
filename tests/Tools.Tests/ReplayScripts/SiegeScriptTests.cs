using Sim.Combat;
using Sim.Core;
using Sim.Numerics;
using Sim.Setup;

namespace Tools.Tests.ReplayScripts;

/// <summary>
/// The script <c>siege</c> of <c>replay record</c> and the golden replay recorded for it: what
/// the script must give, and what that replay must reach.
/// </summary>
public class SiegeScriptTests
{
    private const string GoldenReplay = "switchback-siege.replay";

    private static readonly string[] EveryKindOfOrder =
    [
        "a Tower placed",
        "a wall placed",
        "a row of walls placed",
        "a structure that does not attack placed",
        "a placement refused",
        "a fighting unit made",
        "an attack on a structure of an enemy",
        "an attack on a wall of an enemy",
        "an attack on a structure of the player's own",
        "an attack-move onto a structure of an enemy",
        "a stop",
    ];

    /// <summary>
    /// The script gives every kind of order it is meant to: here in one long match on each map.
    /// The rarest kind comes about once in 350 ticks, and needs a wall of an enemy to stand.
    /// </summary>
    [Theory(Skip = TestBuild.SiegeMeetsConquest)]
    [InlineData("ridge")]
    [InlineData("switchback")]
    public void TheScriptGivesEveryKindOfItsOrders(string map)
    {
        var replay = ReplayRuns.Record("--map", map, "--ticks", "8000", "--orders", "33", "--script", "siege");

        Assert.All(replay.Commands, entry => Assert.True(IsOfThisScript(entry.Command), $"The script gave a command of type {entry.Command.Type}."));
        var match = ReplayRuns.Start(replay);
        var due = replay.Commands.ToLookup(entry => entry.Tick, entry => entry.Command);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var tick = 0; tick < replay.TickCount; tick++)
        {
            seen.UnionWith(KindsOfOrders(match, [.. due[tick]]));
            match.Simulation.Tick();
            seen.UnionWith(KindsOfPlacements(match));
        }

        Assert.Equal(EveryKindOfOrder.Order(StringComparer.Ordinal), seen.Order(StringComparer.Ordinal));
        Assert.True(replay.Run(ReplayRuns.GoldenData).Reproduced);
    }

    /// <summary>
    /// What this recorded match is for: it must reach the rules of structures in combat that a
    /// change could break. The match has research orders beside the script's own, so that the
    /// tiers of the two players drift apart and the fortification rule applies to some hits and
    /// not to others. Every hit on a structure in it is worked out again here, apart from the
    /// simulation's own sum.
    /// </summary>
    [Fact(Skip = TestBuild.SiegeMeetsConquest)]
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
            seen.UnionWith(KindsOfPlacements(match));
            seen.UnionWith(WhatTheTickDid(match, before));
        }

        string[] expected =
        [
            .. EveryKindOfOrder,
            "a Tower fires at a unit",
            "a Tower fires at a Tower",
            "a Tower's shot hits a unit",
            "a Tower's shot hits a Tower",
            "a unit killed by a Tower",
            "a melee hit on a structure",
            "a shot hits a structure",
            "a shell hits a structure",
            "a blast that reaches a structure and a unit",
            "a hit on a structure with the multiplier of its attacker's line",
            "a hit on a structure at full damage",
            "a hit on a structure halved by the fortification rule",
            "a hit on a wall",
            "a hit on a Capitol",
            "a structure destroyed",
            "a Tower destroyed",
            "a unit that picked a Tower by itself",
            "a unit on an attack-move that picked a structure that does not attack",
        ];
        var missing = string.Join("; ", expected.Except(seen).Order(StringComparer.Ordinal));
        var unexpected = string.Join("; ", seen.Except(expected).Order(StringComparer.Ordinal));
        Assert.True(missing.Length == 0 && unexpected.Length == 0, $"Not reached: {missing}. Reached and not on the list: {unexpected}.");
    }

    private static bool IsOfThisScript(Command command) =>
        command.Type is CommandType.DebugPlace or CommandType.DebugSpawn or CommandType.Attack or CommandType.AttackMove or CommandType.Stop;

    private static bool IsTower(Match match, int structure) => match.Data.StructureOf(structure)?.Attack is not null;

    private static bool IsWall(Match match, int structure) => match.Data.StructureOf(structure)?.Wall is not null;

    /// <summary>
    /// What the commands of this script that are due in the next tick are, in the words of
    /// <see cref="EveryKindOfOrder"/>, but for the placements. The match is as it is before that
    /// tick, which is what the script saw when it gave them.
    /// </summary>
    private static HashSet<string> KindsOfOrders(Match match, IReadOnlyList<Command> commands)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var command in commands)
        {
            var fighters = command.Entities.ToArray().Any(entity =>
                match.Combatants.Contains(entity) && match.Bodies.TryGet(entity, out var body) && body.Owner == command.Player);
            switch (command.Type)
            {
                case CommandType.DebugSpawn when match.Grid.IsPassable(new FixedVector2(command.TargetX, command.TargetY)):
                    seen.Add("a fighting unit made");
                    break;
                case CommandType.Attack when fighters
                    && match.Structures.TryGet(command.TargetEntity, out var structure)
                    && match.Bodies.TryGet(command.TargetEntity, out var target):
                    seen.Add(
                        target.Owner == command.Player ? "an attack on a structure of the player's own"
                        : IsWall(match, structure.Type) ? "an attack on a wall of an enemy"
                        : "an attack on a structure of an enemy");
                    break;
                case CommandType.AttackMove when fighters && IsOnAStructureOfAnEnemy(match, command):
                    seen.Add("an attack-move onto a structure of an enemy");
                    break;
                case CommandType.Stop when fighters:
                    seen.Add("a stop");
                    break;
                default:
                    break;
            }
        }

        return seen;
    }

    private static bool IsOnAStructureOfAnEnemy(Match match, Command command)
    {
        var point = new FixedVector2(command.TargetX, command.TargetY);
        for (var i = 0; i < match.Structures.Count; i++)
        {
            if (match.Structures.ValueAt(i).Tiles.Covers(point)
                && match.Bodies.Get(match.Structures.EntityAt(i)).Owner != command.Player)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>What the placements of the tick that has just run came to, from its events.</summary>
    private static HashSet<string> KindsOfPlacements(Match match)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var walls = 0;
        foreach (var reported in match.Simulation.Events)
        {
            if (reported.Kind == SimEventKind.OrderRefused && reported.Amount == (long)CommandType.DebugPlace)
            {
                seen.Add("a placement refused");
            }

            if (reported.Kind != SimEventKind.StructurePlaced)
            {
                continue;
            }

            var structure = (int)reported.Amount;
            walls += IsWall(match, structure) ? 1 : 0;
            seen.Add(
                IsTower(match, structure) ? "a Tower placed"
                : IsWall(match, structure) ? "a wall placed"
                : "a structure that does not attack placed");
        }

        if (walls > 1)
        {
            seen.Add("a row of walls placed");
        }

        return seen;
    }

    /// <summary>
    /// What the tick that has just run did to structures and with Towers, from its events and from
    /// the state after it, in the words of the list of
    /// <see cref="ItsGoldenReplayReachesTheRulesItGuards"/>.
    /// </summary>
    private static HashSet<string> WhatTheTickDid(Match match, Before before)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var events = match.Simulation.Events;
        foreach (var reported in events)
        {
            switch (reported.Kind)
            {
                case SimEventKind.ShotFired when before.Structures.ContainsKey(reported.Other):
                    seen.Add(before.Structures.ContainsKey(match.Shots.Get(reported.Entity).Target) ? "a Tower fires at a Tower" : "a Tower fires at a unit");
                    break;

                case SimEventKind.ShotHit:
                    var shot = before.Shots[reported.Other];
                    var byATower = shot.Line == CombatRules.NoLine;
                    if (before.Structures.TryGetValue(reported.Entity, out var hit))
                    {
                        seen.Add(byATower ? "a Tower's shot hits a Tower" : shot.Blast > Fixed.Zero ? "a shell hits a structure" : "a shot hits a structure");
                        seen.UnionWith(HitOnAStructure(match, before, hit, shot.Damage, shot.Line, shot.Firepower, reported.Amount));
                    }
                    else if (byATower)
                    {
                        seen.Add("a Tower's shot hits a unit");
                        if (events.Any(other => other.Kind == SimEventKind.Death && other.Entity == reported.Entity))
                        {
                            seen.Add("a unit killed by a Tower");
                        }
                    }

                    break;

                case SimEventKind.MeleeHit when before.Structures.TryGetValue(reported.Entity, out var struck):
                    var attacker = before.Units.TryGetValue(reported.Other, out var known)
                        ? known
                        : match.Combat.ProfileOf(match.Bodies.Get(reported.Other).Owner, match.Combatants.Get(reported.Other).Chassis);
                    seen.Add("a melee hit on a structure");
                    seen.UnionWith(HitOnAStructure(match, before, struck, attacker.Damage, attacker.Line, attacker.Firepower, reported.Amount));
                    break;

                case SimEventKind.Blast:
                    var reached = events.Where(other => other.Kind == SimEventKind.ShotHit && other.Other == reported.Entity).ToList();
                    if (reached.Any(other => before.Structures.ContainsKey(other.Entity)) && reached.Any(other => !before.Structures.ContainsKey(other.Entity)))
                    {
                        seen.Add("a blast that reaches a structure and a unit");
                    }

                    break;

                case SimEventKind.Death when before.Structures.TryGetValue(reported.Entity, out var fallen):
                    seen.Add("a structure destroyed");
                    if (IsTower(match, fallen.Type))
                    {
                        seen.Add("a Tower destroyed");
                    }

                    break;

                default:
                    break;
            }
        }

        for (var i = 0; i < match.Combatants.Count; i++)
        {
            var unit = match.Combatants.ValueAt(i);
            if (unit.Order == UnitOrder.Attack || !match.Structures.TryGet(unit.Target, out var picked))
            {
                continue;
            }

            Assert.False(IsWall(match, picked.Type), "A unit picked a wall by itself.");
            if (IsTower(match, picked.Type))
            {
                seen.Add("a unit that picked a Tower by itself");
            }
            else
            {
                Assert.Equal(UnitOrder.AttackMove, unit.Order);
                seen.Add("a unit on an attack-move that picked a structure that does not attack");
            }
        }

        return seen;
    }

    /// <summary>
    /// Works a hit on a structure out again from the design's words (2.4 and 2.5) and holds the
    /// simulation's damage against it: the attacker's damage, times its line's multiplier against
    /// structures, halved when its effective Firepower is below the Protection tier the owner had
    /// when the hit landed, rounded once.
    /// </summary>
    private static HashSet<string> HitOnAStructure(
        Match match, Before before, (int Type, int Owner) structure, Fraction damage, int line, int firepower, long amount)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var multiplier = line == CombatRules.NoLine
            ? Fraction.One
            : match.Data.Lines.Single(candidate => candidate.Index == line).StructureCounter?.ToFraction() ?? Fraction.One;
        var halved = firepower < before.Protection[structure.Owner];
        var expected = damage * multiplier;
        if (halved)
        {
            expected *= Fraction.Parse("0.5");
        }

        Assert.Equal(expected.RoundHalfUp(1000), amount);
        seen.Add(halved ? "a hit on a structure halved by the fortification rule" : "a hit on a structure at full damage");
        if (multiplier != Fraction.One)
        {
            seen.Add("a hit on a structure with the multiplier of its attacker's line");
        }

        if (IsWall(match, structure.Type))
        {
            seen.Add("a hit on a wall");
        }

        if (structure.Type == match.Data.StartStructure.Index)
        {
            seen.Add("a hit on a Capitol");
        }

        return seen;
    }

    /// <summary>What a test needs of the match as it was before a tick, to say what the tick did.</summary>
    private sealed class Before
    {
        /// <summary>Every structure on the map, with its kind and its owner.</summary>
        public Dictionary<EntityId, (int Type, int Owner)> Structures { get; } = [];

        /// <summary>Every shot in the air, as it was fired.</summary>
        public Dictionary<EntityId, Shot> Shots { get; } = [];

        /// <summary>What every fighting unit attacks with.</summary>
        public Dictionary<EntityId, AttackProfile> Units { get; } = [];

        /// <summary>The Protection tier of every player.</summary>
        public List<int> Protection { get; } = [];

        public void Take(Match match)
        {
            Structures.Clear();
            for (var i = 0; i < match.Structures.Count; i++)
            {
                var entity = match.Structures.EntityAt(i);
                Structures[entity] = (match.Structures.ValueAt(i).Type, match.Bodies.Get(entity).Owner);
            }

            Shots.Clear();
            for (var i = 0; i < match.Shots.Count; i++)
            {
                Shots[match.Shots.EntityAt(i)] = match.Shots.ValueAt(i);
            }

            Units.Clear();
            for (var i = 0; i < match.Combatants.Count; i++)
            {
                var entity = match.Combatants.EntityAt(i);
                Units[entity] = match.Combat.ProfileOf(match.Bodies.Get(entity).Owner, match.Combatants.ValueAt(i).Chassis);
            }

            Protection.Clear();
            for (var player = 0; player < match.Players.Count; player++)
            {
                Protection.Add(match.Research.State.Tier(player, match.Data.TrackRoles.Protection));
            }
        }
    }
}
