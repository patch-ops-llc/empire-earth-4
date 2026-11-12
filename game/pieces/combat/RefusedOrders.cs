using Sim.Core;

namespace Game;

/// <summary>
/// Tells the player when the simulation refused one of the player's own orders. The view sends
/// no order it knows to be invalid, so this is seen when the match changed between the click
/// and the tick the order ran in: the target of an attack died or went out of sight first, or
/// the last selected unit died.
/// </summary>
public partial class RefusedOrders : InterfacePart
{
    protected override void Attach()
    {
    }

    public override void AfterTick()
    {
        foreach (var happened in View.Match.Simulation.Events)
        {
            if (happened.Kind == SimEventKind.OrderRefused && happened.Player == View.Player)
            {
                Frame.Say(SentenceFor((CommandType)happened.Amount));
            }
        }
    }

    private static string SentenceFor(CommandType order) => order switch
    {
        CommandType.Attack => "The attack order was not carried out: its target was gone or out of sight, or none of the units ordered could attack it.",
        CommandType.AttackMove => "The attack-move order was not carried out.",
        CommandType.Move => "The move order was not carried out.",
        CommandType.Stop => "The stop order was not carried out: the units ordered were gone.",
        CommandType.DebugSpawn => "Development spawn: no unit can stand there.",
        CommandType.DebugPlace => "Development placement: no structure can stand there, or a unit stands on its tiles.",
        CommandType.DebugSetTier => "Development tier: the track is already that high, or the tier is above what this match allows.",
        _ => "An order was not carried out.",
    };
}
