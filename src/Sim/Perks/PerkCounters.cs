using Sim.Core;
using Sim.Movement;
using Sim.Numerics;

namespace Sim.Perks;

/// <summary>
/// Keeps the <see cref="PerkCounts"/> of every unit of a chassis up to date: the systems that
/// walk a unit, damage it and let it attack tell it here. The counts begin in
/// <see cref="Combat.CombatSpawn.SpawnChassis"/>, and production adds what was paid.
/// </summary>
/// <remarks>
/// A unit without counts, such as a worker or a structure, is not counted for: every call here
/// that names one does nothing.
/// </remarks>
public sealed class PerkCounters : IWalkCounter
{
    private readonly World _world;
    private readonly ComponentStore<PerkCounts> _counts;

    public PerkCounters(World world, ComponentStore<PerkCounts> counts)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(counts);
        _world = world;
        _counts = counts;
    }

    /// <summary>
    /// The length of a step in tiles, rounded to the nearest unit of <see cref="Fixed"/>, so that
    /// the steps of a long walk add up to its length without drifting to one side.
    /// </summary>
    public static Fixed StepLength(FixedVector2 origin, FixedVector2 destination)
    {
        var squared = (ulong)FixedVector2.DistanceSquaredRaw(origin, destination);
        ulong root = IntegerMath.Sqrt(squared);

        // The root is rounded down. The next unit is nearer when the square lies above the middle
        // of the two, (root + 1/2)^2, and as whole numbers that is above root^2 + root.
        if (squared - (root * root) > root)
        {
            root++;
        }

        return root >= int.MaxValue ? Fixed.MaxValue : Fixed.FromRaw((int)root);
    }

    /// <summary>The unit took damage in this tick: its ticks without damage begin again (Regeneration).</summary>
    public void Damaged(EntityId unit)
    {
        var index = _counts.IndexOf(unit);
        if (index >= 0)
        {
            _counts.ValueAt(index).DamagedAt = _world.Tick;
        }
    }

    /// <summary>The unit walked a step: its length is added to what it has walked since its last attack (Charge).</summary>
    public void Walked(EntityId unit, FixedVector2 origin, FixedVector2 destination)
    {
        var index = _counts.IndexOf(unit);
        if (index < 0)
        {
            return;
        }

        ref var counts = ref _counts.ValueAt(index);
        var sum = (long)counts.Walked.Raw + StepLength(origin, destination).Raw;
        counts.Walked = sum >= int.MaxValue ? Fixed.MaxValue : Fixed.FromRaw((int)sum);
    }

    /// <summary>
    /// The unit attacks: the distance it has walked since its last attack, and the count begins
    /// again at zero. Zero for a unit without counts.
    /// </summary>
    public Fixed TakeWalked(EntityId unit)
    {
        var index = _counts.IndexOf(unit);
        if (index < 0)
        {
            return Fixed.Zero;
        }

        ref var counts = ref _counts.ValueAt(index);
        var walked = counts.Walked;
        counts.Walked = Fixed.Zero;
        return walked;
    }
}
