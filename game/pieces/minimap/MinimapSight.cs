using Godot;
using Sim.Economy;

namespace Game;

/// <summary>
/// What of the match the minimap may show the local player. The minimap draws nothing it has not
/// asked about here, so this is the one place the fog of war changes it.
/// </summary>
/// <remarks>
/// There is no fog yet (M17), so today everything is shown. With it, a player sees their own
/// units always, an enemy unit only on ground they see now, a deposit only on ground they have
/// explored, and <see cref="Shade"/> darkens what is unexplored or out of sight. Piece 17v gives
/// these answers from the view of one player, and the drawing stays as it is.
/// </remarks>
public class MinimapSight
{
    /// <summary>True when the player may see this unit where it stands.</summary>
    /// <param name="owner">The unit's owner.</param>
    /// <param name="ground">Where it stands, as map x and y in tiles.</param>
    public virtual bool ShowsUnit(int owner, Vector2 ground) => true;

    /// <summary>True when the player knows of this deposit.</summary>
    public virtual bool ShowsDeposit(in WorkSite deposit) => true;

    /// <summary>
    /// A picture of the whole map, a pixel for a tile, drawn over the terrain and the deposits and
    /// under the units: opaque where nothing is known, half so where the player does not see now,
    /// clear elsewhere. Null while there is no fog.
    /// </summary>
    public virtual Texture2D? Shade => null;
}
