namespace Sim.Setup;

/// <summary>
/// One player of a match, as the match setup needs it. A player's number is its position in the
/// match's player list, from 0, and that is the number in <see cref="Core.Command.Player"/>.
/// </summary>
/// <param name="Start">
/// The index of the player's starting position in the map's list of starts, or
/// <see cref="NoStart"/> for a player who is given nothing at the start. Tests and the battle
/// harness use the latter and place units themselves.
/// </param>
public sealed record PlayerSetup(int Start)
{
    public const int NoStart = -1;

    /// <summary>A player with no starting position.</summary>
    public static PlayerSetup WithoutStart { get; } = new(NoStart);

    public bool HasStart => Start != NoStart;
}
