using Sim;
using Sim.Combat;
using Sim.Core;
using Sim.Economy;
using Sim.Numerics;
using Sim.Perks;
using Sim.Setup;
using Sim.Tech;

namespace Game;

/// <summary>
/// What one unit is, as the inspection panel shows it (design 3.2): whose it is, its chassis with
/// everything that follows from its owner's tiers, or that it is a worker, and its hit points.
/// </summary>
/// <remarks>
/// Hit points and speed are read from the unit itself, the rest from its owner's blueprint of the
/// chassis, which is where the simulation reads them when the unit attacks. So the panel shows
/// what the unit fights with in this tick. What its perks stand at (design 2.3) is read from the
/// counts the simulation keeps for them, and worked out as the simulation works it out.
/// </remarks>
public readonly record struct UnitFacts
{
    /// <summary>The <see cref="Kind"/> of a worker.</summary>
    public const int WorkerKind = -1;

    /// <summary>The <see cref="Kind"/> of a unit that is neither of a chassis nor a worker.</summary>
    public const int OtherKind = -2;

    public required EntityId Entity { get; init; }

    public required int Owner { get; init; }

    /// <summary>The owner's blueprint of the unit's chassis, or null for a unit that has no chassis, as a worker.</summary>
    public ChassisBlueprint? Blueprint { get; init; }

    public bool IsWorker { get; init; }

    /// <summary>What a worker is doing. Meaningful while <see cref="IsWorker"/>.</summary>
    public WorkerState Work { get; init; }

    /// <summary>True for a worker whose place of work is a Generator and not a deposit.</summary>
    public bool AtGenerator { get; init; }

    /// <summary>The roster index of the structure the unit is on its way to build, or null when it has no build order.</summary>
    public int? Builds { get; init; }

    /// <summary>Hit points now, in thousandths. Zero with <see cref="Maximum"/> zero for a unit that cannot be damaged.</summary>
    public long HitPoints { get; init; }

    /// <summary>Hit points when undamaged, in thousandths.</summary>
    public long Maximum { get; init; }

    /// <summary>How fast the unit walks, in tiles per tick.</summary>
    public Fixed SpeedPerTick { get; init; }

    /// <summary>What a unit of a chassis takes a hit with, as the simulation has it, or null for a unit without a chassis.</summary>
    public ArmorProfile? Armor { get; init; }

    /// <summary>The distance and the bonus of Charge on this unit, as the simulation has them, or null for a unit that does not carry it.</summary>
    public ChargeProfile? Charge { get; init; }

    /// <summary>The tiles the unit has walked since its last attack, or since it was made.</summary>
    public Fixed Walked { get; init; }

    /// <summary>The ticks since the unit last took damage, or since it was made.</summary>
    public int TicksSinceDamage { get; init; }

    /// <summary>The Materials paid for the unit: nothing for one that no order made.</summary>
    public Fraction Paid { get; init; }

    public bool HasHealth => Maximum > 0;

    /// <summary>True when the unit's next attack carries the bonus of Charge: it has walked the distance since its last attack.</summary>
    public bool IsCharged => Charge is { } charge && Walked >= charge.Distance;

    /// <summary>Where the unit stands with Regeneration.</summary>
    public Regeneration Regeneration
    {
        get
        {
            if (Blueprint?.Perks.Regeneration is not { } perk)
            {
                return Regeneration.None;
            }

            if (HitPoints >= Maximum)
            {
                return Regeneration.Full;
            }

            return TicksSinceDamage >= perk.DelayTicks ? Regeneration.Regaining : Regeneration.Waiting;
        }
    }

    /// <summary>
    /// The ticks that run until the unit has next regained hit points, while it stays below its
    /// maximum and takes no damage: the rest of the wait after damage, then the rest of the
    /// second between two additions. Zero for a unit without Regeneration.
    /// </summary>
    public int TicksToRegain
    {
        get
        {
            if (Blueprint?.Perks.Regeneration is not { } perk)
            {
                return 0;
            }

            // The simulation adds in a tick in which the ticks since damage, less the wait, are a
            // whole number of seconds. The panel reads between two ticks, and the count it reads
            // is the one the next tick tests: so the tick that adds is one further than the rest.
            var sinceDelay = TicksSinceDamage - perk.DelayTicks;
            if (sinceDelay < 0)
            {
                return 1 - sinceDelay;
            }

            var into = sinceDelay % SimInfo.TicksPerSecond;
            return 1 + (into == 0 ? 0 : SimInfo.TicksPerSecond - into);
        }
    }

    /// <summary>The hit points Regeneration adds each second, in thousandths. Zero for a unit without it.</summary>
    public long RegainedASecond => Blueprint?.Perks.Regeneration is { } perk
        ? (perk.SharePerSecond.ToFraction() * Maximum).RoundHalfUp()
        : 0;

    /// <summary>The Materials Reserves would give back if the unit died now, rounded as the simulation rounds them. Zero for a unit without Reserves.</summary>
    public long Refund => Blueprint?.Perks.DeathRefund is { } perk
        ? Math.Max(0, (perk.Share.ToFraction() * Paid).RoundHalfUp())
        : 0;

    /// <summary>
    /// What units of one owner must share to be one row of a summary: the chassis, as its index
    /// in the data, or <see cref="WorkerKind"/> or <see cref="OtherKind"/>.
    /// </summary>
    public int Kind => Blueprint?.Chassis.Index ?? (IsWorker ? WorkerKind : OtherKind);

    /// <summary>
    /// Reads a unit. False for an entity that is gone, and for one that cannot walk: a structure
    /// stands on the map and has hit points too, and is described by the piece that draws it.
    /// </summary>
    public static bool TryRead(Match match, EntityId entity, out UnitFacts facts)
    {
        facts = default;
        if (!match.World.Entities.IsAlive(entity)
            || !match.Bodies.TryGet(entity, out var body)
            || !match.Movers.TryGet(entity, out var mover))
        {
            return false;
        }

        match.Health.TryGet(entity, out var health);
        if (match.Combatants.TryGet(entity, out var combatant))
        {
            match.PerkCounts.TryGet(entity, out var counts);
            facts = OfChassis(
                entity,
                body.Owner,
                match.Research.Tech(body.Owner).Blueprints[combatant.Chassis],
                match.Combat,
                health.HitPoints,
                health.Maximum,
                mover.Speed,
                counts,
                match.World.Tick);
            return true;
        }

        var isWorker = match.Workers.TryGet(entity, out var worker);
        facts = new UnitFacts
        {
            Entity = entity,
            Owner = body.Owner,
            IsWorker = isWorker,
            Work = worker.State,
            AtGenerator = isWorker && match.WorkSites.TryGet(worker.Site, out var site) && site.Kind == WorkSiteKind.Generator,
            Builds = match.BuildOrders.TryGet(entity, out var order) ? order.Type : null,
            HitPoints = health.HitPoints,
            Maximum = health.Maximum,
            SpeedPerTick = mover.Speed,
        };
        return true;
    }

    /// <summary>
    /// A unit of a chassis: what <see cref="TryRead"/> makes of one in a match. The armor and
    /// the terms of Charge are those the combat rules have for the owner's units of the chassis.
    /// </summary>
    /// <param name="entity">The unit.</param>
    /// <param name="owner">Its owner, as the combat rules number the players.</param>
    /// <param name="blueprint">The owner's blueprint of its chassis.</param>
    /// <param name="combat">The combat rules of the match.</param>
    /// <param name="hitPoints">Its hit points now, in thousandths.</param>
    /// <param name="maximum">Its hit points when undamaged, in thousandths.</param>
    /// <param name="speed">How fast it walks, in tiles per tick.</param>
    /// <param name="counts">What the simulation keeps count of for its perks.</param>
    /// <param name="tick">The world's tick, which the time since its last damage is counted to.</param>
    public static UnitFacts OfChassis(
        EntityId entity,
        int owner,
        ChassisBlueprint blueprint,
        CombatRules combat,
        long hitPoints,
        long maximum,
        Fixed speed,
        PerkCounts counts,
        int tick) => new()
        {
            Entity = entity,
            Owner = owner,
            Blueprint = blueprint,
            HitPoints = hitPoints,
            Maximum = maximum,
            SpeedPerTick = speed,
            Armor = combat.ArmorOf(owner, blueprint.Chassis.Index),
            Charge = combat.ProfileOf(owner, blueprint.Chassis.Index).Charge,
            Walked = counts.Walked,
            TicksSinceDamage = counts.TicksSinceDamage(tick),
            Paid = counts.Paid,
        };
}

/// <summary>Where a unit stands with Regeneration (design 2.3).</summary>
public enum Regeneration
{
    /// <summary>The unit does not carry it.</summary>
    None,

    /// <summary>It is at its maximum and has nothing to regain.</summary>
    Full,

    /// <summary>It is damaged, and the damage is too fresh for it to regain anything yet.</summary>
    Waiting,

    /// <summary>It is damaged and regains hit points each second.</summary>
    Regaining,
}

/// <summary>
/// Units of one owner and one kind, as one row of the inspection panel: how many there are, the
/// first of them for what they all share, and their hit points together.
/// </summary>
/// <param name="First">The unit with the lowest ID. Its owner, chassis and blueprint are those of all.</param>
/// <param name="Count">How many units.</param>
/// <param name="HitPoints">Their hit points now, summed, in thousandths.</param>
/// <param name="Maximum">Their hit points when undamaged, summed, in thousandths.</param>
/// <param name="Damaged">How many of them are below their maximum.</param>
public readonly record struct UnitGroup(UnitFacts First, int Count, long HitPoints, long Maximum, int Damaged)
{
    /// <summary>How many of them carry the bonus of Charge into their next attack.</summary>
    public int Charged { get; init; }

    /// <summary>How many of them are damaged and regain hit points each second.</summary>
    public int Regaining { get; init; }

    /// <summary>How many of them are damaged too freshly to regain hit points yet.</summary>
    public int Waiting { get; init; }

    /// <summary>The Materials Reserves would give back if all of them died now. Each unit's share is rounded by itself, as the simulation rounds it.</summary>
    public long Refund { get; init; }

    /// <summary>The Materials paid for all of them.</summary>
    public Fraction Paid { get; init; }

    /// <summary>One unit as a group of its own.</summary>
    public static UnitGroup OfOne(UnitFacts facts) =>
        new(facts, 1, facts.HitPoints, facts.Maximum, facts.HitPoints < facts.Maximum ? 1 : 0)
        {
            Charged = facts.IsCharged ? 1 : 0,
            Regaining = facts.Regeneration == Regeneration.Regaining ? 1 : 0,
            Waiting = facts.Regeneration == Regeneration.Waiting ? 1 : 0,
            Refund = facts.Refund,
            Paid = facts.Paid,
        };

    /// <summary>
    /// The units as groups: fighting units first, by line and then by epoch, workers after them,
    /// and within that by owner. Entities that are gone are left out.
    /// </summary>
    public static List<UnitGroup> Of(Match match, IEnumerable<EntityId> entities)
    {
        var units = new List<UnitFacts>();
        foreach (var entity in entities)
        {
            if (UnitFacts.TryRead(match, entity, out var facts))
            {
                units.Add(facts);
            }
        }

        return Of(units);
    }

    /// <summary>Units as groups, in the order of <see cref="Of(Match, IEnumerable{EntityId})"/>.</summary>
    public static List<UnitGroup> Of(IEnumerable<UnitFacts> units)
    {
        var groups = new List<UnitGroup>();
        foreach (var facts in units)
        {
            var one = OfOne(facts);
            var at = groups.FindIndex(group => group.First.Owner == facts.Owner && group.First.Kind == facts.Kind);
            if (at < 0)
            {
                groups.Add(one);
            }
            else
            {
                var group = groups[at];
                groups[at] = group with
                {
                    Count = group.Count + 1,
                    HitPoints = group.HitPoints + one.HitPoints,
                    Maximum = group.Maximum + one.Maximum,
                    Damaged = group.Damaged + one.Damaged,
                    Charged = group.Charged + one.Charged,
                    Regaining = group.Regaining + one.Regaining,
                    Waiting = group.Waiting + one.Waiting,
                    Refund = group.Refund + one.Refund,
                    Paid = group.Paid + one.Paid,
                };
            }
        }

        groups.Sort((a, b) => Place(a.First).CompareTo(Place(b.First)));
        return groups;
    }

    private static (int Rank, int Line, int Epoch, int Chassis, int Owner) Place(UnitFacts facts) =>
        facts.Blueprint is { } blueprint
            ? (0, blueprint.Line.Index, blueprint.Chassis.Epoch, blueprint.Chassis.Index, facts.Owner)
            : (facts.IsWorker ? 1 : 2, 0, 0, 0, facts.Owner);
}
