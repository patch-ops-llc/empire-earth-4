using System.Globalization;
using System.Text;
using Godot;
using Sim.Research;
using Sim.Tech;

namespace Game;

/// <summary>
/// The side of the research panel that says what the next tier of one track is: whether it can
/// be researched and why not, its price and time, what it does, the units it unlocks and every
/// accent it would add, remove or change (design 1.3 and 2.3), all before the player pays.
/// </summary>
public partial class ResearchDetail : PanelContainer
{
    // The room between two paragraphs: a line in small print with nothing on it.
    private const string Gap = "[font_size=6]\n[/font_size]";

    private RichTextLabel _text = null!;
    private string _shown = string.Empty;

    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", ResearchColors.Boxed(ResearchColors.Box, 12f, 12f, 12f));

        _text = new RichTextLabel { BbcodeEnabled = true, SelectionEnabled = false };
        _text.AddThemeFontSizeOverride("normal_font_size", 13);
        _text.AddThemeConstantOverride("line_separation", 2);
        _text.Text = _shown;
        AddChild(_text);
    }

    /// <summary>Shows the next tier of a track as the match stands.</summary>
    public void Present(ResearchReading reading, int track)
    {
        var text = Describe(reading, track);
        if (text == _shown)
        {
            return;
        }

        _shown = text;
        if (IsNodeReady())
        {
            _text.Text = text;
        }
    }

    private static string Describe(ResearchReading reading, int track)
    {
        var data = reading.Data;
        var def = data.Tracks[track];
        var color = TrackColors.Of(data, track);
        var text = new StringBuilder();

        if (reading.Preview(track) is not { } preview)
        {
            text.Append(Heading(color, string.Create(CultureInfo.InvariantCulture, $"{def.Name} {reading.Tier(track)}")));
            text.Append(Paint(ResearchColors.Faint, def.TierNames[reading.Tier(track) - 1])).Append('\n').Append(Gap);
            text.Append("This track is at its last tier.");
            return text.ToString();
        }

        var tier = preview.Offer.Tier;
        text.Append(Heading(color, string.Create(CultureInfo.InvariantCulture, $"{def.Name} {tier}")));
        text.Append(Paint(ResearchColors.Faint, $"{def.TierNames[tier - 1]}. {ResearchWords.Epoch(data, tier)}.")).Append('\n').Append(Gap);

        AppendStatus(text, reading, track, preview, color);

        text.Append(Gap).Append(Paint(ResearchColors.Faint, "What it does")).Append('\n');
        text.Append(ResearchWords.Effect(data, track, preview)).Append('\n');
        if (preview.After.Epoch != preview.Before.Epoch)
        {
            var epoch = preview.After.Epoch;
            text.Append(CultureInfo.InvariantCulture, $"Raises your epoch to {epoch}, {ResearchWords.Epoch(data, epoch)}.\n");
        }

        text.Append(Gap).Append(Paint(ResearchColors.Faint, "Unlocks")).Append('\n');
        if (preview.UnlockedChassis.Count == 0)
        {
            text.Append("No new unit.\n");
        }

        foreach (var index in preview.UnlockedChassis)
        {
            var chassis = data.Chassis[index];
            var line = data.LineOf(chassis.Line);
            text.Append(ResearchWords.NameOf(chassis.Id));
            if (line is not null)
            {
                text.Append(CultureInfo.InvariantCulture, $" ({ResearchWords.NameOf(line.Id)}, epoch {chassis.Epoch})");
            }

            text.Append('\n');
        }

        text.Append(Gap).Append(Paint(ResearchColors.Faint, "Accents on the units you can build")).Append('\n');
        AppendAccents(text, reading, preview);
        return text.ToString();
    }

    // Whether the research can start, and what it costs, or what it has cost.
    private static void AppendStatus(StringBuilder text, ResearchReading reading, int track, ResearchPreview preview, Color color)
    {
        var data = reading.Data;
        if (reading.TryGetSlot(track, out var slot))
        {
            text.Append(Paint(color, $"In research: {ResearchWords.Duration(slot.TicksLeft)} left.")).Append('\n');
            text.Append(CultureInfo.InvariantCulture, $"Paid: {ResearchWords.Price(slot.Materials, slot.Energy)}. Cancelling gives all of it back.\n");
            return;
        }

        var rules = data.Rules;
        switch (reading.Check(track))
        {
            case ResearchStartCheck.Allowed:
                text.Append(Paint(ResearchColors.Good, "Ready: click its bright cell to start."));
                break;
            case ResearchStartCheck.TooExpensive when preview.Offer.Price is { } wanted:
                text.Append(Paint(UiStyle.Warning, $"You are {Short(wanted, reading)} short."));
                text.Append(CultureInfo.InvariantCulture, $" You have {reading.Materials} Materials and {reading.Energy} Energy.");
                break;
            case ResearchStartCheck.NoFreeSlot when reading.OpenSlots() == 0:
                text.Append(Paint(UiStyle.Warning, "You have no research slot."));
                break;
            case ResearchStartCheck.NoFreeSlot:
                text.Append(Paint(UiStyle.Warning, "No research slot is free."));
                text.Append(" A slot holds one research at a time.");
                break;
            case ResearchStartCheck.BeyondReach:
                text.Append(Paint(UiStyle.Warning, "Out of reach."));
                text.Append(CultureInfo.InvariantCulture, $" No track may be more than {rules.Epoch.Reach} tiers above your epoch, which is {reading.Epoch}.");
                text.Append(CultureInfo.InvariantCulture, $" Your epoch is the highest tier that {rules.Epoch.TracksNeeded} of your tracks have reached, so bring another track up first.");
                break;
            case ResearchStartCheck.AboveEpochCap:
                text.Append(Paint(UiStyle.Warning, "Closed in this match."));
                text.Append(CultureInfo.InvariantCulture, $" Its epoch cap is {reading.EpochCap}, {ResearchWords.Epoch(data, reading.EpochCap)}, and no track may pass it.");
                break;
            default:
                text.Append(Paint(UiStyle.Warning, "It cannot be researched now."));
                break;
        }

        text.Append('\n');
        if (preview.Offer.Price is not { } price)
        {
            return;
        }

        text.Append(CultureInfo.InvariantCulture, $"Costs {ResearchWords.Price(price.Materials, price.Energy)}. Takes {ResearchWords.Duration(price.Ticks)}.\n");
        var bands = rules.ResearchPrice;
        text.Append(Paint(ResearchColors.Faint, price.Band switch
        {
            ResearchBand.CatchUp => $"Catch-up price: at or below your epoch, so cost x{bands.CatchUp.Cost} and time x{bands.CatchUp.Time}.",
            ResearchBand.Ahead => $"Ahead of its time: more than one tier above your epoch, so cost x{bands.Ahead.Cost}.",
            _ => "Frontier price: one tier above your epoch.",
        })).Append('\n');
    }

    // What the player lacks of a price: "100 Energy", "50 Materials and 100 Energy".
    private static string Short(ResearchPrice price, ResearchReading reading)
    {
        var materials = (int)Math.Max(0, price.Materials - reading.Materials);
        var energy = (int)Math.Max(0, price.Energy - reading.Energy);
        return ResearchWords.Price(materials, energy);
    }

    // Every accent the research would add, remove or change, with the units of one change in one sentence.
    private static void AppendAccents(StringBuilder text, ResearchReading reading, ResearchPreview preview)
    {
        var data = reading.Data;
        if (preview.AccentChanges.Count == 0)
        {
            text.Append("No change.\n");
            return;
        }

        var groups = new List<(int Track, int Before, int After, List<string> Units)>();
        foreach (var change in preview.AccentChanges)
        {
            var at = groups.FindIndex(group => group.Track == change.Track && group.Before == change.LevelBefore && group.After == change.LevelAfter);
            if (at < 0)
            {
                at = groups.Count;
                groups.Add((change.Track, change.LevelBefore, change.LevelAfter, []));
            }

            groups[at].Units.Add(ResearchWords.NameOf(data.Chassis[change.Chassis].Id));
        }

        foreach (var (track, before, after, units) in groups)
        {
            var accent = data.AccentOf(track);
            var name = Paint(TrackColors.Of(data, track), accent?.Name ?? data.Tracks[track].Name);
            var who = ResearchWords.List(units);
            var one = units.Count == 1;
            if (before == 0)
            {
                text.Append(CultureInfo.InvariantCulture, $"{who} {(one ? "gains" : "gain")} {name} {ResearchWords.Level(after)}");
                AppendPerks(text, accent?.PerksAt(after));
            }
            else if (after == 0)
            {
                text.Append(CultureInfo.InvariantCulture, $"{who} {(one ? "loses" : "lose")} {name} {ResearchWords.Level(before)}.");
            }
            else if (after > before)
            {
                text.Append(CultureInfo.InvariantCulture, $"{who}: {name} {ResearchWords.Level(before)} becomes {ResearchWords.Level(after)}");
                AppendPerks(text, accent?.LevelTwo);
            }
            else
            {
                text.Append(CultureInfo.InvariantCulture, $"{who}: {name} {ResearchWords.Level(before)} drops to {ResearchWords.Level(after)}.");
            }

            text.Append('\n');
        }
    }

    private static void AppendPerks(StringBuilder text, Sim.Data.PerkSet? perks)
    {
        var words = perks is null ? string.Empty : ResearchWords.Perks(perks);
        text.Append(words.Length == 0 ? "." : $": {words}.");
    }

    private static string Heading(Color color, string words) => $"[font_size=19]{Paint(color, words)}[/font_size]\n";

    private static string Paint(Color color, string words) => $"[color=#{color.ToHtml(false)}]{words}[/color]";
}
