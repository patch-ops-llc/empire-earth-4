using Godot;

namespace Game;

/// <summary>
/// An accent's badge as the interface draws it: a square in the track's color with one upright
/// mark for level I and two for level II, the same shape a unit carries in the world.
/// </summary>
public partial class AccentBadgeIcon : Control
{
    private const float Side = 16f;
    private readonly Color _color;
    private readonly int _level;

    public AccentBadgeIcon()
        : this(Colors.White, 1)
    {
    }

    public AccentBadgeIcon(Color color, int level)
    {
        _color = color;
        _level = level;
        CustomMinimumSize = new Vector2(Side, Side);
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        var box = new StyleBoxFlat { BgColor = _color, BorderColor = AccentBadges.Rim };
        box.SetCornerRadiusAll(3);
        box.SetBorderWidthAll(1);
        DrawStyleBox(box, new Rect2(Vector2.Zero, new Vector2(Side, Side)));

        // The marks stand where the badge in the world has them, as shares of its side.
        var marks = _level >= 2 ? 2 : 1;
        var width = Side * AccentBadges.MarkWidth;
        var height = Side * AccentBadges.MarkHeight;
        var between = Side * AccentBadges.MarkSpacing;
        var first = (Side / 2f) - ((marks - 1) * between / 2f);
        for (var mark = 0; mark < marks; mark++)
        {
            var middle = first + (mark * between);
            DrawRect(new Rect2(middle - (width / 2f), (Side - height) / 2f, width, height), AccentBadges.Mark);
        }
    }
}
