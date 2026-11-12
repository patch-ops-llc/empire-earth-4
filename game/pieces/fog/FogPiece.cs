namespace Game;

/// <summary>
/// The fog of war in the view (design 5.3). The player is shown their own picture of the match:
/// ground they have never seen is dark and empty, ground they have seen and do not see now is
/// dimmed and shows its terrain, and another player's units, their shots and what happens to
/// them are on the screen only while the player sees them.
/// </summary>
/// <remarks>
/// Most of it is not in this folder, because the fog is not one more thing drawn: it decides
/// what everything else draws. The view of a match holds the player's <see cref="PlayerSight"/>,
/// which every drawer asks, and the <see cref="WorldView"/> draws the units the player sees and
/// shades the ground and the terrain with the <see cref="FogPicture"/>. What this piece adds is
/// the fog on the minimap, which <see cref="FogOnMinimap"/> answers for the minimap's part, and
/// the development switch that lifts the fog from the screen (<see cref="DevelopmentFog"/>).
/// </remarks>
public sealed class FogPiece : ViewPiece
{
    public override string Name => "Fog of war";

    public override void AddTo(MatchView view) => view.Interface.AddPart(new DevelopmentFog());
}
