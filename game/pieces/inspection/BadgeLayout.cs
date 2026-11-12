namespace Game;

/// <summary>
/// How large the accent badges are drawn at one zoom, in whole pixels of the window, and which
/// of their two shapes they have. <see cref="AccentBadges"/> works it out once a frame and hands
/// it to the shader, so every badge on the screen has the same size.
/// </summary>
/// <remarks>
/// <para>
/// <b>The full badge.</b> A square in the track's color with a dark rim, and one upright white
/// mark for level I or two for level II. Its side is <see cref="AccentBadges.WorldSize"/> tiles,
/// kept between <see cref="AccentBadges.MinPixels"/> and <see cref="AccentBadges.MaxPixels"/>,
/// and a pixel less where the marks would otherwise not stand in the middle.
/// </para>
/// <para>
/// <b>The small badge.</b> At the smallest size a mark is one pixel wide, and two badges with
/// the room between them are wider than a tile once the camera is far: the badges of units that
/// stand side by side would cover each other. From there on (<see cref="Compact"/>) the level is
/// shown by the number of boxes and not by marks: one box for level I, two stacked for level II,
/// with no room between the two accents of a unit. A unit's badges are then never wider than
/// <see cref="AccentBadges.Fit"/> of a tile, nor taller than that share of the distance on the
/// screen between two units that stand a tile behind each other, down to
/// <see cref="AccentBadges.PipPixels"/> a box.
/// </para>
/// <para>
/// The limits in pixels are given for a window of the project's own height and grow with the
/// window, as the interface does.
/// </para>
/// </remarks>
/// <param name="Compact">True for the small badge: boxes that are counted, without marks.</param>
/// <param name="Width">The width of one box.</param>
/// <param name="Height">The height of one box. The full badge is square.</param>
/// <param name="Rim">The width of the dark rim.</param>
/// <param name="Step">From the left edge of a unit's first badge to the left edge of its second.</param>
/// <param name="Lift">How far the lower edge of a badge is above its anchor.</param>
/// <param name="MarkWidth">The width of a level mark. Zero for the small badge.</param>
/// <param name="MarkHeight">The height of a level mark.</param>
/// <param name="MarkSpacing">From the left edge of the first mark of level II to the left edge of the second.</param>
public readonly record struct BadgeLayout(bool Compact, int Width, int Height, int Rim, int Step, int Lift, int MarkWidth, int MarkHeight, int MarkSpacing)
{
    /// <summary>The width of the two badges of one unit together.</summary>
    public int PairWidth => Step + Width;

    /// <summary>The height of a badge of a level: the small badge of level II is two boxes that share a rim.</summary>
    public int HeightOf(int level) => Compact && level >= 2 ? (2 * Height) - Rim : Height;

    /// <summary>The layout at one zoom.</summary>
    /// <param name="pixelsPerTile">The width of a tile at the point the camera looks at, in pixels of the window.</param>
    /// <param name="pixelsPerRow">How far apart on the screen two points are that lie a tile behind each other there, in pixels of the window.</param>
    /// <param name="pixelsPerUnit">The pixels of the window to one unit of the interface: 1 on a window of the project's own size.</param>
    public static BadgeLayout For(float pixelsPerTile, float pixelsPerRow, float pixelsPerUnit)
    {
        var scale = Math.Max(pixelsPerUnit, 0.01f);
        var smallest = Whole(AccentBadges.MinPixels * scale);
        var largest = Math.Max(smallest, Whole(AccentBadges.MaxPixels * scale));
        var gap = Whole(AccentBadges.GapPixels * scale);
        var lift = Whole(AccentBadges.LiftPixels * scale);
        var raw = Math.Clamp(Whole(AccentBadges.WorldSize * pixelsPerTile), smallest, largest);

        if (raw == smallest && (2 * smallest) + gap > AccentBadges.Fit * pixelsPerTile)
        {
            var thin = Whole(scale);
            var least = Math.Min(smallest, Whole(AccentBadges.PipPixels * scale));

            // Two boxes share a rim, side by side for two accents and stacked for level II.
            var width = Math.Clamp((int)MathF.Floor(((AccentBadges.Fit * pixelsPerTile) + thin) / 2f), least, smallest);
            var height = Math.Clamp((int)MathF.Floor(((AccentBadges.Fit * pixelsPerRow) + thin) / 2f), least, width);
            return new BadgeLayout(Compact: true, width, height, thin, width - thin, lift, 0, 0, 0);
        }

        // The marks stand on whole pixels and in the middle of the badge, so the side of the
        // badge is odd where the mark's width is and even where it is even. The width of a
        // mark, the room between two and the room above and below them only grow as the camera
        // comes nearer: a mark that got thinner and wider in turns would flicker while the
        // player zooms. The height of a mark follows from the room above and below it, and may
        // lose a pixel where the badge gains one.
        var markWidth = Math.Max(1, (int)MathF.Ceiling((AccentBadges.MarkWidth * raw) - 0.3f));
        var side = (raw - markWidth) % 2 == 0 ? raw : raw > smallest ? raw - 1 : raw + 1;
        var rim = Whole(side / 9f);
        var markHeight = side - (2 * (side / 4));
        var spacing = Math.Max(2 * markWidth, 2 * Whole(AccentBadges.MarkSpacing * side / 2f));
        return new BadgeLayout(Compact: false, side, side, rim, side + gap, lift, markWidth, markHeight, spacing);
    }

    // To whole pixels, and never to nothing.
    private static int Whole(float pixels) => Math.Max(1, (int)MathF.Floor(pixels + 0.5f));
}
