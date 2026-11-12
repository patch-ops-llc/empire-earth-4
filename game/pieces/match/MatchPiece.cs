namespace Game;

/// <summary>
/// The match itself in the interface: who the player is in it, who has left, and the way out;
/// how it ends, with the end screen and conceding; and its pause and speed, which are the host's.
/// </summary>
public sealed class MatchPiece : ViewPiece
{
    public override string Name => "Match";

    public override void AddTo(MatchView view)
    {
        view.Interface.AddPart(new MatchPanel());
        view.Interface.AddPart(new PaceControl());
        view.Interface.AddPart(new EndScreen());
    }
}
