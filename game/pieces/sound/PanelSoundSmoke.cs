namespace Game;

/// <summary>
/// What the smoke test's checks of the panels' sound share. A panel with buttons of its own
/// sends its orders to the match itself, past the order table and the frame's commands, so
/// nothing hears them unless the panel says so: it calls <see cref="SoundPart.Acknowledge"/> as
/// the order goes. Each part here gives an order the way a click on the panel gives it and
/// checks that the sound part was called once for it.
/// </summary>
/// <remarks>
/// <para>
/// Every order is taken back before the part ends, with what it cost, so that the match is left
/// as the part found it but for a walk or two. The orders go through the view, as a player's do,
/// and not through <see cref="SmokeOrders.Submit"/>, so the count of orders on the smoke test's
/// line does not include them.
/// </para>
/// <para>
/// A part needs the match to allow its order when its first tick comes. When it does not, the
/// part fails and says what stood in the way, since an order that is not given tests nothing.
/// </para>
/// </remarks>
public abstract class PanelSoundSmoke : SmokePart
{
    /// <summary>The tick of a part at which an order given at its start has run and can be taken back.</summary>
    protected const int TakeBackAt = 15;

    /// <summary>The tick of a part at which an order taken back at <see cref="TakeBackAt"/> is gone.</summary>
    protected const int GoneAt = 30;

    public override int Ticks => 40;

    /// <summary>
    /// Does what a click on a panel does and checks that the sound part was called for one
    /// sound of an order given, and for none of an order refused. Returns false, having failed
    /// the test, when it was not.
    /// </summary>
    /// <param name="what">The click, as the rest of a sentence: "placed a Generator".</param>
    protected static bool Answered(SmokeOrders orders, string what, Action click)
    {
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(click);
        if (SoundPart.Of(orders.View.Interface) is not { } sound)
        {
            orders.Fail("the view of the match has no sound part.");
            return false;
        }

        var given = sound.Asked(SoundCue.OrderGiven);
        var refused = sound.Asked(SoundCue.OrderRefused);
        var played = sound.Played(SoundCue.OrderGiven);

        // The sound of an order that comes a moment after another is left out. Whether this one
        // is depends on the engine's clock, so the part asks before it counts.
        var starts = sound.WouldStart(SoundCue.OrderGiven) ? 1 : 0;

        click();

        var nowGiven = sound.Asked(SoundCue.OrderGiven) - given;
        var nowRefused = sound.Asked(SoundCue.OrderRefused) - refused;
        var nowPlayed = sound.Played(SoundCue.OrderGiven) - played;
        if (nowGiven == 1 && nowRefused == 0 && nowPlayed == starts)
        {
            return true;
        }

        orders.Fail(
            $"the local player {what}, and the sound of an order given was called for {nowGiven} times and started {nowPlayed} times, " +
            $"and the sound of an order refused was called for {nowRefused} times. " +
            $"An order given should be called for once and started {(starts == 1 ? "once" : "not at all, so soon after the last")}, and an order refused not at all. " +
            "A panel that sends an order calls SoundPart.Of(Frame)?.Acknowledge() as it goes.");
        return false;
    }
}
