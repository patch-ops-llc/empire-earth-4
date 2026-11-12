using Godot;

namespace Game;

/// <summary>
/// One part of the interface round a match: a readout in the top bar, a panel about the
/// selection, a full-screen panel. Each is a class of its own that puts its controls into the
/// places of the <see cref="InterfaceFrame"/>, so that no two parts meet in one file.
/// </summary>
/// <remarks>
/// A part reads the match and never changes it: what it does to the match leaves as a command
/// through <c>View.Client</c>, alone and over the network alike. It is added with
/// <see cref="InterfaceFrame.AddPart"/> and becomes a child of the frame. It may handle input of
/// its own in <c>_UnhandledInput</c>, through actions of the input map.
/// </remarks>
public abstract partial class InterfacePart : Node
{
    /// <summary>The match this part shows, and everything else the view of it holds.</summary>
    protected MatchView View { get; private set; } = null!;

    /// <summary>The frame whose places this part fills.</summary>
    protected InterfaceFrame Frame => View.Interface;

    /// <summary>
    /// Called once, when the part is added, before <see cref="AfterTick"/> is called for the first
    /// time. Create controls here and put them into the frame's places.
    /// </summary>
    protected abstract void Attach();

    /// <summary>
    /// Called once when the part is added and then after every tick, after the world view and its
    /// layers have read the match. In a frame that runs several ticks it is called for each.
    /// </summary>
    public virtual void AfterTick()
    {
    }

    /// <summary>Called in every frame, after the world view has drawn the frame's units.</summary>
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
