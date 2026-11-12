using Godot;

namespace Game;

/// <summary>
/// What the minimap shows of structures and of territory (design 2.5 and 5.4): the ground each
/// player holds as a wash of the player's color with a stronger line along its edge, and every
/// structure as a square in its owner's color.
/// </summary>
/// <remarks>
/// <para>
/// The structures are those of <see cref="StructureLooks"/>, the one list of the structures the
/// local player is shown, so under the fog of war the minimap shows what that list holds and
/// nothing else. The territory is read with <see cref="TerritoryTiles"/>, as the layer on the
/// ground reads it, into a picture of a pixel for a tile that is made again only when a
/// territory comes, goes or changes.
/// </para>
/// <para>
/// The minimap's picture calls <see cref="DrawTerritory"/> over the terrain and
/// <see cref="DrawStructures"/> over the deposits, both under the shade of the fog and under
/// the units.
/// </para>
/// </remarks>
public sealed class MinimapStructures
{
    /// <summary>The least side of a structure's square, in pixels, so that a wall of one tile is found on a large map.</summary>
    public const float SmallestStructure = 3f;

    private const float WashAlpha = 0.26f;
    private const float EdgeAlpha = 0.9f;
    private const float UnfinishedAlpha = 0.45f;

    private static readonly Color StructureEdge = new(0.05f, 0.06f, 0.08f);

    private readonly TerritoryTiles _tiles = new();
    private readonly List<StructureLook> _looks = [];
    private ImageTexture? _territory;

    /// <summary>The number of structures drawn in the last picture.</summary>
    public int Drawn { get; private set; }

    /// <summary>The players whose territory is in the picture, as a bit each.</summary>
    public int Holders => _tiles.Players;

    /// <summary>The picture of the territory: a pixel for a tile, clear where nobody holds the ground. Null before the first drawing.</summary>
    public Texture2D? Territory => _territory;

    /// <summary>Draws the territory over the whole minimap.</summary>
    public void DrawTerritory(CanvasItem on, MatchView view, MinimapGeometry geometry)
    {
        if (_tiles.Refresh(view.Match) || _territory is null)
        {
            _territory = ImageTexture.CreateFromImage(TerritoryPicture(_tiles));
        }

        if (_tiles.Players != 0)
        {
            on.DrawTextureRect(_territory, new Rect2(Vector2.Zero, geometry.Size), tile: false);
        }
    }

    /// <summary>Draws the structures: every dark rim first and every square after, so that the walls of a line join.</summary>
    public void DrawStructures(CanvasItem on, MatchView view, MinimapGeometry geometry)
    {
        StructureLooks.Collect(view, _looks);
        Drawn = _looks.Count;
        foreach (var look in _looks)
        {
            on.DrawRect(SquareOf(look, geometry).Grow(1f), StructureEdge);
        }

        foreach (var look in _looks)
        {
            var color = PlayerColors.Of(look.Owner);
            on.DrawRect(SquareOf(look, geometry), look.IsFinished ? color : StructureEdge.Lerp(color, UnfinishedAlpha));
        }
    }

    /// <summary>The square a structure is drawn as: its footprint, or the least size round its middle.</summary>
    public static Rect2 SquareOf(in StructureLook look, MinimapGeometry geometry)
    {
        var middle = geometry.ToMinimap(new Vector2(look.X + (look.Size / 2f), look.Y + (look.Size / 2f)));
        var side = Math.Max(SmallestStructure, look.Size * geometry.PixelsPerTile);
        return new Rect2(middle - (Vector2.One * (side / 2f)), Vector2.One * side);
    }

    // A tile on the edge of a player's territory, one with a neighbor that is not the player's,
    // gets the strong color, the others the wash. Where territories lie over each other the
    // colors are mixed, and an edge wins over a wash.
    private static Image TerritoryPicture(TerritoryTiles tiles)
    {
        var image = Image.CreateEmpty(Math.Max(1, tiles.Width), Math.Max(1, tiles.Height), false, Image.Format.Rgba8);
        for (var y = 0; y < tiles.Height; y++)
        {
            for (var x = 0; x < tiles.Width; x++)
            {
                var holders = tiles.HoldersAt(x, y);
                if (holders == 0)
                {
                    continue;
                }

                var washes = 0;
                var edges = 0;
                var wash = Vector3.Zero;
                var edge = Vector3.Zero;
                for (var player = 0; player < TerritoryTiles.MaxPlayers; player++)
                {
                    if ((holders & (1 << player)) == 0)
                    {
                        continue;
                    }

                    var color = PlayerColors.Of(player);
                    var paint = new Vector3(color.R, color.G, color.B);
                    if (IsEdge(tiles, player, x, y))
                    {
                        edge += paint;
                        edges++;
                    }
                    else
                    {
                        wash += paint;
                        washes++;
                    }
                }

                var mixed = edges > 0 ? edge / edges : wash / washes;
                image.SetPixel(x, y, new Color(mixed.X, mixed.Y, mixed.Z, edges > 0 ? EdgeAlpha : WashAlpha));
            }
        }

        return image;
    }

    // The edge of the map is no edge of a territory: a line along it would say nothing.
    private static bool IsEdge(TerritoryTiles tiles, int player, int x, int y) =>
        Open(tiles, player, x - 1, y) || Open(tiles, player, x + 1, y) || Open(tiles, player, x, y - 1) || Open(tiles, player, x, y + 1);

    private static bool Open(TerritoryTiles tiles, int player, int x, int y) =>
        x >= 0 && y >= 0 && x < tiles.Width && y < tiles.Height && !tiles.Holds(player, x, y);
}
