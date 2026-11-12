using Godot;

namespace Game;

/// <summary>
/// The shape of one chassis: flat triangles, each in one tone, built from the parts a shape file
/// under <c>game/assets/units/</c> lists. One tile is one unit, the unit stands on the ground at
/// the origin, and its front points toward -Z, as the stand-in shape's does.
/// </summary>
/// <remarks>
/// A part is a box, a cylinder or a ball, sized by the box it fills, then turned and put in its
/// place. A cylinder stands along Y, may narrow toward its top down to a point, and has as few
/// as three sides, so it is also a cone, a pyramid and a prism. Nothing has an underside that
/// faces the ground: the camera never sees it.
/// </remarks>
public sealed class LookShape
{
    private readonly List<LookTriangle> _triangles = [];

    /// <summary>The triangles of the shape. Seen from outside, the corners of each run clockwise.</summary>
    public IReadOnlyList<LookTriangle> Triangles => _triangles;

    /// <summary>The height of the shape's highest point above the ground.</summary>
    public float Height { get; private set; }

    /// <summary>The distance from the middle of the unit to the shape's farthest point, measured on the ground.</summary>
    public float Reach { get; private set; }

    /// <summary>The lowest point of the shape. A shape that stands on the ground has zero here.</summary>
    public float Lowest { get; private set; } = float.MaxValue;

    /// <summary>Adds a box of a size, turned about its middle and put with its middle at a place.</summary>
    public void AddBox(Vector3 size, Transform3D place, LookTone tone)
    {
        var half = size / 2f;
        Span<Vector3> corners = stackalloc Vector3[8];
        for (var i = 0; i < 8; i++)
        {
            corners[i] = new Vector3((i & 1) == 0 ? -half.X : half.X, (i & 2) == 0 ? -half.Y : half.Y, (i & 4) == 0 ? -half.Z : half.Z);
        }

        // The six faces, each by its four corners in order round the face.
        ReadOnlySpan<int> faces = [0, 1, 3, 2, 4, 5, 7, 6, 0, 1, 5, 4, 2, 3, 7, 6, 0, 2, 6, 4, 1, 3, 7, 5];
        for (var face = 0; face < 6; face++)
        {
            var a = corners[faces[face * 4]];
            var b = corners[faces[(face * 4) + 1]];
            var c = corners[faces[(face * 4) + 2]];
            var d = corners[faces[(face * 4) + 3]];
            var outward = (a + b + c + d) / 4f;
            Add(place, a, b, c, outward, tone);
            Add(place, a, c, d, outward, tone);
        }
    }

    /// <summary>
    /// Adds a cylinder that stands along Y and fills a box of a size, so it may be oval. Its top
    /// is <paramref name="top"/> times as wide as its foot: 1 for a cylinder, 0 for a cone.
    /// The spin, in radians, turns it about its own axis first, which matters when it has few sides.
    /// </summary>
    public void AddCylinder(Vector3 size, float top, int sides, float spin, Transform3D place, LookTone tone)
    {
        var half = size / 2f;
        var upper = new Vector3(0f, half.Y, 0f);
        var lower = new Vector3(0f, -half.Y, 0f);
        for (var side = 0; side < sides; side++)
        {
            var from = Round(side, sides, spin);
            var to = Round(side + 1, sides, spin);
            var footFrom = new Vector3(from.X * half.X, -half.Y, from.Y * half.Z);
            var footTo = new Vector3(to.X * half.X, -half.Y, to.Y * half.Z);
            var topFrom = new Vector3(footFrom.X * top, half.Y, footFrom.Z * top);
            var topTo = new Vector3(footTo.X * top, half.Y, footTo.Z * top);

            var outward = (footFrom + footTo) / 2f;
            Add(place, footFrom, footTo, topTo, outward, tone);
            Add(place, footFrom, lower, footTo, lower, tone);
            if (top > 0f)
            {
                Add(place, footFrom, topTo, topFrom, outward, tone);
                Add(place, topFrom, topTo, upper, upper, tone);
            }
        }
    }

    /// <summary>Adds a ball that fills a box of a size, so it may be an egg.</summary>
    public void AddBall(Vector3 size, int sides, float spin, Transform3D place, LookTone tone)
    {
        var half = size / 2f;
        var bands = Math.Max(2, sides / 2);
        for (var band = 0; band < bands; band++)
        {
            var low = Mathf.Pi * ((band / (float)bands) - 0.5f);
            var high = Mathf.Pi * (((band + 1) / (float)bands) - 0.5f);
            for (var side = 0; side < sides; side++)
            {
                var from = Round(side, sides, spin);
                var to = Round(side + 1, sides, spin);
                var a = OnBall(half, from, low);
                var b = OnBall(half, to, low);
                var c = OnBall(half, to, high);
                var d = OnBall(half, from, high);
                var outward = (a + b + c + d) / 4f;
                if (band > 0)
                {
                    Add(place, a, b, c, outward, tone);
                }

                if (band < bands - 1)
                {
                    Add(place, a, c, d, outward, tone);
                }
            }
        }
    }

    /// <summary>
    /// The shape as a mesh of one surface and no material. Each corner carries the number of
    /// its tone (<see cref="LookTone.Index"/>) as its first texture coordinate, and the shader
    /// of the looks has the colors by that number.
    /// </summary>
    public ArrayMesh ToMesh()
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var triangle in _triangles)
        {
            var tone = new Vector2(triangle.Tone.Index, 0f);
            AddCorner(tool, triangle.A, triangle.Normal, tone);
            AddCorner(tool, triangle.B, triangle.Normal, tone);
            AddCorner(tool, triangle.C, triangle.Normal, tone);
        }

        return tool.Commit();
    }

    private static void AddCorner(SurfaceTool tool, Vector3 corner, Vector3 normal, Vector2 tone)
    {
        tool.SetNormal(normal);
        tool.SetUV(tone);
        tool.AddVertex(corner);
    }

    // The corner of a side on a circle of radius 1 that lies in X and Z. The spin, in radians,
    // says where the first corner is: at 0 it is on +X, at a quarter turn on +Z.
    private static Vector2 Round(int side, int sides, float spin)
    {
        var angle = spin + (Mathf.Tau * side / sides);
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
    }

    private static Vector3 OnBall(Vector3 half, Vector2 round, float latitude) =>
        new(round.X * Mathf.Cos(latitude) * half.X, Mathf.Sin(latitude) * half.Y, round.Y * Mathf.Cos(latitude) * half.Z);

    // Adds one triangle of a part, given in the part's own space, where the part's middle is the
    // origin. Outward is any point on the outer side of the triangle's plane, which decides the
    // order of the corners.
    private void Add(Transform3D place, Vector3 a, Vector3 b, Vector3 c, Vector3 outward, LookTone tone)
    {
        var across = (b - a).Cross(c - a);
        if (across.LengthSquared() < 1e-12f)
        {
            return;
        }

        // Godot draws the side on which the corners run clockwise, which is the side the cross
        // product points away from.
        if (across.Dot(outward) > 0f)
        {
            (b, c) = (c, b);
            across = -across;
        }

        var normal = (place.Basis * -across).Normalized();
        var triangle = new LookTriangle(place * a, place * b, place * c, normal, tone);
        _triangles.Add(triangle);
        Measure(triangle.A);
        Measure(triangle.B);
        Measure(triangle.C);
    }

    private void Measure(Vector3 corner)
    {
        Height = Math.Max(Height, corner.Y);
        Lowest = Math.Min(Lowest, corner.Y);
        Reach = Math.Max(Reach, new Vector2(corner.X, corner.Z).Length());
    }
}

/// <summary>One flat triangle of a <see cref="LookShape"/>, with the direction it faces.</summary>
public readonly record struct LookTriangle(Vector3 A, Vector3 B, Vector3 C, Vector3 Normal, LookTone Tone);

/// <summary>
/// What a part of a shape is painted with: a share of its owner's color, or a color of its own.
/// The tones have names in <c>game/view-data/chassis-looks.jsonc</c>.
/// </summary>
/// <param name="Name">The name the shape files use.</param>
/// <param name="Index">The number of the tone among the tones of the looks file, from 0.</param>
/// <param name="Owner">True for a tone that is a share of the owner's color.</param>
/// <param name="Share">For an owner's tone, how much of the owner's light it keeps: 1 is the color itself, less is darker.</param>
/// <param name="Color">For any other tone, its color.</param>
public sealed record LookTone(string Name, int Index, bool Owner, float Share, Color Color)
{
    /// <summary>
    /// The tone as it is drawn for an owner of a color, before light. A share of the owner's
    /// color is taken of its light, not of its numbers, so that half is half as bright.
    /// </summary>
    public Color For(Color owner) => Owner ? Scaled(owner, Share, 0f) : Color;

    /// <summary>
    /// A color with a share of its light kept, and then a share of white light mixed in. The
    /// color is given and returned as the interface has it.
    /// </summary>
    public static Color Scaled(Color color, float kept, float white)
    {
        var light = color.SrgbToLinear();
        var mixed = new Color(
            Mathf.Lerp(light.R * kept, 1f, white),
            Mathf.Lerp(light.G * kept, 1f, white),
            Mathf.Lerp(light.B * kept, 1f, white));
        return mixed.LinearToSrgb();
    }
}
