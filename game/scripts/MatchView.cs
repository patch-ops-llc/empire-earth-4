using Net;
using Sim.Setup;

namespace Game;

/// <summary>
/// One match as the player sees it and acts in it: the match to read, the local player's side of
/// the session to give commands through, and the parts of the view that every piece builds on.
/// Every <see cref="WorldLayer"/>, <see cref="InterfacePart"/>, <see cref="PointerPicker"/> and
/// row of the <see cref="OrderTable"/> is handed this, and nothing else.
/// </summary>
/// <remarks>
/// Code under <c>game/</c> reads the match and never changes it. Only commands change the
/// simulation: they leave through <see cref="Client"/>, alone and over the network alike, and
/// the session runs them a tick or more later on every machine.
/// </remarks>
public sealed class MatchView
{
    private MatchView(MatchRun run, Match match, WorldView world, RtsCamera camera, UnitControl units, InterfaceFrame frame, Action? leave)
    {
        Run = run;
        Match = match;
        World = world;
        Camera = camera;
        Units = units;
        Interface = frame;
        Leave = leave;
        Selection = new Selection();
        UnderPointer = new UnderPointer(this);
        Orders = new OrderTable();
    }

    /// <summary>The match on this machine, however it is driven: alone, hosted or joined.</summary>
    public MatchRun Run { get; }

    /// <summary>The match, to read.</summary>
    public Match Match { get; }

    /// <summary>The local player's side of the session, which every command goes through.</summary>
    public ClientSession Client => Run.Client;

    /// <summary>The local player's number.</summary>
    public int Player => Client.Player;

    /// <summary>Draws the units, and calls every <see cref="WorldLayer"/>.</summary>
    public WorldView World { get; }

    public RtsCamera Camera { get; }

    /// <summary>What the player has selected.</summary>
    public Selection Selection { get; }

    /// <summary>Answers what is under the pointer.</summary>
    public UnderPointer UnderPointer { get; }

    /// <summary>Turns the selection and what is under the pointer into an order.</summary>
    public OrderTable Orders { get; }

    /// <summary>Takes the clicks that select and that give orders.</summary>
    public UnitControl Units { get; }

    /// <summary>The frame of the interface, whose places the parts fill.</summary>
    public InterfaceFrame Interface { get; }

    /// <summary>Takes the player out of the match and back to the menu, or null when there is no menu to go back to.</summary>
    public Action? Leave { get; }

    /// <summary>
    /// Builds the view of a match: the world, the frame of the interface and the unit control are
    /// given the match, and then every piece of <see cref="ViewPieces.All"/> adds what it draws
    /// and does. Call it once, before the match's first tick. The frame must be in the tree.
    /// </summary>
    public static MatchView Open(MatchRun run, Match match, WorldView world, RtsCamera camera, UnitControl units, InterfaceFrame frame, Action? leave)
    {
        var view = new MatchView(run, match, world, camera, units, frame, leave);
        world.Attach(view);
        frame.Attach(view);
        units.Attach(view);

        foreach (var piece in ViewPieces.All)
        {
            piece.AddTo(view);
        }

        // After every tick the world reads the match first, then the selection drops what is
        // gone, then the interface reads it. Each hears of one tick at a time.
        run.Client.TickCompleted += _ =>
        {
            world.AfterTick();
            units.AfterTick();
            frame.AfterTick();
        };
        return view;
    }
}
