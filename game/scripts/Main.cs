using Godot;

namespace Game;

/// <summary>
/// Root of <c>scenes/main.tscn</c>, the scene the game starts in: a flat ground the size of the
/// map, the strategy camera and the debug overlay. There is no simulation behind it yet.
/// </summary>
public partial class Main : Node3D
{
    /// <summary>
    /// Passed after <c>--</c> on the command line, this makes the game run the smoke test and quit.
    /// Exported builds refuse a scene path on the command line, so this is how an export is tested.
    /// </summary>
    public const string SmokeTestArgument = "--smoke-test";

    /// <summary>Map size in tiles. A placeholder until the map comes from the data files.</summary>
    [Export]
    public Vector2I MapSizeTiles { get; set; } = new(96, 96);

    public override void _Ready()
    {
        var size = (Vector2)MapSizeTiles;

        // The map's corner is the world origin, so a tile's coordinates are its world position.
        var ground = GetNode<MeshInstance3D>("Ground");
        ground.Mesh = new PlaneMesh { Size = size };
        ground.Position = new Vector3(size.X / 2f, 0f, size.Y / 2f);

        GetNode<RtsCamera>("CameraRig").SetBounds(new Rect2(Vector2.Zero, size));

        if (OS.GetCmdlineUserArgs().Contains(SmokeTestArgument))
        {
            // Deferred, because a scene cannot be replaced while it is still being set up.
            GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, "res://scenes/smoke_test.tscn");
        }
    }
}
