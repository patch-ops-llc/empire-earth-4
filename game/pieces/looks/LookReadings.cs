using Godot;

namespace Game;

/// <summary>
/// How far apart the looks are on the screen: for each two of them, the share of the larger
/// one's pixels at which the two show something else, taken at the heading where they are most
/// alike. The smoke test holds the closest two above a floor at the camera's starting distance,
/// by color and outline, and the closest two of different lines above another at the widest
/// zoom, by outline alone.
/// </summary>
/// <remarks>
/// It is a floor under what was judged by eye, not a judge: it does not know that a crest reads
/// better than a belt. It catches a shape file that is edited into the likeness of another, a
/// tone that is changed until two epochs wear the same, and a look made too small to count.
/// </remarks>
public static class LookReadings
{
    /// <summary>The headings the looks are compared at, evenly round the circle.</summary>
    public const int Headings = 8;

    /// <summary>
    /// The least share of a look's pixels at which it must differ from every other look, in
    /// color or outline, at the camera's starting distance.
    /// </summary>
    public const float NormalShare = 0.3f;

    /// <summary>
    /// The least share of a look's pixels at which its outline must differ from that of every
    /// look of another line, at the widest zoom.
    /// </summary>
    public const float WidestShare = 0.25f;

    /// <summary>The two looks that are most alike, among those a rule says to compare.</summary>
    /// <param name="looks">The looks.</param>
    /// <param name="pitch">The angle at which the camera looks down, in radians.</param>
    /// <param name="pixelsPerTile">The width of a tile on the screen.</param>
    /// <param name="growth">How many times its true size a look is drawn there, measured on the ground.</param>
    /// <param name="owner">The color of the owner of both, or null to compare outlines only.</param>
    /// <param name="compared">Which two looks are compared.</param>
    public static Closest? ClosestOf(
        IReadOnlyList<ChassisLook> looks,
        float pitch,
        float pixelsPerTile,
        float growth,
        Color? owner,
        Func<ChassisLook, ChassisLook, bool> compared)
    {
        Closest? closest = null;
        var pictures = new LookPicture[looks.Count];
        for (var turn = 0; turn < Headings; turn++)
        {
            var heading = Mathf.Tau * turn / Headings;
            for (var i = 0; i < looks.Count; i++)
            {
                pictures[i] = LookPicture.Of(looks[i].Shape, heading, pitch, pixelsPerTile, growth);
            }

            for (var a = 0; a < looks.Count; a++)
            {
                for (var b = a + 1; b < looks.Count; b++)
                {
                    if (!compared(looks[a], looks[b]))
                    {
                        continue;
                    }

                    var pixels = owner is { } color ? pictures[a].Differs(pictures[b], color) : pictures[a].OutlineDiffers(pictures[b]);
                    var share = pixels / (float)Math.Max(1, Math.Max(pictures[a].Covered, pictures[b].Covered));
                    if (closest is not { } before || share < before.Share)
                    {
                        closest = new Closest(looks[a], looks[b], turn, pixels, share);
                    }
                }
            }
        }

        return closest;
    }

    /// <summary>The width of a tile on the screen, at the point the camera looks at.</summary>
    /// <param name="distance">The camera's distance, in tiles.</param>
    /// <param name="fieldOfView">The camera's upright field of view, in degrees.</param>
    /// <param name="windowHeight">The height of the window, in pixels.</param>
    public static float PixelsPerTile(float distance, float fieldOfView, float windowHeight) =>
        windowHeight / (2f * distance * Mathf.Tan(Mathf.DegToRad(fieldOfView) / 2f));
}

/// <summary>Two looks and how far apart they are at the heading where they are most alike.</summary>
/// <param name="A">One look.</param>
/// <param name="B">The other.</param>
/// <param name="Turn">The heading, in eighths of a turn.</param>
/// <param name="Pixels">The number of pixels at which they differ.</param>
/// <param name="Share">That number as a share of the pixels the larger of the two covers.</param>
public readonly record struct Closest(ChassisLook A, ChassisLook B, int Turn, int Pixels, float Share)
{
    public override string ToString() => $"{A.Chassis.Id} and {B.Chassis.Id} differ at {Pixels} pixels, {Share:P0} of the larger";
}
