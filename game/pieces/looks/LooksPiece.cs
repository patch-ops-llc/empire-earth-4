namespace Game;

/// <summary>
/// What a structure tells of its owner's research, part of the readability set (M20, design
/// 3.2): its look follows the tier of the track it shows, and an Academy flies the banner of
/// the track in research in its slot.
/// </summary>
public sealed class LooksPiece : ViewPiece
{
    public override string Name => "Structure looks";

    public override void AddTo(MatchView view)
    {
        view.World.AddLayer(new TierMarkLayer());
        view.World.AddLayer(new BannerLayer());
    }
}
