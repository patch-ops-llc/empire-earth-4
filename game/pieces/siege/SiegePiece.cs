namespace Game;

/// <summary>
/// Structures in combat in the view (16b; design 2.4, 2.5): a structure that has lost hit
/// points has a bar, is drawn darker and smokes; a hit on one flashes; a Tower's shots fly from
/// its top; a structure that is destroyed falls and leaves rubble; and a click with the order
/// button on another player's structure is an attack order on it.
/// </summary>
/// <remarks>
/// <para>
/// It comes after the pieces that draw structures and shots, and draws over what they draw.
/// The darker look itself is the <see cref="StructureLayer"/>'s, and the Tower's shots are the
/// <see cref="ShotLayer"/>'s, like every other shot.
/// </para>
/// <para>
/// Under the fog of war (design 5.3) all of it goes by what the player knows. A structure the
/// player remembers is shown as it was when last seen, with the bar and the darkness it had
/// then, and may be ordered attacked. When it has been destroyed out of the player's sight it
/// stays on the screen until the player sees its tiles again, and then it is gone without any
/// effect: nobody saw it fall.
/// </para>
/// </remarks>
public sealed class SiegePiece : ViewPiece
{
    public override string Name => "Structures in combat";

    public override void AddTo(MatchView view)
    {
        var layers = view.World.GetChildren();
        var structures = layers.OfType<StructureLayer>().FirstOrDefault()
            ?? throw new InvalidOperationException("Structures in combat are added after the piece that draws structures.");
        var effects = new StructureEffects(structures, layers.OfType<ShotLayer>().FirstOrDefault());
        view.World.AddLayer(effects);
        view.World.AddLayer(new StructureBars(structures));

        // A click on another player's structure. It names its kind of target, so it is asked
        // before the move order.
        view.Orders.Add(StructureAttack.Row(effects));
        view.Interface.AddPart(new DevelopmentPlace());
    }
}
