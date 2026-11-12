namespace Game;

/// <summary>
/// Combat in the view (design 2.4): a mark on every unit that can fight, health bars, shots in
/// flight, an effect for every hit and death, the attack, attack-move and stop orders, and the
/// development aid that makes fighting units while no match can train one.
/// </summary>
/// <remarks>
/// Everything drawn here is read from the match and from the events of each tick
/// (<c>Simulation.Events</c>). A unit that dies is taken off the screen by the world view, which
/// draws the units; this piece draws what it leaves behind.
/// </remarks>
public sealed class CombatPiece : ViewPiece
{
    public override string Name => "Combat";

    public override void AddTo(MatchView view)
    {
        // The effects lie under the shots, and the bars over everything.
        view.World.AddLayer(new ChassisMarks());
        view.World.AddLayer(new CombatEffects());
        view.World.AddLayer(new ShotLayer());
        view.World.AddLayer(new HealthBars());

        // A click on an enemy unit is an attack order for the selected units that can hit it,
        // and asked before the move order because it names its kind of target. With the
        // attack-move key held, a click anywhere is an attack-move.
        view.Orders.Add(CombatOrders.AttackRow());
        view.Orders.Add(CombatOrders.AttackMoveRow());
        view.Interface.AddCommand(CombatOrders.StopCommand());

        view.Interface.AddPart(new SelectedUnitReadout());
        view.Interface.AddPart(new RefusedOrders());
        view.Interface.AddPart(new DevelopmentSpawn());
    }
}
