using System.Globalization;
using Godot;

namespace Game;

/// <summary>
/// The row under the top bar that shows what the keys of this piece reach: a button for every
/// group that holds a unit, and one for the idle workers while there are any. Each button does
/// what its key does.
/// </summary>
public partial class GroupStrip : HBoxContainer
{
    private readonly Button[] _groups = new Button[ControlGroups.Count];
    private readonly StyleBoxFlat _plain = ChipStyle(new Color(0.2f, 0.26f, 0.36f), null);
    private readonly StyleBoxFlat _selected = ChipStyle(new Color(0.2f, 0.26f, 0.36f), new Color(0.55f, 1f, 0.55f));
    private Button _idle = null!;
    private string? _idleKey;

    /// <summary>Raised when the button of a group is pressed, with the group counted from 0.</summary>
    public event Action<int>? GroupPressed;

    /// <summary>Raised when the button of the idle workers is pressed.</summary>
    public event Action? IdlePressed;

    /// <summary>Builds the row. Call it once.</summary>
    /// <param name="idleKey">The key that selects the next idle worker, as the player knows it, or null.</param>
    public void Begin(string? idleKey)
    {
        Name = "GroupStrip";
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", 6);
        _idleKey = idleKey;

        for (var group = 0; group < ControlGroups.Count; group++)
        {
            var number = group;
            var digit = ControlGroups.DigitOf(group);
            var chip = Chip();
            chip.Name = string.Create(CultureInfo.InvariantCulture, $"Group{digit}");
            chip.TooltipText = string.Create(CultureInfo.InvariantCulture, $"Group {digit}. One click selects it, a second looks at it.");
            chip.Pressed += () => GroupPressed?.Invoke(number);
            _groups[group] = chip;
            AddChild(chip);
        }

        // The idle workers come and go far more often than a group does. Their button stands
        // last, so that the groups' buttons do not move under the pointer when it does.
        _idle = Chip();
        _idle.Name = "IdleWorkers";
        _idle.TooltipText = "Selects the next worker that has nothing to do, and looks at it.";
        _idle.Pressed += () => IdlePressed?.Invoke();
        AddChild(_idle);
    }

    /// <summary>Shows how many workers are idle. The button goes away while none is.</summary>
    public void ShowIdle(int count)
    {
        _idle.Visible = count > 0;
        var text = count == 1 ? "1 idle worker" : string.Create(CultureInfo.InvariantCulture, $"{count} idle workers");
        _idle.Text = _idleKey is null ? text : $"{text} ({_idleKey})";
    }

    /// <summary>Shows a group: how many units it holds, and whether it is what is selected now.</summary>
    public void ShowGroup(int group, int count, bool selected)
    {
        var chip = _groups[group];
        chip.Visible = count > 0;
        chip.Text = string.Create(CultureInfo.InvariantCulture, $"{ControlGroups.DigitOf(group)}: {count}");
        chip.AddThemeStyleboxOverride("normal", selected ? _selected : _plain);
    }

    private Button Chip()
    {
        var chip = UiStyle.FrameButton(string.Empty);
        chip.Visible = false;
        chip.AddThemeStyleboxOverride("normal", _plain);
        chip.AddThemeStyleboxOverride("hover", ChipStyle(new Color(0.27f, 0.35f, 0.48f), null));
        chip.AddThemeStyleboxOverride("pressed", ChipStyle(new Color(0.15f, 0.2f, 0.28f), null));
        chip.AddThemeFontSizeOverride("font_size", 14);
        return chip;
    }

    // A button of the frame, with less room round its text.
    private static StyleBoxFlat ChipStyle(Color color, Color? border)
    {
        var style = UiStyle.BoxStyle(color, border);
        style.ContentMarginLeft = 8f;
        style.ContentMarginRight = 8f;
        style.ContentMarginTop = 3f;
        style.ContentMarginBottom = 3f;
        return style;
    }
}
