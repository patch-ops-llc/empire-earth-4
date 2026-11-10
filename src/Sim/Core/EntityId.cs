using System.Globalization;

namespace Sim.Core;

/// <summary>
/// The identity of one entity. IDs are handed out in ascending order starting at 1 and are never
/// reused, so a lower ID always means an older entity. That makes ID order a total order every
/// machine agrees on, and the tie-break for every sort in the simulation.
/// </summary>
public readonly record struct EntityId(int Value) : IComparable<EntityId>
{
    /// <summary>No entity. The default value.</summary>
    public static EntityId None => default;

    public static bool operator <(EntityId a, EntityId b) => a.Value < b.Value;

    public static bool operator >(EntityId a, EntityId b) => a.Value > b.Value;

    public static bool operator <=(EntityId a, EntityId b) => a.Value <= b.Value;

    public static bool operator >=(EntityId a, EntityId b) => a.Value >= b.Value;

    public int CompareTo(EntityId other) => Value.CompareTo(other.Value);

    public override int GetHashCode() => Value;

    public override string ToString() => "#" + Value.ToString(CultureInfo.InvariantCulture);
}
