using System.Globalization;
using Godot;
using Sim.Research;

namespace Game;

/// <summary>
/// The smoke test's look at the epoch announcement: the epoch of the local player and of the
/// other seat is raised by one, and each must get its banner, with the words of
/// <see cref="InspectionWords"/>, under the messages, in the middle of the window, and over
/// nothing else the interface shows.
/// </summary>
/// <remarks>
/// <para>
/// An epoch rises when enough tracks reach its tier, and a research takes minutes, so the part
/// raises the tracks with the development tier command (<see cref="DebugSetTierHandler"/>), which
/// does what a research that finishes does. It needs <c>debug.spawn</c> on in <c>rules.jsonc</c>,
/// as the fight does, and raises no more tracks than the epoch needs.
/// </para>
/// <para>
/// Three messages that do not fit on one line are shown first, so that the banners have
/// something to stand under, a fighting unit is selected, so that they have the bottom panel
/// to stay above, and the window is the narrowest there is
/// (<see cref="InterfaceLayoutSmoke.UseNarrowestWindow"/>). The look comes three frames after
/// the commands, when the banners have their place.
/// </para>
/// </remarks>
public sealed class EpochAnnouncementSmoke : SmokePart
{
    private const int LookAt = 60;

    private const string LongMessage =
        "The Academy cannot be ordered yet: it needs more materials than the stock holds, "
        + "and the last one the rules allow is already under way. Wait for the generators.";

    private readonly List<(int Player, int Epoch)> _raised = [];
    private int _before;

    public override string Name => "Epoch announcement";

    public override int Ticks => LookAt + 10;

    public override void GiveOrders(int tick, SmokeOrders orders)
    {
        var frame = orders.View.Interface;
        if (frame.Parts.OfType<EpochAnnouncement>().FirstOrDefault() is not { } banners)
        {
            if (tick == 0)
            {
                orders.Fail("the interface has no part for the epoch announcement.");
            }

            return;
        }

        if (tick == 0)
        {
            Raise(orders, frame, banners);
        }
        else if (tick == LookAt)
        {
            Look(orders, frame, banners);
        }
    }

    private void Raise(SmokeOrders orders, InterfaceFrame frame, EpochAnnouncement banners)
    {
        var match = orders.Match;
        if (!match.Data.Rules.Debug.Spawn)
        {
            orders.Fail("the development commands are off in the rules, and the part has no other way to raise an epoch in the time it has.");
            return;
        }

        InterfaceLayoutSmoke.UseNarrowestWindow(frame);

        // A unit is selected, as it mostly is in a match, so that the bottom panel is shown.
        foreach (var unit in orders.OwnUnits())
        {
            if (match.Combatants.Contains(unit))
            {
                orders.View.Selection.Set([unit]);
                break;
            }
        }

        for (var i = 0; i < 3; i++)
        {
            frame.Say(LongMessage, seconds: 30.0);
        }

        _before = banners.Count;
        RaiseEpochOf(orders, orders.Player);
        if (match.Players.Count > 1)
        {
            RaiseEpochOf(orders, (orders.Player + 1) % match.Players.Count);
        }
    }

    // Raises as many of the player's tracks to the tier of the next epoch as that epoch needs,
    // the first tracks of the data first.
    private void RaiseEpochOf(SmokeOrders orders, int player)
    {
        var match = orders.Match;
        var data = match.Data;
        var epoch = match.Research.Tech(player).Epoch + 1;
        if (epoch > match.Research.EpochCap || epoch > data.TierCount)
        {
            orders.Fail(string.Create(
                CultureInfo.InvariantCulture,
                $"{MatchPanel.PlayerName(player)} is at the last epoch of the match, {epoch - 1}, so there is none to announce. The part must run before whatever raised it."));
            return;
        }

        var reached = 0;
        for (var track = 0; track < data.Tracks.Count; track++)
        {
            reached += match.Research.State.Tier(player, track) >= epoch ? 1 : 0;
        }

        for (var track = 0; track < data.Tracks.Count && reached < data.Rules.Epoch.TracksNeeded; track++)
        {
            if (match.Research.State.Tier(player, track) >= epoch)
            {
                continue;
            }

            var command = DebugSetTierHandler.CommandFor(player, track, epoch);
            if (player == orders.Player)
            {
                orders.Submit(command);
            }
            else
            {
                orders.SubmitFor(command);
            }

            reached++;
        }

        _raised.Add((player, epoch));
    }

    private void Look(SmokeOrders orders, InterfaceFrame frame, EpochAnnouncement banners)
    {
        var match = orders.Match;
        if (_raised.Count == 0)
        {
            return;
        }

        if (banners.Count != _before + _raised.Count)
        {
            orders.Fail($"{_raised.Count} epochs were raised and {banners.Count - _before} were announced.");
            return;
        }

        var shown = banners.Shown.ToList();
        if (shown.Count < _raised.Count)
        {
            orders.Fail($"{_raised.Count} epochs were announced and {shown.Count} banners are shown.");
            return;
        }

        // The newest banners are the last, in the order of the players.
        _raised.Sort();
        var middle = frame.MiddleWidth;
        var window = frame.GetViewport().GetVisibleRect().Size.X;
        for (var i = 0; i < _raised.Count; i++)
        {
            var (player, epoch) = _raised[i];
            var who = MatchPanel.PlayerName(player);
            if (match.Research.Tech(player).Epoch != epoch)
            {
                orders.Fail($"{who} is in epoch {match.Research.Tech(player).Epoch} and was raised to {epoch}.");
            }

            var banner = shown[shown.Count - _raised.Count + i];
            var says = string.Join(" / ", banner.GetChild(0).GetChildren().OfType<Label>().Select(line => line.Text));
            var title = InspectionWords.EpochTitle(match.Data, epoch);
            var sentence = InspectionWords.EpochSentence(match.Data, player, orders.Player, epoch, match.Research.EpochCap);
            if (says != $"{title} / {sentence}")
            {
                orders.Fail($"the banner for {who} says '{says}' and should say '{title} / {sentence}'.");
            }

            var place = banner.GetGlobalRect();
            if (place.Position.Y < frame.MessagesEnd - 0.5f)
            {
                orders.Fail(string.Create(
                    CultureInfo.InvariantCulture,
                    $"the banner for {who} begins at {place.Position.Y:F0} and the messages end at {frame.MessagesEnd:F0}."));
            }

            if (place.Size.X > middle + 0.5f || Math.Abs(place.GetCenter().X - (window / 2f)) > 1f)
            {
                orders.Fail(string.Create(
                    CultureInfo.InvariantCulture,
                    $"the banner for {who} is {place.Size.X:F0} wide round {place.GetCenter().X:F0}, and the middle of the window is {middle:F0} wide round {window / 2f:F0}."));
            }
        }

        foreach (var problem in frame.LayoutProblems())
        {
            orders.Fail($"with the epoch banners shown, {problem}");
        }

        var boxes = frame.Boxes();
        var among = boxes.Count(box => box.Name.Contains("EpochBanner", StringComparison.Ordinal));
        if (among < _raised.Count)
        {
            orders.Fail($"{_raised.Count} banners are shown and {among} are among the boxes the frame looks at: {string.Join(", ", boxes.Select(box => box.Name))}");
        }
    }
}
