namespace Sim.Core;

/// <summary>
/// Finds an entity among IDs in ascending order: the search behind every lookup in a
/// <see cref="ComponentStore{T}"/> and in the <see cref="EntityRegistry"/>.
/// </summary>
/// <remarks>
/// It answers exactly as <see cref="Array.BinarySearch{T}(T[], int, int, T)"/> does for IDs that
/// ascend without repeating. It is written out because that one compares through
/// <see cref="IComparable{T}"/>, a call for every step, and the lookups are what a large fight
/// spends most of its time in.
/// </remarks>
internal static class EntitySearch
{
    /// <summary>
    /// The position of an ID among the first <paramref name="count"/> of <paramref name="ids"/>,
    /// or, when it is not there, the bitwise complement of the position it would be put at.
    /// </summary>
    public static int Find(EntityId[] ids, int count, EntityId id)
    {
        var value = id.Value;
        var low = 0;
        var high = count - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) >> 1);
            var found = ids[middle].Value;
            if (found == value)
            {
                return middle;
            }

            if (found < value)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return ~low;
    }
}
