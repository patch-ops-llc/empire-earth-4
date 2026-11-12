using System.Globalization;
using Godot;
using Sim.Core;

namespace Game;

/// <summary>
/// The smoke test's check of the sound: a second of play in which one order is given the way a
/// click gives it, through the order table, and the sound part is asked what it has played since
/// the match began. No machine that runs the smoke test has anyone listening, so the part tests
/// what was called for and started, and not what it sounded like.
/// </summary>
/// <remarks>
/// <para>
/// It fails when the view has no sound part, when a sound was not made, when the fight before
/// it called for no hit and no death, when the order was not answered with its sound, or when
/// more sounds were started than were called for.
/// </para>
/// <para>
/// The order is a move order for one of the local player's units to the point it stands on, so
/// that it changes as little as an order can. It goes through the view, as a player's does, and
/// not through <see cref="SmokeOrders.Submit"/>, so the count of orders on the smoke test's line
/// does not include it.
/// </para>
/// <para>
/// The part reaches the view through <see cref="SoundPart.Latest"/>, since a part is handed the
/// match and not its view. The fog view, 17v (pull request 48), hands a part the view; once it
/// is on <c>main</c>, this part can take the sound part from there.
/// </para>
/// </remarks>
public sealed class SoundSmoke : SmokePart
{
    private const int Length = 20;

    public override string Name => "Sound";

    public override int Ticks => Length;

    public override void GiveOrders(int tick, SmokeOrders orders)
    {
        if (tick > 0)
        {
            foreach (var happened in orders.Match.Simulation.Events)
            {
                if (happened.Kind == SimEventKind.OrderRefused && happened.Player == orders.Player)
                {
                    orders.Fail($"the simulation refused the {(CommandType)happened.Amount} order that the sound part's check gave.");
                }
            }
        }

        if (SoundPart.Latest is not { } sound)
        {
            orders.Fail("the view of the match has no sound part.");
            return;
        }

        switch (tick)
        {
            case 0:
                CheckWhatWasPlayed(sound, orders);
                GiveOneOrder(sound, orders);
                break;

            case Length - 1:
                Report(sound);

                // The smoke test quits a few thousandths of a second after its last tick, in
                // the middle of the sounds of its fight. The engine's mixer lets go of a stopped
                // sound a moment later, on a thread of its own, and at its exit reports what it
                // still holds as leaked. So the sounds are stopped here and the mixer is given
                // that moment.
                sound.Silence();
                OS.DelayMsec(80);
                break;
        }
    }

    private static void CheckWhatWasPlayed(SoundPart sound, SmokeOrders orders)
    {
        foreach (var cue in SoundCues.All)
        {
            if (sound.LengthOf(cue) <= 0.0)
            {
                orders.Fail($"the sound for when {SoundCues.Name(cue)} was not made.");
            }

            if (sound.Played(cue) > sound.Asked(cue))
            {
                orders.Fail($"the sound for when {SoundCues.Name(cue)} was started {sound.Played(cue)} times and called for {sound.Asked(cue)} times.");
            }
        }

        // The fight of the combat piece runs before this part and is the local player's own.
        var hits = sound.Played(SoundCue.MeleeHit) + sound.Played(SoundCue.ShotHit);
        var deaths = sound.Played(SoundCue.UnitDied);
        if (hits == 0 || deaths == 0)
        {
            orders.Fail(
                $"the match so far started {hits} sounds of a hit and {deaths} of a death. The fight before this part has both, " +
                "and the local player knows of them. If no fight runs before this part any more, it needs one of its own.");
        }
    }

    private static void GiveOneOrder(SoundPart sound, SmokeOrders orders)
    {
        var units = orders.OwnUnits();
        if (units.Length == 0)
        {
            orders.Fail("the local player has no unit left to give an order to.");
            return;
        }

        var view = sound.MatchView;
        var where = orders.Match.Bodies.Get(units[0]).Position;
        var asked = sound.Asked(SoundCue.OrderGiven);
        var played = sound.Played(SoundCue.OrderGiven);

        // A part before this one may have had an order answered a moment ago. The sound of this
        // one is then left out, as it would be for a player: it is called for and not started.
        var starts = sound.WouldStart(SoundCue.OrderGiven) ? 1 : 0;

        view.Selection.Set([units[0]]);
        var row = view.Orders.Give(view, new PointerTarget(PointerTargets.Ground, EntityId.None, where));
        view.Selection.Clear();

        if (row is null)
        {
            orders.Fail("the order table had no order for one unit and a point of the ground.");
        }
        else if (sound.Asked(SoundCue.OrderGiven) != asked + 1 || sound.Played(SoundCue.OrderGiven) != played + starts)
        {
            orders.Fail(
                $"the order table gave a {row.Name} order, and the sound of an order given was called for " +
                $"{sound.Asked(SoundCue.OrderGiven) - asked} times and started {sound.Played(SoundCue.OrderGiven) - played} times. " +
                $"It should be called for once and started {(starts == 1 ? "once" : "not at all, so soon after the last")}.");
        }
    }

    private static void Report(SoundPart sound)
    {
        var seconds = 0.0;
        var counts = new List<string>();
        foreach (var cue in SoundCues.All)
        {
            seconds += sound.LengthOf(cue);
            if (sound.Asked(cue) > 0)
            {
                counts.Add(string.Create(CultureInfo.InvariantCulture, $"{cue} {sound.Played(cue)} of {sound.Asked(cue)}"));
            }
        }

        var waiting = sound.Kinds.Missing.Count == 0 ? "none" : string.Join(", ", sound.Kinds.Missing);
        GD.Print(string.Create(
            CultureInfo.InvariantCulture,
            $"Smoke test, the sound: {SoundCues.All.Count} sounds made, {seconds:0.00} seconds in all. Started, of those called for: {string.Join(", ", counts)}. " +
            $"The fog is followed: {(sound.FollowsFog ? "yes" : "this build has none")}. Kinds of event not in this build: {waiting}."));
    }
}
