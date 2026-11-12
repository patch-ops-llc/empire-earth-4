namespace Game;

/// <summary>The match itself in the interface: who the player is in it, who has left, and the way out.</summary>
public sealed class MatchPiece : ViewPiece
{
    public override string Name => "Match";

    public override void AddTo(MatchView view) => view.Interface.AddPart(new MatchPanel());
}
