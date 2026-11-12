using Godot;

namespace Game;

/// <summary>
/// One kind of thing drawn in the world beside the units: the rings under the selection, the mark
/// of an order, and later structures, deposits, shots, health bars and the fog. Each is a class of
/// its own that the <see cref="WorldView"/> calls after every tick and in every frame, so that no
/// two of them meet in one file.
/// </summary>
/// <remarks>
/// A layer reads the match and never changes it. It is added with
/// <see cref="WorldView.AddLayer"/>, becomes a child of the world view, and creates the nodes it
/// draws with under itself. It overrides neither <c>_Process</c> nor a handler of the session's
/// events: the world view calls it at the right moments, in the order the layers were added.
/// </remarks>
public abstract partial class WorldLayer : Node3D
{
    /// <summary>The match this layer draws, and everything else the view of it holds.</summary>
    protected MatchView View { get; private set; } = null!;

    /// <summary>
    /// Called once, when the layer is added, before <see cref="AfterTick"/> is called for the
    /// first time. Create nodes here.
    /// </summary>
    protected virtual void Attach()
    {
    }

    /// <summary>
    /// Called once when the layer is added and then after every tick, after the world view has
    /// read where the units stand. In a frame that runs several ticks it is called for each, so
    /// the events of the tick, in <c>View.Match.Simulation.Events</c>, are those of one tick.
    /// </summary>
    public virtual void AfterTick()
    {
    }

    /// <summary>
    /// Called in every frame, after the world view has put every unit where it is drawn in this
    /// frame, so that what follows a unit is exactly on it.
    /// </summary>
    /// <param name="delta">The seconds since the last frame.</param>
    public virtual void EveryFrame(double delta)
    {
    }

    internal void Begin(MatchView view)
    {
        View = view;
        Attach();
        AfterTick();
    }
}
