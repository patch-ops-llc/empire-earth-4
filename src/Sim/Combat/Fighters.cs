using Sim.Core;
using Sim.Data;
using Sim.Map;
using Sim.Movement;
using Sim.Numerics;

namespace Sim.Combat;

/// <summary>
/// The melee units that are fighting: every one that stands where it last attacked from and still
/// waits for its next attack. They are what makes a rank a rank (design 2.4, "only the front rank
/// of a melee block fights"), in two ways.
/// </summary>
/// <remarks>
/// <para>
/// A melee unit that fights holds its ground among its own side (<see cref="IGroundHolders"/>).
/// A unit attacks only in a tick in which it has not moved, so a unit of its own side that stops
/// too close to it steps away alone, and the one that fights goes on fighting: the ranks behind
/// do not push the front rank about.
/// </para>
/// <para>
/// A melee unit that stands closer than their spacing to one of these has no room in the rank
/// and does not attack (<see cref="HasNoRoom"/>). Units walk through their own side and enemies
/// hold them at their spacing, so without this the ranks behind would walk into the front rank,
/// be held there by the enemy, and fight from inside it. Units are asked in the order of the
/// store, so of two that come to stand on one place the one asked first attacks, and the other
/// then has no room.
/// </para>
/// <para>
/// Units that shoot are left out of both. A rank of them that has come into range is pushed on
/// by the ranks behind like any standing unit, and the block closes up until every rank that can
/// be in range is: at one tile a rank, a range of 5 fires five ranks deep (design 3.1).
/// </para>
/// </remarks>
public sealed class Fighters : IGroundHolders
{
    private readonly CombatRules _rules;
    private readonly ComponentStore<Combatant> _combatants;
    private readonly ComponentStore<Body> _bodies;
    private readonly ComponentStore<Mover> _movers;
    private readonly SpatialIndex _spatialIndex;
    private readonly Fixed _widestSpacing;

    // Scratch for one search, emptied at its start. Not state.
    private readonly List<EntityId> _found = [];

    public Fighters(
        GameData data,
        CombatRules rules,
        ComponentStore<Combatant> combatants,
        ComponentStore<Body> bodies,
        ComponentStore<Mover> movers,
        SpatialIndex spatialIndex)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(combatants);
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(movers);
        ArgumentNullException.ThrowIfNull(spatialIndex);
        _rules = rules;
        _combatants = combatants;
        _bodies = bodies;
        _movers = movers;
        _spatialIndex = spatialIndex;
        foreach (var line in data.Lines)
        {
            _widestSpacing = Fixed.Max(_widestSpacing, line.Base.Spacing.ToFraction().ToFixed());
        }
    }

    /// <summary>True when the unit is a melee unit that stands where it last attacked from and waits for its next attack.</summary>
    public bool IsFighting(EntityId unit) =>
        _combatants.TryGet(unit, out var combatant)
        && _bodies.TryGet(unit, out var body)
        && IsFighting(in combatant, in body);

    public bool HoldsGround(EntityId unit) => IsFighting(unit);

    /// <summary>
    /// True when a unit stands closer than their spacing to a melee unit of its own side that is
    /// fighting. It reads the spatial index, which must hold where the units stand now.
    /// </summary>
    public bool HasNoRoom(EntityId unit, in Body body)
    {
        _spatialIndex.QueryCircle(body.Position, _widestSpacing, _found);
        foreach (var other in _found)
        {
            if (other == unit
                || !_combatants.TryGet(other, out var combatant)
                || !_bodies.TryGet(other, out var otherBody)
                || otherBody.Owner != body.Owner
                || !IsFighting(in combatant, in otherBody)
                || !SpacingRule.TryBetween(_movers, unit, other, out var spacing))
            {
                continue;
            }

            if (FixedVector2.DistanceSquaredRaw(body.Position, otherBody.Position) < FixedVector2.SquareRaw(spacing))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsFighting(in Combatant combatant, in Body body) =>
        combatant.Wait > Fraction.Zero
        && body.Position == combatant.Fought
        && _rules.ProfileOf(body.Owner, combatant.Chassis).Melee;
}
