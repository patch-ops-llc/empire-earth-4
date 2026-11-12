using Sim.Core;
using Sim.Numerics;

namespace Sim.Movement;

/// <summary>
/// The spacing rule of design 2.4, in one place: the least distance between the centers of two
/// units, and what it means between units of one side and between enemies.
/// </summary>
/// <remarks>
/// <para>
/// Every unit has a value from its line, and between two units the larger of the two applies.
/// Combat asks here too: melee reach against a unit is <see cref="Between(in Mover, in Mover)"/>
/// plus a quarter of a tile (design 2.4, "Range and melee reach").
/// </para>
/// <para>
/// Among a player's own units spacing is soft: <see cref="DistanceOwnUnitsKeep"/> is what
/// <see cref="SeparationSystem"/> steps them apart to, and they never block each other. Between
/// enemies it is hard: <see cref="AllowsStep"/> is asked before any unit moves, walking or
/// settling, and enemies never push each other.
/// </para>
/// <para>
/// Every unit here walks on the ground, workers included. Air units, which neither block ground
/// units nor are blocked by them and keep spacing only from each other, come with the Air line;
/// the test for them belongs in <see cref="AreEnemyGroundUnits"/> and <see cref="DistanceOwnUnitsKeep"/>.
/// </para>
/// </remarks>
public static class SpacingRule
{
    /// <summary>The spacing between two units, from the value of each: the larger one.</summary>
    public static Fixed Between(Fixed first, Fixed second) => Fixed.Max(first, second);

    /// <summary>The spacing between two units.</summary>
    public static Fixed Between(in Mover first, in Mover second) => Between(first.Spacing, second.Spacing);

    /// <summary>
    /// The spacing between two entities. False when either is not a unit, such as a structure,
    /// which has no spacing.
    /// </summary>
    public static bool TryBetween(ComponentStore<Mover> movers, EntityId first, EntityId second, out Fixed spacing)
    {
        ArgumentNullException.ThrowIfNull(movers);
        var firstIndex = movers.IndexOf(first);
        var secondIndex = movers.IndexOf(second);
        if (firstIndex < 0 || secondIndex < 0)
        {
            spacing = Fixed.Zero;
            return false;
        }

        spacing = Between(movers.ValueAt(firstIndex), movers.ValueAt(secondIndex));
        return true;
    }

    /// <summary>
    /// True for two units that block each other: units of different players. With teams, units of
    /// allied players will not.
    /// </summary>
    public static bool AreEnemyGroundUnits(int firstOwner, int secondOwner) => firstOwner != secondOwner;

    /// <summary>
    /// The distance two units of one side step apart to, as they are now, or zero when they keep none.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>Two units that stand keep their spacing.</item>
    /// <item>Two units that walk keep half of it where they have the room, so a crowd closes up and does not pile onto one point.</item>
    /// <item>A unit that walks and a unit that stands keep none: the one walks through the other. Nor does a unit waiting for its route.</item>
    /// <item>A unit that waits for enemy units to clear its way keeps none and stands still, so units held at an enemy line close up there.</item>
    /// </list>
    /// </remarks>
    public static Fixed DistanceOwnUnitsKeep(in Mover first, in Mover second)
    {
        if (first.State == MoveState.Idle && second.State == MoveState.Idle)
        {
            return Between(first, second);
        }

        return IsWalking(first) && IsWalking(second) ? Between(first, second) / 2 : Fixed.Zero;
    }

    /// <summary>
    /// Whether a unit may go straight from one point to another with an enemy unit standing where
    /// it does now, the spacing between the two being <paramref name="spacing"/>.
    /// </summary>
    /// <remarks>
    /// No movement may bring a unit closer to an enemy than their spacing: the whole way must stay
    /// that far from the enemy, its ends included, and a way at exactly the spacing is allowed. So
    /// two enemies leave a way between them only when they stand at least twice the spacing apart.
    /// A unit that is already closer may move only if that brings it no nearer.
    /// </remarks>
    public static bool AllowsStep(FixedVector2 from, FixedVector2 to, FixedVector2 enemy, Fixed spacing)
    {
        var least = FixedVector2.SquareRaw(spacing);
        var before = FixedVector2.DistanceSquaredRaw(from, enemy);
        var after = FixedVector2.DistanceSquaredRaw(to, enemy);
        if (before < least)
        {
            return after >= before;
        }

        if (after < least)
        {
            return false;
        }

        // Both ends are far enough. The nearest point of the way is an end unless the enemy lies
        // beside the way; then it is where the perpendicular from the enemy meets it, at a squared
        // distance of before - along² / length².
        var stepX = (long)to.X.Raw - from.X.Raw;
        var stepY = (long)to.Y.Raw - from.Y.Raw;
        var along = (stepX * ((long)enemy.X.Raw - from.X.Raw)) + (stepY * ((long)enemy.Y.Raw - from.Y.Raw));
        var lengthSquared = (stepX * stepX) + (stepY * stepY);
        if (along <= 0 || along >= lengthSquared)
        {
            return true;
        }

        return (Int128)(before - least) * lengthSquared >= (Int128)along * along;
    }

    private static bool IsWalking(in Mover mover) => mover.State is MoveState.Moving or MoveState.Following && !mover.Waiting;
}
