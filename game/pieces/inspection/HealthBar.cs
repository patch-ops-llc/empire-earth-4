using Godot;

namespace Game;

/// <summary>
/// Hit points as a bar in the inspection panel: green, yellow below half and red below a quarter.
/// </summary>
public partial class HealthBar : ProgressBar
{
    private static readonly Color Healthy = new(0.42f, 0.8f, 0.42f);
    private static readonly Color Hurt = new(0.93f, 0.78f, 0.3f);
    private static readonly Color NearlyGone = new(0.9f, 0.34f, 0.28f);

    // Which of the three colors the bar has now, or -1 before it has one.
    private int _band = -1;

    public HealthBar()
        : this(120f)
    {
    }

    public HealthBar(float width)
    {
        MinValue = 0.0;
        MaxValue = 1.0;
        Step = 0.0;
        ShowPercentage = false;
        CustomMinimumSize = new Vector2(width, 10f);
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeStyleboxOverride("background", ResearchColors.Bar(ResearchColors.Trough));
    }

    /// <summary>Shows hit points out of their maximum, both in thousandths.</summary>
    public void Present(long hitPoints, long maximum)
    {
        var share = maximum > 0 ? hitPoints / (double)maximum : 0.0;
        Value = share;
        var band = share >= 0.5 ? 0 : share >= 0.25 ? 1 : 2;
        if (band != _band)
        {
            _band = band;
            AddThemeStyleboxOverride("fill", ResearchColors.Bar(band == 0 ? Healthy : band == 1 ? Hurt : NearlyGone));
        }
    }
}
