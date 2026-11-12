using Godot;
using Sim.Vision;

namespace Game;

/// <summary>
/// The fog of war as a picture of the whole map, a pixel for a tile: the one texture that the
/// ground, the forest and the cliffs, and the minimap are all shaded with (design 5.3).
/// </summary>
/// <remarks>
/// <para>
/// A pixel is the color of the fog, and its alpha says how much of it lies on the tile: all of
/// it where the player has never seen, <see cref="ExploredShade"/> where the player has seen and
/// does not see now, and none where the player sees. Whatever is drawn under it shows through
/// by as much as is left.
/// </para>
/// <para>
/// The simulation works sight out again every few ticks, and a tile then changes at once. Here a
/// tile takes <see cref="FadeSeconds"/> to change, so that the edge of what a unit sees glides
/// with it instead of jumping a tile at a time. The picture is for the eye only.
/// </para>
/// </remarks>
public sealed class FogPicture
{
    /// <summary>The share of the fog that lies on ground the player has seen and does not see now.</summary>
    public const float ExploredShade = 0.5f;

    /// <summary>How long a tile takes to go from one state to another, in seconds.</summary>
    public const float FadeSeconds = 0.3f;

    /// <summary>The color of ground the player has never seen: a shade darker than the sky round the map.</summary>
    public static readonly Color Unknown = new(0.025f, 0.03f, 0.04f);

    private const int BytesPerPixel = 4;

    private readonly PlayerSight _sight;
    private readonly byte[] _states;
    private readonly float[] _wanted;
    private readonly float[] _shown;
    private readonly byte[] _pixels;
    private readonly Image _image;
    private readonly ImageTexture _texture;
    private long _revision;
    private bool _settled;

    public FogPicture(PlayerSight sight)
    {
        _sight = sight;
        var tiles = sight.Width * sight.Height;
        _states = new byte[tiles];
        _wanted = new float[tiles];
        _shown = new float[tiles];
        _pixels = new byte[tiles * BytesPerPixel];

        for (var tile = 0; tile < tiles; tile++)
        {
            _pixels[tile * BytesPerPixel] = (byte)Unknown.R8;
            _pixels[(tile * BytesPerPixel) + 1] = (byte)Unknown.G8;
            _pixels[(tile * BytesPerPixel) + 2] = (byte)Unknown.B8;
        }

        // The match begins with the fog as it is, not with the fog closing in.
        Read();
        Array.Copy(_wanted, _shown, tiles);
        WritePixels();
        _image = Image.CreateFromData(sight.Width, sight.Height, false, Image.Format.Rgba8, _pixels);
        _texture = ImageTexture.CreateFromImage(_image);
        _settled = true;
    }

    /// <summary>The picture. It is the same texture for the whole match, and its pixels change.</summary>
    public Texture2D Texture => _texture;

    /// <summary>The size of the map in tiles, which is the size of the picture in pixels.</summary>
    public Vector2 MapSize => new(_sight.Width, _sight.Height);

    /// <summary>The share of the fog on a tile as it is drawn in this frame, from 0 for none to 1 for all.</summary>
    public float ShownAt(int x, int y) => _shown[(y * _sight.Width) + x];

    /// <summary>
    /// Makes a material of one of the fog's shaders draw under this picture. The shaders are
    /// <c>ground_grid.gdshader</c> and <c>terrain_fog.gdshader</c>.
    /// </summary>
    public void Shade(ShaderMaterial material)
    {
        material.SetShaderParameter("fog", _texture);
        material.SetShaderParameter("map_size", MapSize);
    }

    /// <summary>Reads the fog again when it has changed. Call it after every tick.</summary>
    public void AfterTick()
    {
        if (_sight.Revision != _revision)
        {
            Read();
            _settled = false;
        }
    }

    /// <summary>Moves every tile that is changing a little further. Call it in every frame.</summary>
    /// <param name="delta">The seconds since the last frame.</param>
    public void EveryFrame(double delta)
    {
        if (_settled)
        {
            return;
        }

        var step = (float)delta / FadeSeconds;
        var settled = true;
        for (var tile = 0; tile < _shown.Length; tile++)
        {
            var shown = _shown[tile];
            var wanted = _wanted[tile];
            if (shown == wanted)
            {
                continue;
            }

            shown = Mathf.MoveToward(shown, wanted, step);
            _shown[tile] = shown;
            settled &= shown == wanted;
        }

        _settled = settled;
        WritePixels();
        _image.SetData(_sight.Width, _sight.Height, false, Image.Format.Rgba8, _pixels);
        _texture.Update(_image);
    }

    private void Read()
    {
        _revision = _sight.Revision;
        _sight.CopyFog(_states);
        for (var tile = 0; tile < _states.Length; tile++)
        {
            _wanted[tile] = (FogState)_states[tile] switch
            {
                FogState.Visible => 0f,
                FogState.Explored => ExploredShade,
                _ => 1f,
            };
        }
    }

    private void WritePixels()
    {
        for (var tile = 0; tile < _shown.Length; tile++)
        {
            _pixels[(tile * BytesPerPixel) + 3] = (byte)Mathf.RoundToInt(_shown[tile] * 255f);
        }
    }
}
