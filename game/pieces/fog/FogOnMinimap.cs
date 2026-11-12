using Godot;
using Sim.Economy;
using Sim.Vision;

namespace Game;

/// <summary>
/// The fog of war on the minimap (design 5.3): the answers of the minimap's
/// <see cref="MinimapSight"/> from the local player's <see cref="PlayerSight"/>, and the
/// <see cref="FogPicture"/> of the world as the shade over its terrain. So the minimap and the
/// world always show the same fog.
/// </summary>
public sealed class FogOnMinimap(MatchView view) : MinimapSight
{
    public override bool ShowsUnit(int owner, Vector2 ground) => view.Sight.ShowsUnitAt(owner, ground);

    public override bool ShowsDeposit(in WorkSite deposit) => view.Sight.ShowsDeposit(deposit);

    public override void CollectStructures(MatchView view, List<SeenStructure> structures) => view.Sight.CollectStructures(structures);

    public override Texture2D? Shade => view.World.Fog.Texture;
}
