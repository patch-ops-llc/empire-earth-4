namespace Game;

/// <summary>
/// Research in the view (plan piece 14v): the research panel with its grid, opened from the top
/// bar or with a key, where the player starts and cancels a research and watches it finish.
/// </summary>
public sealed class ResearchPiece : ViewPiece
{
    public override string Name => "Research";

    public override void AddTo(MatchView view) => view.Interface.AddPart(new ResearchPart());
}
