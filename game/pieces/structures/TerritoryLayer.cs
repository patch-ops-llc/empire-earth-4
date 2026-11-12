using Godot;

namespace Game;

/// <summary>
/// Draws the ground each player holds (design 2.5 and 5.4): a faint wash of the player's color
/// over every tile of their territory and a line of it along the edge.
/// </summary>
/// <remarks>
/// It is drawn tile by tile from <see cref="TerritoryTiles"/>, so the line runs along the sides
/// of tiles and steps round a circle: a deposit on the inside of it pays and a structure on the
/// inside of it can be placed, and neither holds one tile further out. Where the territories of
/// two players lie over each other, both are drawn. Everything is one mesh, made again only
/// when a territory comes, goes or changes.
/// </remarks>
public partial class TerritoryLayer : WorldLayer
{
    // Above the ground by enough not to flicker against it, and under the rings and marks of the unit control.
    private const float WashLift = 0.02f;
    private const float EdgeLift = 0.03f;

    /// <summary>How wide the line along the edge is, in tiles. It lies inside the territory.</summary>
    public const float EdgeWidth = 0.12f;

    private const float WashAlpha = 0.10f;
    private const float EdgeAlpha = 0.8f;

    private MeshInstance3D _ground = null!;

    /// <summary>Who holds each tile, as this layer draws it.</summary>
    public TerritoryTiles Tiles { get; } = new();

    protected override void Attach()
    {
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        _ground = new MeshInstance3D
        {
            Name = "Territory",
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_ground);
    }

    public override void AfterTick()
    {
        if (Tiles.Refresh(View.Match))
        {
            _ground.Mesh = Build();
        }
    }

    private ArrayMesh? Build()
    {
        if (Tiles.Players == 0)
        {
            return null;
        }

        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        tool.SetNormal(Vector3.Up);

        // Every wash first and every line after, so that a line is drawn over the wash of a
        // neighbor whose territory lies over this one.
        for (var player = 0; player < TerritoryTiles.MaxPlayers; player++)
        {
            if ((Tiles.Players & (1 << player)) != 0)
            {
                AddWash(tool, player);
            }
        }

        for (var player = 0; player < TerritoryTiles.MaxPlayers; player++)
        {
            if ((Tiles.Players & (1 << player)) != 0)
            {
                AddEdge(tool, player);
            }
        }

        return tool.Commit();
    }

    // One rectangle for every run of the player's tiles in a row.
    private void AddWash(SurfaceTool tool, int player)
    {
        tool.SetColor(PlayerColors.Of(player) with { A = WashAlpha });
        for (var y = 0; y < Tiles.Height; y++)
        {
            var x = 0;
            while (x < Tiles.Width)
            {
                if (!Tiles.Holds(player, x, y))
                {
                    x++;
                    continue;
                }

                var from = x;
                while (x < Tiles.Width && Tiles.Holds(player, x, y))
                {
                    x++;
                }

                AddFlat(tool, from, y, x, y + 1, WashLift);
            }
        }
    }

    // A strip inside every side of a tile whose neighbor on that side is not the player's. The
    // strips along the rows run the whole side, those along the columns fit between them, and a
    // corner that turns inward gets a square of its own, so that the line has no gap and no
    // place where it lies on itself.
    private void AddEdge(SurfaceTool tool, int player)
    {
        var w = EdgeWidth;
        tool.SetColor(PlayerColors.Of(player) with { A = EdgeAlpha });
        for (var y = 0; y < Tiles.Height; y++)
        {
            for (var x = 0; x < Tiles.Width; x++)
            {
                if (!Tiles.Holds(player, x, y))
                {
                    continue;
                }

                var up = !Tiles.Holds(player, x, y - 1);
                var down = !Tiles.Holds(player, x, y + 1);
                var left = !Tiles.Holds(player, x - 1, y);
                var right = !Tiles.Holds(player, x + 1, y);

                if (up)
                {
                    AddFlat(tool, x, y, x + 1, y + w, EdgeLift);
                }

                if (down)
                {
                    AddFlat(tool, x, y + 1 - w, x + 1, y + 1, EdgeLift);
                }

                var top = y + (up ? w : 0f);
                var bottom = y + 1 - (down ? w : 0f);
                if (left)
                {
                    AddFlat(tool, x, top, x + w, bottom, EdgeLift);
                }

                if (right)
                {
                    AddFlat(tool, x + 1 - w, top, x + 1, bottom, EdgeLift);
                }

                if (!up && !left && !Tiles.Holds(player, x - 1, y - 1))
                {
                    AddFlat(tool, x, y, x + w, y + w, EdgeLift);
                }

                if (!up && !right && !Tiles.Holds(player, x + 1, y - 1))
                {
                    AddFlat(tool, x + 1 - w, y, x + 1, y + w, EdgeLift);
                }

                if (!down && !left && !Tiles.Holds(player, x - 1, y + 1))
                {
                    AddFlat(tool, x, y + 1 - w, x + w, y + 1, EdgeLift);
                }

                if (!down && !right && !Tiles.Holds(player, x + 1, y + 1))
                {
                    AddFlat(tool, x + 1 - w, y + 1 - w, x + 1, y + 1, EdgeLift);
                }
            }
        }
    }

    // A rectangle lying over the ground, seen from above. Map x and y are world x and z.
    private static void AddFlat(SurfaceTool tool, float fromX, float fromY, float toX, float toY, float lift)
    {
        tool.AddVertex(new Vector3(fromX, lift, fromY));
        tool.AddVertex(new Vector3(toX, lift, fromY));
        tool.AddVertex(new Vector3(toX, lift, toY));

        tool.AddVertex(new Vector3(fromX, lift, fromY));
        tool.AddVertex(new Vector3(toX, lift, toY));
        tool.AddVertex(new Vector3(fromX, lift, toY));
    }
}
