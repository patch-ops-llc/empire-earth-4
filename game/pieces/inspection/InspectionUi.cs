using System.Globalization;
using Godot;
using Sim.Data;
using Sim.Tech;

namespace Game;

/// <summary>
/// The controls the inspection panel is built from: a line of text, a health bar, the card of a
/// stat with the tier behind it, the short form of a tier for a summary, and an accent's badge.
/// </summary>
public static class InspectionUi
{
    public static Label Text(string text, Color color, int size)
    {
        var label = new Label { Text = text, VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    /// <summary>A small square of a player's color.</summary>
    public static ColorRect Swatch(int player) => new()
    {
        Color = PlayerColors.Of(player),
        CustomMinimumSize = new Vector2(12f, 12f),
        SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
    };

    /// <summary>
    /// A stat with the tier behind it: what it is, its value, and under them the track in its
    /// color with the tier the chassis uses and how far that is ahead of the chassis or behind.
    /// </summary>
    /// <param name="data">The game data.</param>
    /// <param name="stat">What the value is: "Damage a second".</param>
    /// <param name="value">The value.</param>
    /// <param name="blueprint">The chassis as its owner has it.</param>
    /// <param name="track">The track behind the stat.</param>
    /// <param name="ownerTier">The owner's own tier in the track, to say so when the chassis cannot use all of it, or 0 to say nothing.</param>
    public static PanelContainer StatCard(GameData data, string stat, string value, ChassisBlueprint blueprint, int track, int ownerTier)
    {
        var color = TrackColors.Of(data, track);
        var style = ResearchColors.Boxed(ResearchColors.Box, 9f, 8f, 4f);
        style.BorderColor = color;
        style.BorderWidthLeft = 3;

        var card = new PanelContainer
        {
            Name = data.Tracks[track].Name,
            CustomMinimumSize = new Vector2(112f, 0f),
            TooltipText = TierTooltip(data, blueprint, track, ownerTier),
        };
        card.AddThemeStyleboxOverride("panel", style);

        var lines = UiStyle.Column(-2);
        lines.MouseFilter = Control.MouseFilterEnum.Ignore;
        card.AddChild(lines);
        lines.AddChild(Text(stat, ResearchColors.Faint, 11));
        lines.AddChild(Text(value, ResearchColors.Ink, 17));
        lines.AddChild(TierLine(data, blueprint, track, shortName: false, size: 12));
        if (ownerTier > blueprint.EffectiveTiers[track])
        {
            lines.AddChild(Text(string.Create(CultureInfo.InvariantCulture, $"of your tier {ownerTier}"), ResearchColors.Faint, 11));
        }

        return card;
    }

    /// <summary>
    /// A track in its color with the tier the chassis uses, and the offset after it when the
    /// track is ahead of the chassis or behind: "Firepower 3 +2", or "FP 3 +2" in short.
    /// </summary>
    public static HBoxContainer TierLine(GameData data, ChassisBlueprint blueprint, int track, bool shortName, int size)
    {
        var definition = data.Tracks[track];
        var offset = blueprint.Offsets[track];
        var row = UiStyle.Row(4);
        row.MouseFilter = Control.MouseFilterEnum.Ignore;
        var name = shortName ? definition.ShortName : definition.Name;
        row.AddChild(Text(string.Create(CultureInfo.InvariantCulture, $"{name} {blueprint.EffectiveTiers[track]}"), TrackColors.Of(data, track), size));
        if (offset != 0)
        {
            // Ahead is what gives an accent, so it is bright. Behind stands back.
            row.AddChild(Text(InspectionWords.Offset(offset), offset > 0 ? ResearchColors.Ink : ResearchColors.Faint, size));
        }

        return row;
    }

    /// <summary>A tier in short, in a box, for a row of a summary.</summary>
    public static PanelContainer TierChip(GameData data, ChassisBlueprint blueprint, int track)
    {
        var chip = new PanelContainer
        {
            CustomMinimumSize = new Vector2(62f, 0f),
            TooltipText = TierTooltip(data, blueprint, track, ownerTier: 0),
        };
        chip.AddThemeStyleboxOverride("panel", ResearchColors.Boxed(ResearchColors.Box, 7f, 7f, 1f));
        chip.AddChild(TierLine(data, blueprint, track, shortName: true, size: 13));
        return chip;
    }

    /// <summary>An accent as a unit carries it over its head, with its name beside it: the badge, then "Overgunned II" in the track's color.</summary>
    public static HBoxContainer AccentName(GameData data, Accent accent, int size)
    {
        var row = UiStyle.Row(5);
        row.AddChild(new AccentBadgeIcon(TrackColors.Of(data, accent.Track), accent.Level));
        row.AddChild(Text(InspectionWords.Accent(data, accent), TrackColors.Of(data, accent.Track), size));
        return row;
    }

    private static string TierTooltip(GameData data, ChassisBlueprint blueprint, int track, int ownerTier)
    {
        var window = data.Rules.EffectiveTierWindow;
        var epoch = blueprint.Chassis.Epoch;
        var first = Math.Max(1, epoch - window);
        var last = Math.Min(data.TierCount, epoch + window);
        var name = data.Tracks[track].Name;
        var text = string.Create(
            CultureInfo.InvariantCulture,
            $"A chassis of epoch {epoch} uses {name} from tier {first} to tier {last}. This one uses tier {blueprint.EffectiveTiers[track]}");
        var offset = blueprint.Offsets[track];
        text += offset switch
        {
            > 0 => string.Create(CultureInfo.InvariantCulture, $", {offset} ahead of its epoch."),
            < 0 => string.Create(CultureInfo.InvariantCulture, $", {-offset} behind its epoch."),
            _ => ", level with its epoch.",
        };
        if (ownerTier > blueprint.EffectiveTiers[track])
        {
            text += string.Create(CultureInfo.InvariantCulture, $" Your {name} is at tier {ownerTier}: a newer chassis uses more of it.");
        }

        return text;
    }
}
