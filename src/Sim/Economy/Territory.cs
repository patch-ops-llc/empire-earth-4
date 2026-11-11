using Sim.Core;

namespace Sim.Economy;

/// <summary>
/// The ground a player holds around a Capitol or an Outpost (design 2.5 and 5.4): a circle around
/// the center of the structure's footprint. A tile is inside when its center is within the radius,
/// the exact distance included.
/// </summary>
/// <remarks>
/// The center is kept doubled, so that the center of a footprint with an odd side is a whole
/// number too. Distances are compared as squares of doubled coordinates, the same way the map
/// checker tests a starting territory, so the two always agree.
/// </remarks>
public struct Territory : IStateValue
{
    public const string StoreName = "territories";

    /// <summary>The player who holds it.</summary>
    public int Owner { get; set; }

    /// <summary>Twice the x of the center, in tiles.</summary>
    public int CenterX2 { get; set; }

    /// <summary>Twice the y of the center, in tiles.</summary>
    public int CenterY2 { get; set; }

    /// <summary>The radius in tiles.</summary>
    public int Radius { get; set; }

    /// <summary>The territory of a structure whose square footprint has its lowest tile at (x, y).</summary>
    public static Territory Around(int owner, int x, int y, int footprint, int radius) => new()
    {
        Owner = owner,
        CenterX2 = (2 * x) + footprint,
        CenterY2 = (2 * y) + footprint,
        Radius = radius,
    };

    /// <summary>
    /// True when every tile of the square of <paramref name="size"/> tiles from (x, y) is inside a
    /// territory of <paramref name="player"/>. The tiles need not all be in the same one.
    /// </summary>
    public static bool Holds(ComponentStore<Territory> territories, EntityRegistry entities, int player, int x, int y, int size)
    {
        ArgumentNullException.ThrowIfNull(territories);
        ArgumentNullException.ThrowIfNull(entities);

        for (var tileY = y; tileY < y + size; tileY++)
        {
            for (var tileX = x; tileX < x + size; tileX++)
            {
                var inside = false;
                for (var i = 0; i < territories.Count && !inside; i++)
                {
                    ref var territory = ref territories.ValueAt(i);
                    inside = territory.Owner == player && territory.Contains(tileX, tileY) && entities.IsAlive(territories.EntityAt(i));
                }

                if (!inside)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>True when the center of the tile is within the radius.</summary>
    public readonly bool Contains(int tileX, int tileY)
    {
        long dx = (2 * tileX) + 1 - CenterX2;
        long dy = (2 * tileY) + 1 - CenterY2;
        long reach = 2L * Radius;
        return (dx * dx) + (dy * dy) <= reach * reach;
    }

    public void Walk(IStateWalker walker)
    {
        Owner = walker.Field(Owner);
        CenterX2 = walker.Field(CenterX2);
        CenterY2 = walker.Field(CenterY2);
        Radius = walker.Field(Radius);
    }
}
