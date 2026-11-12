namespace Game;

/// <summary>
/// Sound in the view (design 6: minimal sound): a short sound for an attack that lands, a unit
/// that dies, a structure finished and a structure destroyed, a research finished, a new epoch,
/// an order given and an order refused; and a panel with the volume and a mute.
/// </summary>
/// <remarks>
/// Every sound is made in code (<see cref="SoundRecipes"/>). What is played follows the events
/// of each tick (<c>Simulation.Events</c>) and the player's own orders, and nothing of it is
/// seen by the simulation.
/// </remarks>
public sealed class SoundPiece : ViewPiece
{
    public override string Name => "Sound";

    public override void AddTo(MatchView view)
    {
        var sound = new SoundPart();
        view.Interface.AddPart(sound);
        view.Interface.AddPanel("Sound", SoundPanel.Build(sound));
    }
}
