using Godot;

namespace Game;

/// <summary>
/// How the interface looks, in one place: the menu, the frame round a match and every part a view
/// piece adds build their buttons, panels and labels here, so that they look alike.
/// </summary>
public static class UiStyle
{
    /// <summary>The color of a line that tells the player something went wrong.</summary>
    public static readonly Color Warning = new(1f, 0.72f, 0.45f);

    /// <summary>The background of every panel.</summary>
    public static readonly Color PanelColor = new(0.09f, 0.1f, 0.13f, 0.94f);

    public static Button PushButton(string text)
    {
        var button = new Button { Text = text };
        button.AddThemeStyleboxOverride("normal", BoxStyle(new Color(0.2f, 0.26f, 0.36f)));
        button.AddThemeStyleboxOverride("hover", BoxStyle(new Color(0.27f, 0.35f, 0.48f)));
        button.AddThemeStyleboxOverride("pressed", BoxStyle(new Color(0.15f, 0.2f, 0.28f)));
        button.AddThemeStyleboxOverride("disabled", BoxStyle(new Color(0.16f, 0.17f, 0.2f)));
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        return button;
    }

    /// <summary>
    /// A button of the frame round a match. It never takes the keyboard, so that the keys keep
    /// moving the camera after a click on it.
    /// </summary>
    public static Button FrameButton(string text)
    {
        var button = PushButton(text);
        button.FocusMode = Control.FocusModeEnum.None;
        return button;
    }

    public static LineEdit Field(string text)
    {
        var field = new LineEdit { Text = text };
        field.AddThemeStyleboxOverride("normal", BoxStyle(new Color(0.03f, 0.035f, 0.05f)));
        field.AddThemeStyleboxOverride("focus", BoxStyle(new Color(0.03f, 0.035f, 0.05f), new Color(0.45f, 0.6f, 0.85f)));
        return field;
    }

    public static StyleBoxFlat BoxStyle(Color color, Color? border = null)
    {
        var style = new StyleBoxFlat { BgColor = color, BorderColor = border ?? color.Lightened(0.15f) };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(4);
        style.ContentMarginLeft = 12f;
        style.ContentMarginRight = 12f;
        style.ContentMarginTop = 7f;
        style.ContentMarginBottom = 7f;
        return style;
    }

    public static VBoxContainer Column(int separation)
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", separation);
        return column;
    }

    public static HBoxContainer Row(int separation = 10)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", separation);
        return row;
    }

    public static Label Title(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 24);
        return label;
    }

    /// <summary>A paragraph that wraps at the width of a page of the menu.</summary>
    public static Label Text(string text) => new()
    {
        Text = text,
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
        CustomMinimumSize = new Vector2(480f, 0f),
    };

    /// <summary>
    /// The width a line of text needs to stand on one line, with the box round it if it has one.
    /// Ask once the label is in the tree, so that it has its font.
    /// </summary>
    public static float WidthOf(Label label)
    {
        var text = label.GetThemeFont("font").GetStringSize(label.Text, HorizontalAlignment.Left, -1f, label.GetThemeFontSize("font_size")).X;
        var box = label.GetThemeStylebox("normal");
        return Mathf.Ceil(text + box.GetMargin(Side.Left) + box.GetMargin(Side.Right));
    }

    /// <summary>
    /// Keeps a line of text no wider than a limit: on one line while it fits, and wrapped at the
    /// limit when it does not.
    /// </summary>
    /// <param name="label">The line.</param>
    /// <param name="width">The width it needs on one line, from <see cref="WidthOf"/>.</param>
    /// <param name="limit">The most it may have.</param>
    public static void FitWidth(Label label, float width, float limit)
    {
        var wraps = width > limit;
        var mode = wraps ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off;
        var least = new Vector2(wraps ? limit : 0f, 0f);
        if (label.AutowrapMode != mode)
        {
            label.AutowrapMode = mode;
        }

        if (label.CustomMinimumSize != least)
        {
            label.CustomMinimumSize = least;
        }
    }

    /// <summary>The box of a panel, with this much room between its edge and what is in it.</summary>
    public static StyleBoxFlat PanelStyle(float margin, int cornerRadius = 6)
    {
        var style = new StyleBoxFlat { BgColor = PanelColor };
        style.SetCornerRadiusAll(cornerRadius);
        style.SetContentMarginAll(margin);
        return style;
    }
}
