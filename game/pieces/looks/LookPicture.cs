using Godot;

namespace Game;

/// <summary>
/// A look as the camera draws it, worked out without the renderer: which tone each pixel of a
/// small square shows, for one heading of the unit and one size of a tile on the screen. It is
/// what the looks are measured with, so that a look that drifts toward another one, or one too
/// small to tell apart, fails the smoke test and not only the eye of whoever looks next.
/// </summary>
/// <remarks>
/// The camera looks down at a fixed angle and from far away next to the size of a unit, so the
/// picture takes its rays as parallel. Light is left out: a pixel has the tone of the part in
/// front. The middle of the picture is the point on the ground the unit stands on, moved down
/// by a third of the picture so that the unit, which rises from there, is inside.
/// </remarks>
public sealed class LookPicture
{
    // How far apart two colors must be, in red, green and blue from 0 to 1, to count as two.
    private const float ColorStep = 0.3f;

    private readonly LookTone?[] _tones;

    private LookPicture(int size, LookTone?[] tones, int covered)
    {
        Size = size;
        _tones = tones;
        Covered = covered;
    }

    /// <summary>The width and the height of the picture, in pixels.</summary>
    public int Size { get; }

    /// <summary>The number of pixels the look covers.</summary>
    public int Covered { get; }

    /// <summary>The tone a pixel shows, or null where the ground shows.</summary>
    public LookTone? At(int x, int y) => _tones[(y * Size) + x];

    /// <summary>The picture of a shape.</summary>
    /// <param name="shape">The shape.</param>
    /// <param name="heading">How far the unit is turned about the upright axis, in radians.</param>
    /// <param name="pitch">The angle at which the camera looks down, in radians.</param>
    /// <param name="pixelsPerTile">The width of a tile on the screen.</param>
    /// <param name="growth">How many times its true size the look is drawn, measured on the ground.</param>
    public static LookPicture Of(LookShape shape, float heading, float pitch, float pixelsPerTile, float growth)
    {
        // Room for the widest look at its largest, at any heading.
        var size = Mathf.CeilToInt(pixelsPerTile * 3.4f);
        var tones = new LookTone?[size * size];
        var nearest = new float[size * size];
        Array.Fill(nearest, float.MinValue);

        var turn = new Basis(Vector3.Up, heading);
        var grow = new Vector3(growth, 1f, growth);
        var up = new Vector3(0f, Mathf.Cos(pitch), -Mathf.Sin(pitch));
        var toCamera = new Vector3(0f, Mathf.Sin(pitch), Mathf.Cos(pitch));
        var middle = new Vector2(size / 2f, size * (2f / 3f));

        Vector3 OnScreen(Vector3 corner)
        {
            var point = turn * (corner * grow);
            return new Vector3(middle.X + (point.X * pixelsPerTile), middle.Y - (point.Dot(up) * pixelsPerTile), point.Dot(toCamera));
        }

        var covered = 0;
        foreach (var triangle in shape.Triangles)
        {
            var a = OnScreen(triangle.A);
            var b = OnScreen(triangle.B);
            var c = OnScreen(triangle.C);
            var area = Edge(a, b, c.X, c.Y);
            if (Mathf.Abs(area) < 1e-6f)
            {
                continue;
            }

            var left = Math.Max(0, Mathf.FloorToInt(Math.Min(a.X, Math.Min(b.X, c.X))));
            var right = Math.Min(size - 1, Mathf.CeilToInt(Math.Max(a.X, Math.Max(b.X, c.X))));
            var top = Math.Max(0, Mathf.FloorToInt(Math.Min(a.Y, Math.Min(b.Y, c.Y))));
            var bottom = Math.Min(size - 1, Mathf.CeilToInt(Math.Max(a.Y, Math.Max(b.Y, c.Y))));
            for (var y = top; y <= bottom; y++)
            {
                for (var x = left; x <= right; x++)
                {
                    // The middle of the pixel, by its shares of the three corners.
                    var shareA = Edge(b, c, x + 0.5f, y + 0.5f) / area;
                    var shareB = Edge(c, a, x + 0.5f, y + 0.5f) / area;
                    var shareC = 1f - shareA - shareB;
                    if (shareA < 0f || shareB < 0f || shareC < 0f)
                    {
                        continue;
                    }

                    var near = (shareA * a.Z) + (shareB * b.Z) + (shareC * c.Z);
                    var index = (y * size) + x;
                    if (near <= nearest[index])
                    {
                        continue;
                    }

                    if (tones[index] is null)
                    {
                        covered++;
                    }

                    nearest[index] = near;
                    tones[index] = triangle.Tone;
                }
            }
        }

        return new LookPicture(size, tones, covered);
    }

    /// <summary>
    /// The number of pixels at which this picture and another show something else: the look in
    /// one and the ground in the other, or two colors a step apart when both belong to an owner
    /// of a color.
    /// </summary>
    public int Differs(LookPicture other, Color owner)
    {
        var count = 0;
        for (var i = 0; i < _tones.Length; i++)
        {
            var mine = _tones[i];
            var theirs = other._tones[i];
            if (mine is null || theirs is null)
            {
                count += mine is null && theirs is null ? 0 : 1;
            }
            else if (!ReferenceEquals(mine, theirs) && Apart(mine.For(owner), theirs.For(owner)) >= ColorStep)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>The number of pixels that the look covers in one of this picture and another and not in both.</summary>
    public int OutlineDiffers(LookPicture other)
    {
        var count = 0;
        for (var i = 0; i < _tones.Length; i++)
        {
            count += _tones[i] is null == other._tones[i] is null ? 0 : 1;
        }

        return count;
    }

    private static float Apart(Color a, Color b) => new Vector3(a.R - b.R, a.G - b.G, a.B - b.B).Length();

    private static float Edge(Vector3 from, Vector3 to, float x, float y) =>
        ((to.X - from.X) * (y - from.Y)) - ((to.Y - from.Y) * (x - from.X));
}
