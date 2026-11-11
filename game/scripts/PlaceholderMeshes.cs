using Godot;

namespace Game;

/// <summary>
/// The shapes that stand in for art until there is some: a unit, a tree and a block of rock. One
/// world unit is one tile.
/// </summary>
public static class PlaceholderMeshes
{
    public const float TreeHeight = 1.3f;
    public const float RockHeight = 0.8f;

    /// <summary>How tall the unit shape is.</summary>
    public const float UnitHeight = 0.75f;

    /// <summary>The distance from the unit shape's center to its nose, its farthest point.</summary>
    public const float UnitRadius = 0.42f;

    // The unit's outline seen from above, clockwise, with its nose toward -Z.
    private static readonly Vector2[] UnitOutline =
    [
        new(0f, -UnitRadius),
        new(0.26f, -0.08f),
        new(0.26f, 0.28f),
        new(-0.26f, 0.28f),
        new(-0.26f, -0.08f),
    ];

    /// <summary>A material of one color that takes light and does not shine.</summary>
    public static StandardMaterial3D Flat(Color color) => new() { AlbedoColor = color, Roughness = 1f };

    /// <summary>
    /// A block with a pointed front, so that the way a unit faces can be read from above. It stands
    /// on the ground, and it has no material: the owner's color is put on each unit.
    /// </summary>
    public static ArrayMesh Unit()
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);

        // The top, as a fan from the nose.
        tool.SetNormal(Vector3.Up);
        for (var i = 1; i < UnitOutline.Length - 1; i++)
        {
            tool.AddVertex(Top(0));
            tool.AddVertex(Top(i));
            tool.AddVertex(Top(i + 1));
        }

        // The sides. There is no underside, since nothing sees it.
        for (var i = 0; i < UnitOutline.Length; i++)
        {
            var next = (i + 1) % UnitOutline.Length;
            var along = UnitOutline[next] - UnitOutline[i];
            tool.SetNormal(new Vector3(along.Y, 0f, -along.X).Normalized());

            tool.AddVertex(Top(next));
            tool.AddVertex(Top(i));
            tool.AddVertex(Bottom(i));

            tool.AddVertex(Top(next));
            tool.AddVertex(Bottom(i));
            tool.AddVertex(Bottom(next));
        }

        return tool.Commit();

        static Vector3 Top(int corner) => new(UnitOutline[corner].X, UnitHeight, UnitOutline[corner].Y);
        static Vector3 Bottom(int corner) => new(UnitOutline[corner].X, 0f, UnitOutline[corner].Y);
    }

    /// <summary>
    /// A thin ring of one color that lies on the ground and takes no light, for marks the interface
    /// puts under and around things.
    /// </summary>
    public static TorusMesh Ring(float radius, Color color) => new()
    {
        InnerRadius = radius - 0.04f,
        OuterRadius = radius + 0.04f,
        Rings = 32,
        RingSegments = 6,
        Material = new StandardMaterial3D { AlbedoColor = color, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
    };

    /// <summary>A cone for a forest tile. Its center is at half of <see cref="TreeHeight"/>.</summary>
    public static CylinderMesh Tree() => new()
    {
        TopRadius = 0f,
        BottomRadius = 0.45f,
        Height = TreeHeight,
        RadialSegments = 7,
        Rings = 1,
        Material = Flat(new Color(0.11f, 0.27f, 0.13f)),
    };

    /// <summary>
    /// A block that fills a cliff tile, so that a row of them reads as a wall of rock. Its center is
    /// at half of <see cref="RockHeight"/>.
    /// </summary>
    public static BoxMesh Rock() => new()
    {
        Size = new Vector3(1f, RockHeight, 1f),
        Material = Flat(new Color(0.42f, 0.40f, 0.38f)),
    };
}
