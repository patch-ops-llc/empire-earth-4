using Sim.Core;
using Sim.Economy;
using Sim.Numerics;
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
/// what the unit fights with in this tick.
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

    /// <summary>Hit points now, in thousandths. Zero with <see cref="Maximum"/> zero for a unit that cannot be damaged.</summary>
    public long HitPoints { get; init; }

    /// <summary>Hit points when undamaged, in thousandths.</summary>
    public long Maximum { get; init; }

    /// <summary>How fast the unit walks, in tiles per tick.</summary>
    public Fixed SpeedPerTick { get; init; }

    public bool HasHealth => Maximum > 0;

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

        ChassisBlueprint? blueprint = null;
        if (match.Combatants.TryGet(entity, out var combatant))
        {
            blueprint = match.Research.Tech(body.Owner).Blueprints[combatant.Chassis];
        }

        var isWorker = match.Workers.TryGet(entity, out var worker);
        match.Health.TryGet(entity, out var health);
        facts = new UnitFacts
        {
            Entity = entity,
            Owner = body.Owner,
            Blueprint = blueprint,
            IsWorker = isWorker,
            Work = worker.State,
            HitPoints = health.HitPoints,
            Maximum = health.Maximum,
            SpeedPerTick = mover.Speed,
        };
        return true;
    }
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
    /// <summary>
    /// The units as groups: fighting units first, by line and then by epoch, workers after them,
    /// and within that by owner. Entities that are gone are left out.
    /// </summary>
    public static List<UnitGroup> Of(Match match, IEnumerable<EntityId> entities)
    {
        var groups = new List<UnitGroup>();
        foreach (var entity in entities)
        {
            if (!UnitFacts.TryRead(match, entity, out var facts))
            {
                continue;
            }

            var damaged = facts.HitPoints < facts.Maximum ? 1 : 0;
            var at = groups.FindIndex(group => group.First.Owner == facts.Owner && group.First.Kind == facts.Kind);
            if (at < 0)
            {
                groups.Add(new UnitGroup(facts, 1, facts.HitPoints, facts.Maximum, damaged));
            }
            else
            {
                var group = groups[at];
                groups[at] = group with
                {
                    Count = group.Count + 1,
                    HitPoints = group.HitPoints + facts.HitPoints,
                    Maximum = group.Maximum + facts.Maximum,
                    Damaged = group.Damaged + damaged,
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
