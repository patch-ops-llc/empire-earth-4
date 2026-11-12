using Godot;

namespace Game;

/// <summary>
/// The territories on the minimap (design 2.5 and 5.4): the ground each player holds as a wash
/// of the player's color, with a stronger line of it along the edge.
/// </summary>
/// <remarks>
/// <para>
/// It shows what the world shows. It reads the <see cref="TerritoryTiles"/> the layer on the
/// ground keeps and draws from, and never reads the match or fills the tiles itself. So what
/// that layer leaves out, as under the fog of war the territory of a player whose Capitol the
/// local player has not seen, is left out here too, and however the tiles come to be filled is
/// no matter of this class.
/// </para>
/// <para>
/// The picture is a pixel for a tile. <see cref="Read"/> compares the tiles with those it drew
/// last, once for every tick, and makes the picture again only when one differs. The minimap
/// draws it over the terrain and under the deposits, the shade of the fog and the units.
/// </para>
/// </remarks>
public sealed class MinimapTerritory
{
    private const float WashAlpha = 0.26f;
    private const float EdgeAlpha = 0.9f;

    private byte[] _holders = [];
    private int _width;
    private int _height;
    private long _readAt = -1;
    private TerritoryLayer? _layer;
    private ImageTexture? _picture;

    /// <summary>The players whose territory is in the picture, as a bit each.</summary>
    public int Holders { get; private set; }

    /// <summary>How many times the picture was made: once for every change of the territories.</summary>
    public int Pictures { get; private set; }

    /// <summary>Draws the territories over the whole minimap.</summary>
    public void Draw(CanvasItem on, MatchView view, MinimapGeometry geometry)
    {
        var tick = view.Match.World.Tick;
        if (tick != _readAt && TilesOf(view) is { } tiles)
        {
            _readAt = tick;
            Read(tiles);
        }

        if (_picture is not null && Holders != 0)
        {
            on.DrawTextureRect(_picture, new Rect2(Vector2.Zero, geometry.Size), tile: false);
        }
    }

    /// <summary>
    /// Takes the tiles as they are now. Returns true when they are not what was read last, and
    /// the picture was made again.
    /// </summary>
    public bool Read(TerritoryTiles tiles)
    {
        var same = _picture is not null && tiles.Width == _width && tiles.Height == _height;
        if (!same)
        {
            _width = tiles.Width;
            _height = tiles.Height;
            _holders = new byte[_width * _height];
        }

        var holders = 0;
        for (var y = 0; y < _height; y++)
        {
            for (var x = 0; x < _width; x++)
            {
                var now = (byte)tiles.HoldersAt(x, y);
                holders |= now;
                same &= _holders[(y * _width) + x] == now;
                _holders[(y * _width) + x] = now;
            }
        }

        if (same)
        {
            return false;
        }

        Holders = holders;
        Pictures++;
        var image = Picture();
        if (_picture is null || _picture.GetWidth() != image.GetWidth() || _picture.GetHeight() != image.GetHeight())
        {
            _picture = ImageTexture.CreateFromImage(image);
        }
        else
        {
            _picture.Update(image);
        }

        return true;
    }

    // The tiles of the layer that draws the territories in the world. Without that layer the
    // world shows none, and neither does the minimap.
    private TerritoryTiles? TilesOf(MatchView view)
    {
        if (_layer is null || !GodotObject.IsInstanceValid(_layer))
        {
            _layer = view.World.GetChildren().OfType<TerritoryLayer>().FirstOrDefault();
        }

        return _layer?.Tiles;
    }

    // A tile on the edge of a player's territory, one with a neighbor on the map that is not
    // the player's, gets the strong color and the others the wash. Where territories lie over
    // each other the colors are mixed, and an edge wins over a wash.
    private Image Picture()
    {
        var image = Image.CreateEmpty(Math.Max(1, _width), Math.Max(1, _height), false, Image.Format.Rgba8);
        for (var y = 0; y < _height; y++)
        {
            for (var x = 0; x < _width; x++)
            {
                var holders = _holders[(y * _width) + x];
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
                    if (IsEdge(player, x, y))
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
    private bool IsEdge(int player, int x, int y) =>
        Open(player, x - 1, y) || Open(player, x + 1, y) || Open(player, x, y - 1) || Open(player, x, y + 1);

    private bool Open(int player, int x, int y) =>
        x >= 0 && y >= 0 && x < _width && y < _height && (_holders[(y * _width) + x] & (1 << player)) == 0;
}
