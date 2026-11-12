using Sim.Combat;
using Sim.Core;
using Sim.Economy;
using Sim.Numerics;
using Sim.Setup;
using Sim.Tech;

namespace Tools.Tests.ReplayScripts;

/// <summary>
/// The script <c>combat-perks</c> of <c>replay record</c> and the golden replay recorded for it:
/// what the script must give, and what that replay must reach.
/// </summary>
public class CombatPerkScriptTests
{
    private const string GoldenReplay = "switchback-combat-perks.replay";

    private static readonly string[] EveryKindOfOrder =
    [
        "a Generator placed",
        "workers sent to a Generator",
        "a worker sent to a deposit",
        "a research started",
        "a fighting unit made",
        "a Tower placed",
        "a wall placed",
        "an attack-move into the middle",
        "an attack on a unit the player sees",
        "an attack on a structure the player sees",
        "units sent back to their side",
        "a stop",
    ];

    /// <summary>
    /// What the golden replay must reach of the accents of combat (design 2.3). Every hit on a unit
    /// in it is worked out again here from the blueprints of the two sides, apart from the
    /// simulation's own sum, so each line stands for hits whose damage was checked.
    /// </summary>
    /// <remarks>
    /// One thing the match cannot reach: a splash that plating takes from as well. Among chassis
    /// of one epoch a unit that splashes has the highest Firepower a plated unit's Protection
    /// can be, so it is never below it. <c>PlatingTests</c> has that case, with a Legionary and
    /// a Knight.
    /// </remarks>
    private static readonly string[] EveryRuleReached =
    [
        // Overgunned I.
        "a melee unit with the rate accent attacks",
        "a surplus of waiting carried into the next wait",
        "a unit that is ready with nothing to hit and keeps no surplus",
        "a shot fired from beyond the range of its line",

        // Overgunned II.
        "a melee hit that splashes",
        "a shot that splashes when it lands",
        "a splash that reaches another unit",
        "a splash that reaches a structure",
        "a splash that reaches nobody",
        "a splash lost with its shot",
        "a blast wider than that of its line",
        "a blast that reaches an enemy beyond the radius of its line",

        // Hardened I.
        "a blast on a Hardened unit, a quarter less",
        "a splash on a Hardened unit, a quarter less",
        "a hit on a Hardened unit itself, whole",

        // Hardened II.
        "a melee hit that plating takes 30% off",
        "a shot that plating takes 30% off",
        "a blast that the area reduction and plating both take from",
        "a splash on a plated unit by an attacker level with its Protection, a quarter less and no more",
        "a hit on a plated unit by an attacker level with its Protection, whole",
        "a Tower's shot that plating takes 30% off",
        "a Tower's shot on a plated unit, whole, its owner's Firepower level with the unit's Protection",

        // Rapid I.
        "a unit that picked a target beyond the sight of its line",

        // The fog of war, which the replay of the script fog no longer reaches (see FogScriptTests).
        "a unit whose attack order ends where it stands because its target died",
    ];

    /// <summary>
    /// The script gives every kind of order it is meant to: here in one match on each map, long
    /// enough for the researches of every part.
    /// </summary>
    [Theory]
    [InlineData("ridge")]
    [InlineData("switchback")]
    public void TheScriptGivesEveryKindOfItsOrders(string map)
    {
        var replay = ReplayRuns.Record("--map", map, "--ticks", "3000", "--orders", "33", "--script", "combat-perks");

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
    /// What this recorded match is for: it must reach the rule of every accent of combat that a
    /// change could break. It has three players: a gunner and an armored player with bases, who
    /// research, and a plain player without a base, whose units and Tower stay at Firepower 1.
    /// </summary>
    [Fact]
    public void ItsGoldenReplayReachesTheRulesItGuards()
    {
        var replay = ReplayRuns.ReadGolden(GoldenReplay);
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
            seen.UnionWith(watch.WhatTheTickDid());
        }

        string[] expected = [.. EveryKindOfOrder, .. EveryRuleReached];
        var missing = string.Join("; ", expected.Except(seen).Order(StringComparer.Ordinal));
        var unexpected = string.Join("; ", seen.Except(expected).Order(StringComparer.Ordinal));
        Assert.True(missing.Length == 0 && unexpected.Length == 0, $"Not reached: {missing}. Reached and not on the list: {unexpected}.");

        // The researches the parts are for are done by the end, and the plain player has none.
        var roles = match.Data.TrackRoles;
        Assert.Equal((3, 2), (match.Research.State.Tier(0, roles.Firepower), match.Research.State.Tier(0, roles.Mobility)));
        Assert.Equal((3, 2), (match.Research.State.Tier(1, roles.Protection), match.Research.State.Tier(1, roles.Mobility)));
        Assert.All(Enumerable.Range(0, match.Data.Tracks.Count), track => Assert.Equal(1, match.Research.State.Tier(2, track)));
        Assert.True(watch.HitsWorkedOut > 500, $"Only {watch.HitsWorkedOut} hits on units were worked out.");
    }

    private static bool IsOfThisScript(Command command) =>
        command.Type is CommandType.DebugPlace or CommandType.DebugSpawn or CommandType.Gather or CommandType.StartResearch
            or CommandType.Attack or CommandType.AttackMove or CommandType.Move or CommandType.Stop;

    /// <summary>
    /// What the commands that are due in the next tick are, in the words of
    /// <see cref="EveryKindOfOrder"/>. The match is as it is before that tick, which is what the
    /// script saw when it gave them.
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
                case CommandType.DebugPlace when match.Data.StructureOf(command.ContentId) is { } structure:
                    seen.Add(structure.IsEnergyWorkSite ? "a Generator placed" : structure.Attack is not null ? "a Tower placed" : "a wall placed");
                    Assert.True(structure.IsEnergyWorkSite || structure.Attack is not null || structure.Wall is not null);
                    break;
                case CommandType.DebugSpawn:
                    seen.Add("a fighting unit made");
                    break;
                case CommandType.Gather when match.WorkSites.TryGet(command.TargetEntity, out var site):
                    seen.Add(site.Kind == WorkSiteKind.Generator ? "workers sent to a Generator" : "a worker sent to a deposit");
                    break;
                case CommandType.StartResearch:
                    Assert.Equal(Sim.Research.ResearchStartCheck.Allowed, match.Research.CheckStart(command.Player, command.ContentId));
                    seen.Add("a research started");
                    break;
                case CommandType.AttackMove when fighters:
                    seen.Add("an attack-move into the middle");
                    break;
                case CommandType.Attack when fighters && match.Bodies.TryGet(command.TargetEntity, out var target):
                    Assert.NotEqual(command.Player, target.Owner);
                    Assert.True(match.Vision.Sees(command.Player, command.TargetEntity));
                    seen.Add(match.Structures.Contains(command.TargetEntity)
                        ? "an attack on a structure the player sees"
                        : "an attack on a unit the player sees");
                    break;
                case CommandType.Move when fighters:
                    seen.Add("units sent back to their side");
                    break;
                case CommandType.Stop when fighters:
                    seen.Add("a stop");
                    break;
            }
        }

        return seen;
    }

    /// <summary>What a unit was before a tick.</summary>
    private readonly record struct UnitBefore(int Owner, int Chassis, UnitOrder Order, EntityId Target, Fraction Wait, FixedVector2 Position);

    /// <summary>
    /// Takes what the match is before a tick, and says after it what the tick did, in the words
    /// of <see cref="EveryRuleReached"/>. It works out the damage of every hit on a unit itself.
    /// </summary>
    private sealed class Watch(Match match)
    {
        private readonly Dictionary<EntityId, UnitBefore> _units = [];
        private readonly Dictionary<EntityId, Shot> _shots = [];
        private readonly Dictionary<EntityId, int> _owners = [];
        private readonly HashSet<EntityId> _structures = [];
        private readonly HashSet<EntityId> _towerShots = [];
        private readonly HashSet<EntityId> _ordered = [];
        private readonly HashSet<EntityId> _attacked = [];
        private readonly PlayerTech[] _tech = new PlayerTech[match.Players.Count];
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

        /// <summary>How many hits on units have been worked out again so far.</summary>
        public int HitsWorkedOut { get; private set; }

        public void BeforeTheTick(IReadOnlyList<Command> commands)
        {
            _units.Clear();
            _shots.Clear();
            _owners.Clear();
            _structures.Clear();
            _ordered.Clear();
            for (var i = 0; i < match.Combatants.Count; i++)
            {
                var entity = match.Combatants.EntityAt(i);
                if (match.World.Entities.IsAlive(entity) && match.Bodies.TryGet(entity, out var body))
                {
                    var unit = match.Combatants.ValueAt(i);
                    _units[entity] = new UnitBefore(body.Owner, unit.Chassis, unit.Order, unit.Target, unit.Wait, body.Position);
                }
            }

            for (var i = 0; i < match.Shots.Count; i++)
            {
                _shots[match.Shots.EntityAt(i)] = match.Shots.ValueAt(i);
            }

            for (var i = 0; i < match.Bodies.Count; i++)
            {
                _owners[match.Bodies.EntityAt(i)] = match.Bodies.ValueAt(i).Owner;
            }

            for (var i = 0; i < match.Structures.Count; i++)
            {
                _structures.Add(match.Structures.EntityAt(i));
            }

            for (var player = 0; player < _tech.Length; player++)
            {
                _tech[player] = match.Research.Tech(player);
            }

            foreach (var command in commands)
            {
                _ordered.UnionWith(command.Entities.ToArray());
            }
        }

        public HashSet<string> WhatTheTickDid()
        {
            _seen.Clear();
            _attacked.Clear();
            var events = match.Simulation.Events;
            for (var i = 0; i < events.Count; i++)
            {
                var reported = events[i];
                switch (reported.Kind)
                {
                    case SimEventKind.MeleeHit:
                        MeleeHit(in reported);
                        break;
                    case SimEventKind.ShotFired:
                        ShotFired(in reported);
                        break;
                    case SimEventKind.ShotHit:
                        ShotHit(in reported);
                        break;
                    case SimEventKind.Blast:
                        Blast(in reported);
                        break;
                    case SimEventKind.ShotLost:
                        if (_shots[reported.Entity].SplashRadius > Fixed.Zero)
                        {
                            _seen.Add("a splash lost with its shot");
                        }

                        Assert.DoesNotContain(events, other => other.Kind == SimEventKind.Splash && other.Entity == reported.Entity);
                        break;
                    case SimEventKind.Splash:
                        Splash(in reported, [.. events.Skip(i + 1).TakeWhile(other => other.Kind == SimEventKind.SplashHit)]);
                        break;
                    case SimEventKind.SplashHit:
                        // Each belongs to the splash before it, and is looked at there.
                        Assert.Contains(events.Take(i), other => other.Kind == SimEventKind.Splash && other.Entity == reported.Other);
                        break;
                }
            }

            Units();
            return _seen;
        }

        /// <summary>The blueprint a player had of a chassis when the tick began, which is the one its hits of that tick were made and taken with.</summary>
        private ChassisBlueprint Blueprint(int owner, int chassis) => _tech[owner].Blueprints[chassis];

        /// <summary>The unit an entity was in this tick, also when it was made in it.</summary>
        private bool TryFindUnit(EntityId entity, out int owner, out ChassisBlueprint blueprint)
        {
            if (_units.TryGetValue(entity, out var before))
            {
                (owner, blueprint) = (before.Owner, Blueprint(before.Owner, before.Chassis));
                return true;
            }

            if (match.Combatants.TryGet(entity, out var unit) && match.Bodies.TryGet(entity, out var body))
            {
                (owner, blueprint) = (body.Owner, Blueprint(body.Owner, unit.Chassis));
                return true;
            }

            (owner, blueprint) = (-1, null!);
            return false;
        }

        /// <summary>
        /// The damage of a hit on a unit by design 2.3 and 2.4, from the target's blueprint: the
        /// attacker's side, times the counter multiplier, times the area reduction for area
        /// damage, times plating for an attacker below the target's Protection, rounded once.
        /// </summary>
        private long Expected(Fraction side, int line, int firepower, ChassisBlueprint target, bool area, out bool hardened, out bool plated)
        {
            var damage = side * match.Combat.Multiplier(line, target.Line.Index);
            hardened = area && target.Perks.AreaDamageReduction is not null;
            if (hardened)
            {
                damage *= target.Perks.AreaDamageReduction!.Multiplier.ToFraction();
            }

            plated = target.Perks.Plating is not null && firepower < target.EffectiveTiers[match.Data.TrackRoles.Protection];
            if (plated)
            {
                damage *= target.Perks.Plating!.Multiplier.ToFraction();
            }

            HitsWorkedOut++;
            return damage.RoundHalfUp(1000);
        }

        private void MeleeHit(in SimEvent hit)
        {
            Assert.True(TryFindUnit(hit.Other, out var owner, out var attacker));
            _attacked.Add(hit.Other);
            var firepower = attacker.EffectiveTiers[match.Data.TrackRoles.Firepower];
            if (attacker.Perks.AttackRateBonus is not null)
            {
                _seen.Add("a melee unit with the rate accent attacks");
                if (match.Combatants.TryGet(hit.Other, out var after) && after.Wait != after.Wait.RoundHalfUp(1))
                {
                    // The wait after an attack is the interval less what the waiting before it had over.
                    Assert.True(after.Wait < attacker.Attack.IntervalTicks);
                    _seen.Add("a surplus of waiting carried into the next wait");
                }
            }

            if (!TryFindUnit(hit.Entity, out var targetOwner, out var target))
            {
                return;
            }

            Assert.NotEqual(owner, targetOwner);
            Assert.Equal(Expected(attacker.DamagePerHit, attacker.Line.Index, firepower, target, area: false, out _, out var plated), hit.Amount);
            if (plated)
            {
                _seen.Add("a melee hit that plating takes 30% off");
            }
            else
            {
                Whole(target);
            }
        }

        /// <summary>A hit that lands whole on a unit that has an accent of Protection.</summary>
        private void Whole(ChassisBlueprint target)
        {
            if (target.Perks.Plating is not null)
            {
                _seen.Add("a hit on a plated unit by an attacker level with its Protection, whole");
            }
            else if (target.Perks.AreaDamageReduction is not null)
            {
                _seen.Add("a hit on a Hardened unit itself, whole");
            }
        }

        private void ShotFired(in SimEvent fired)
        {
            var shot = match.Shots.Get(fired.Entity);
            if (_structures.Contains(fired.Other) || match.Structures.Contains(fired.Other))
            {
                // A Tower's Firepower is its owner's tier.
                _towerShots.Add(fired.Entity);
                Assert.Equal(_tech[fired.Player].Tiers[match.Data.TrackRoles.Firepower], shot.Firepower);
                Assert.Equal(Fixed.Zero, shot.SplashRadius);
                return;
            }

            Assert.True(TryFindUnit(fired.Other, out _, out var attacker));
            _attacked.Add(fired.Other);
            var splash = attacker.Perks.Splash;
            Assert.Equal(splash is null ? Fraction.Zero : splash.Share.ToFraction(), shot.SplashShare);
            Assert.Equal(splash is null ? Fixed.Zero : splash.Radius.ToFraction().ToFixed(), shot.SplashRadius);
            Assert.Equal(attacker.BlastRadius ?? Fixed.Zero, shot.Blast);
            Assert.Equal(attacker.EffectiveTiers[match.Data.TrackRoles.Firepower], shot.Firepower);
            if (attacker.Perks.RangeBonus is null || _structures.Contains(shot.Target) || match.Structures.Contains(shot.Target)
                || !match.Bodies.TryGet(shot.Target, out var target))
            {
                return;
            }

            // The range of the line is the chassis's own, before the accent.
            var distance = FixedVector2.DistanceSquaredRaw(fired.Position, target.Position);
            var ofTheLine = attacker.Attack.Range!.Value.ToFraction().ToFixed();
            Assert.True(distance <= FixedVector2.SquareRaw(attacker.Range!.Value));
            Assert.True(attacker.Range.Value > ofTheLine);
            if (distance > FixedVector2.SquareRaw(ofTheLine))
            {
                _seen.Add("a shot fired from beyond the range of its line");
            }
        }

        private void ShotHit(in SimEvent hit)
        {
            var shot = _shots[hit.Other];
            if (shot.Blast > Fixed.Zero || !TryFindUnit(hit.Entity, out _, out var target))
            {
                // What a blast reaches is looked at with its blast.
                return;
            }

            var byATower = _towerShots.Contains(hit.Other);
            Assert.Equal(byATower, shot.Line == CombatRules.NoLine);
            Assert.Equal(Expected(shot.Damage, shot.Line, shot.Firepower, target, area: false, out _, out var plated), hit.Amount);
            if (byATower && target.Perks.Plating is not null)
            {
                _seen.Add(plated
                    ? "a Tower's shot that plating takes 30% off"
                    : "a Tower's shot on a plated unit, whole, its owner's Firepower level with the unit's Protection");
            }
            else if (plated)
            {
                _seen.Add("a shot that plating takes 30% off");
            }
            else
            {
                Whole(target);
            }
        }

        private void Blast(in SimEvent blast)
        {
            var shot = _shots[blast.Entity];
            Assert.Equal(shot.Blast.Raw, blast.Amount);
            var ofTheLine = match.Data.Lines.Single(line => line.Index == shot.Line).Attack.BlastRadius!.Value.ToFraction().ToFixed();
            if (shot.Blast > ofTheLine)
            {
                _seen.Add("a blast wider than that of its line");
            }

            foreach (var hit in match.Simulation.Events)
            {
                if (hit.Kind != SimEventKind.ShotHit || hit.Other != blast.Entity)
                {
                    continue;
                }

                var distance = FixedVector2.DistanceSquaredRaw(blast.Position, hit.Position);
                Assert.True(distance <= FixedVector2.SquareRaw(shot.Blast));
                if (!TryFindUnit(hit.Entity, out _, out var target))
                {
                    continue;
                }

                if (distance > FixedVector2.SquareRaw(ofTheLine))
                {
                    _seen.Add("a blast that reaches an enemy beyond the radius of its line");
                }

                Assert.Equal(Expected(shot.Damage, shot.Line, shot.Firepower, target, area: true, out var hardened, out var plated), hit.Amount);
                if (hardened)
                {
                    _seen.Add(plated ? "a blast that the area reduction and plating both take from" : "a blast on a Hardened unit, a quarter less");
                }
            }
        }

        private void Splash(in SimEvent splash, SimEvent[] hits)
        {
            Fraction side;
            int line;
            int firepower;
            if (_shots.TryGetValue(splash.Entity, out var shot))
            {
                (side, line, firepower) = (shot.Damage * shot.SplashShare, shot.Line, shot.Firepower);
                Assert.Equal(shot.SplashRadius.Raw, splash.Amount);
                _seen.Add("a shot that splashes when it lands");
            }
            else
            {
                Assert.True(TryFindUnit(splash.Entity, out _, out var attacker));
                var perk = attacker.Perks.Splash;
                Assert.NotNull(perk);
                (side, line, firepower) = (
                    attacker.DamagePerHit * perk.Share.ToFraction(), attacker.Line.Index, attacker.EffectiveTiers[match.Data.TrackRoles.Firepower]);
                Assert.Equal(perk.Radius.ToFraction().ToFixed().Raw, splash.Amount);
                _seen.Add("a melee hit that splashes");
            }

            if (hits.Length == 0)
            {
                _seen.Add("a splash that reaches nobody");
            }

            foreach (var hit in hits)
            {
                Assert.Equal(splash.Entity, hit.Other);
                Assert.NotEqual(splash.Other, hit.Entity);
                Assert.NotEqual(splash.Player, _owners.TryGetValue(hit.Entity, out var owner) ? owner : match.Bodies.Get(hit.Entity).Owner);
                Assert.True(FixedVector2.DistanceSquaredRaw(splash.Position, hit.Position) <= FixedVector2.SquareRaw(Fixed.FromRaw((int)splash.Amount)));
                if (_structures.Contains(hit.Entity) || match.Structures.Contains(hit.Entity))
                {
                    _seen.Add("a splash that reaches a structure");
                    continue;
                }

                if (!TryFindUnit(hit.Entity, out _, out var target))
                {
                    // A worker: no line and no accent.
                    Assert.Equal(side.RoundHalfUp(1000), hit.Amount);
                    continue;
                }

                _seen.Add("a splash that reaches another unit");
                Assert.Equal(Expected(side, line, firepower, target, area: true, out var hardened, out var plated), hit.Amount);
                if (hardened)
                {
                    _seen.Add(
                        plated ? "a splash that the area reduction and plating both take from"
                        : target.Perks.Plating is not null ? "a splash on a plated unit by an attacker level with its Protection, a quarter less and no more"
                        : "a splash on a Hardened unit, a quarter less");
                }
            }
        }

        /// <summary>What the tick did to each unit that was there before it and still is.</summary>
        private void Units()
        {
            foreach (var (entity, before) in _units)
            {
                if (!match.Combatants.TryGet(entity, out var unit) || !match.Bodies.TryGet(entity, out var body))
                {
                    continue;
                }

                var blueprint = Blueprint(before.Owner, before.Chassis);
                if (blueprint.Perks.AttackRateBonus is { } rate && !_attacked.Contains(entity)
                    && before.Wait > Fraction.Zero && before.Wait < rate.Multiplier.ToFraction())
                {
                    // Its wait ran out in this tick with something over, and it did not attack.
                    Assert.Equal(Fraction.Zero, unit.Wait);
                    _seen.Add("a unit that is ready with nothing to hit and keeps no surplus");
                }

                if (before.Order == UnitOrder.Attack && unit.Order != UnitOrder.Attack && !_ordered.Contains(entity)
                    && !(match.World.Entities.IsAlive(before.Target) && match.Bodies.Contains(before.Target))
                    && unit.Order == UnitOrder.Idle && body.Position == unit.Anchor)
                {
                    _seen.Add("a unit whose attack order ends where it stands because its target died");
                }

                // A target it picked itself, further off than its line sees: its owner sees that far by the accent.
                var now = match.Research.Tech(before.Owner).Blueprints[before.Chassis];
                if (now.Perks.SightBonus is { } sight && unit.Order is UnitOrder.Idle or UnitOrder.AttackMove
                    && unit.Target != EntityId.None && match.Movers.Contains(unit.Target) && match.Bodies.TryGet(unit.Target, out var target))
                {
                    var ofTheLine = Fixed.FromInt(now.Sight - sight.Tiles);
                    var reach = now.Range is { } range && range > ofTheLine ? range : ofTheLine;
                    if (FixedVector2.DistanceSquaredRaw(body.Position, target.Position) > FixedVector2.SquareRaw(reach)
                        && unit.Target != before.Target)
                    {
                        Assert.True(match.Vision.Sees(before.Owner, in target));
                        _seen.Add("a unit that picked a target beyond the sight of its line");
                    }
                }
            }
        }
    }
}
