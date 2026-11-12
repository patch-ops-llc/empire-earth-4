using Sim.Combat;
using Sim.Core;
using Sim.Numerics;

namespace Sim.Research;

/// <summary>
/// Executes <see cref="CommandType.DebugSetTier"/>, the development tier: it raises one track of
/// the player the command names to a tier, at once and for nothing. The design has no such order.
/// It is there so that accents and perks that take many researches to reach can be seen, tested
/// and recorded without the minutes those researches take.
/// </summary>
/// <remarks>
/// <para>
/// It shares its switch with the development spawn (<see cref="Data.DebugRules.Spawn"/>): the
/// match setup gives the command this handler only while the rules say so, and otherwise the
/// command has no handler and is dropped. The switch is data, so both players of a match have the
/// same one.
/// </para>
/// <para>
/// The track is the command's <see cref="Command.ContentId"/>, its dense index in the data. The
/// tier is the command's <see cref="Command.TargetX"/>, as a whole number.
/// <see cref="CommandFor"/> makes such a command. Nothing else in the command is read.
/// </para>
/// <para>
/// The command is valid when its player is in the match, the track is one of the data's, and the
/// tier is a whole number above the tier the track is at and not above the match's epoch cap. It
/// only ever raises a track. What it then does is what a research that finishes does, once for
/// each tier on the way: see <see cref="MatchResearch.Raise"/>, which also says what becomes of a
/// research under way on the track (cancelled, with its price given back) and that neither the
/// reach of the epoch nor a price holds it back.
/// </para>
/// <para>
/// A command runs before the systems of its tick. So the new tier is in effect in the tick the
/// command runs, where a research that finishes is in effect from the next.
/// </para>
/// <para>
/// A command that is not valid changes nothing and is reported as a
/// <see cref="SimEventKind.OrderRefused"/>.
/// </para>
/// </remarks>
public sealed class DebugSetTierHandler : ICommandHandler
{
    private readonly MatchResearch _research;
    private readonly SimEventList _events;

    public DebugSetTierHandler(MatchResearch research, SimEventList events)
    {
        ArgumentNullException.ThrowIfNull(research);
        ArgumentNullException.ThrowIfNull(events);
        _research = research;
        _events = events;
    }

    /// <summary>The command that raises a track of a player to a tier, for whoever sends one.</summary>
    /// <param name="player">The player whose track is raised.</param>
    /// <param name="track">The dense index of the track in the data.</param>
    /// <param name="tier">The tier to raise it to, from 2 to the match's epoch cap.</param>
    public static Command CommandFor(int player, int track, int tier) =>
        new(CommandType.DebugSetTier, player, targetX: Fixed.FromInt(tier), contentId: track);

    public bool Execute(World world, in Command command)
    {
        ArgumentNullException.ThrowIfNull(world);

        var tier = command.TargetX.FloorToInt();
        if (Fixed.FromInt(tier) != command.TargetX || !_research.Raise(command.Player, command.ContentId, tier))
        {
            OrderCommandHandler.Refuse(_events, in command);
            return false;
        }

        return true;
    }
}
