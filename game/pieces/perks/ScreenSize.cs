using Godot;

namespace Game;

/// <summary>
/// How large a length of the world is on the screen, so that what a perk draws can be kept from
/// getting too small to see when the camera is far away: at the widest zoom a tile is a few
/// pixels.
/// </summary>
public static class ScreenSize
{
    /// <summary>The number of pixels of the window that one tile covers at a point of the world.</summary>
    public static float PixelsPerTile(Camera3D camera, Vector3 point)
    {
        // Along the line of sight, as the projection measures it, and not to the camera itself.
        var depth = Mathf.Max((point - camera.GlobalPosition).Dot(-camera.GlobalBasis.Z), 0.001f);
        var window = camera.GetTree().Root.Size.Y;
        return window / (2f * Mathf.Tan(Mathf.DegToRad(camera.Fov) / 2f) * depth);
    }

    /// <summary>A length in tiles, made longer where it would otherwise cover fewer than a number of pixels.</summary>
    public static float AtLeast(float tiles, float pixels, float pixelsPerTile) => Mathf.Max(tiles, pixels / pixelsPerTile);
}
