namespace Game;

/// <summary>
/// The perks of the accents in the view (19v; design 2.3): the splash of an Overgunned unit's
/// hit, the run-up and the hit of a Rapid unit's Charge, the mark on a Supplied unit that is
/// healing, and the Materials a Standardized unit gives back when it dies. It gives no order of
/// the game. Its development aid raises a track, so that a tester can reach an accent.
/// </summary>
/// <remarks>
/// <para>
/// Each perk is shown in the color of its track, the one the research grid and the accent badges
/// use: Firepower for splash, Mobility for Charge, Logistics for Regeneration and Manufacturing
/// for Reserves. Whatever is drawn from an event is drawn only when the player may know of the
/// event (design 5.3), so a refund is shown to its player alone and a splash on unseen ground to
/// nobody.
/// </para>
/// <para>
/// It comes after the pieces that draw the shots, the health bars and the resources, and draws
/// beside what they draw.
/// </para>
/// </remarks>
public sealed class PerksPiece : ViewPiece
{
    public override string Name => "Perks";

    public override void AddTo(MatchView view)
    {
        var shots = view.World.GetChildren().OfType<ShotLayer>().FirstOrDefault();
        view.World.AddLayer(new PerkEffects(shots));
        view.World.AddLayer(new ChargeMarks());
        view.World.AddLayer(new HealingMarks());
        view.World.AddLayer(new RefundNotes());
        view.Interface.AddPart(new RefundReadout());
        view.Interface.AddPart(new DevelopmentTier());
    }
}
