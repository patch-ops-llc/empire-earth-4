using Godot;
using Sim.Core;
using Sim.Data;
using Sim.Numerics;
using Sim.Setup;

namespace Game;

/// <summary>
/// Draws a match as the local player sees it: the map's forest and cliff tiles, the fog of war
/// over them and the ground, and one node for every unit the player may see. A unit is an entity
/// that stands on the map and can move. It only reads the match. The nodes of units are created,
/// moved and removed here and nowhere else, so no entity has a script of its own.
/// </summary>
/// <remarks>
/// <para>
/// <b>The fog of war</b> (design 5.3). What may be drawn is asked of the view's
/// <see cref="PlayerSight"/>: the player's own units always, and another player's only while the
/// player sees it. A unit that goes out of sight loses its node, and so everything that follows
/// the drawn units, as the health bars and the pointer do, loses it with it. The ground, the
/// forest and the cliffs are shaded with the <see cref="Fog"/> picture.
/// </para>
/// <para>
/// The simulation moves in ticks and the screen in frames. After each tick <see cref="AfterTick"/>
/// records where everything stands now, and every frame a unit is drawn between where it stood
/// one tick ago and where it stands now, <see cref="TickFraction"/> of the way along.
/// </para>
/// <para>
/// Every other kind of thing in the world is a <see cref="WorldLayer"/>, a class of its own that
/// is called from here after every tick and in every frame. A piece that draws something new
/// adds a layer and leaves this file alone. An entity that stands on the map and cannot move, as
/// a structure or a deposit will be, is not drawn here: the layer of its kind draws it.
/// </para>
/// </remarks>
public partial class WorldView : Node3D
{
    private const string TerrainShader = "res://scenes/terrain_fog.gdshader";

    private readonly Dictionary<EntityId, UnitView> _units = [];
    private readonly List<EntityId> _gone = [];
    private readonly List<WorldLayer> _layers = [];
    private MatchView? _view;
    private PlayerSight? _sight;
    private FogPicture? _fog;
    private Material[] _playerMaterials = [];
    private Mesh? _unitMesh;
    private Match? _match;
    private int _pass;

    /// <summary>
    /// How far the current tick has gone, from 0 at its start to 1 at its end. Whoever advances the
    /// match sets it once per frame.
    /// </summary>
    public float TickFraction { get; set; } = 1f;

    /// <summary>The fog of war as a picture of the map, which everything on the ground is shaded with.</summary>
    public FogPicture Fog => _fog ?? throw new InvalidOperationException("The world view draws no match yet.");

    /// <summary>The number of units drawn.</summary>
    public int UnitCount => _units.Count;

    /// <summary>The units drawn, in no order.</summary>
    public IReadOnlyCollection<EntityId> Units => _units.Keys;

    /// <summary>Starts drawing a match. Call it once, before the match's first tick.</summary>
    public void Attach(MatchView view)
    {
        if (_match is not null)
        {
            throw new InvalidOperationException("The world view already draws a match.");
        }

        _view = view;
        _match = view.Match;
        _sight = view.Sight;
        _fog = new FogPicture(view.Sight);
        _unitMesh = PlaceholderMeshes.Unit();
        _playerMaterials = [.. Enumerable.Range(0, PlayerColors.Count).Select(player => (Material)PlaceholderMeshes.Flat(PlayerColors.Of(player)))];

        BuildTerrain(_match.Map);
        AfterTick();
    }

    /// <summary>
    /// Adds a layer: a kind of thing drawn in the world beside the units. From now on it is called
    /// after every tick and in every frame, after the layers added before it.
    /// </summary>
    public void AddLayer(WorldLayer layer)
    {
        if (_view is null)
        {
            throw new InvalidOperationException("A layer is added once the world view draws a match.");
        }

        if (string.IsNullOrEmpty(layer.Name))
        {
            layer.Name = layer.GetType().Name;
        }

        AddChild(layer);
        _layers.Add(layer);
        layer.Begin(_view);
    }

    /// <summary>
    /// Reads the match again. Call it after every tick: units that are new or have come into
    /// sight get a node, units that are gone or out of sight lose theirs, and the rest start
    /// gliding to where they stand now. Then every layer reads the match in its turn.
    /// </summary>
    public void AfterTick()
    {
        if (_match is null || _sight is null || _fog is null)
        {
            return;
        }

        _fog.AfterTick();
        _pass++;
        var bodies = _match.Bodies;
        for (var i = 0; i < bodies.Count; i++)
        {
            var entity = bodies.EntityAt(i);
            if (!_match.Movers.TryGet(entity, out var mover))
            {
                continue;
            }

            var body = bodies.ValueAt(i);
            if (body.Owner != _sight.Player && !_sight.ShowsUnit(entity))
            {
                continue;
            }

            var position = ToWorld(body.Position);
            var heading = ToHeading(mover.Facing);

            if (_units.TryGetValue(entity, out var unit))
            {
                unit.PreviousPosition = unit.Position;
                unit.PreviousHeading = unit.Heading;
            }
            else
            {
                unit = CreateUnit(entity, body.Owner);
                unit.PreviousPosition = position;
                unit.PreviousHeading = heading;
            }

            unit.Position = position;
            unit.Heading = heading;
            unit.Pass = _pass;
        }

        foreach (var (entity, unit) in _units)
        {
            if (unit.Pass != _pass)
            {
                unit.Node.QueueFree();
                _gone.Add(entity);
            }
        }

        foreach (var entity in _gone)
        {
            _units.Remove(entity);
        }

        _gone.Clear();

        foreach (var layer in _layers)
        {
            layer.AfterTick();
        }
    }

    /// <summary>
    /// Where a unit is drawn in this frame, which is where the player sees it. False for an
    /// entity that is not drawn as a unit.
    /// </summary>
    public bool TryGetDrawnPosition(EntityId entity, out Vector3 position)
    {
        if (_units.TryGetValue(entity, out var unit))
        {
            position = unit.Node.Position;
            return true;
        }

        position = default;
        return false;
    }

    /// <summary>
    /// Gives a drawn unit a shape and a material of its own in place of the stand-in. A layer
    /// that knows what a unit looks like calls it after a tick, so that a unit that is new on
    /// the screen has its look before its first frame. The node stays this view's: it is moved
    /// and removed as before, and a unit that is drawn again after it was out of sight starts
    /// with the stand-in again. False for an entity that is not drawn as a unit.
    /// </summary>
    public bool TryDress(EntityId entity, Mesh mesh, Material material)
    {
        if (!_units.TryGetValue(entity, out var unit))
        {
            return false;
        }

        unit.Node.Mesh = mesh;
        unit.Node.MaterialOverride = material;
        return true;
    }

    /// <summary>
    /// The shape and the material a drawn unit has now: the stand-in's, or those a layer gave it
    /// with <see cref="TryDress"/>. False for an entity that is not drawn as a unit.
    /// </summary>
    public bool TryGetDress(EntityId entity, out Mesh? mesh, out Material? material)
    {
        if (!_units.TryGetValue(entity, out var unit))
        {
            mesh = null;
            material = null;
            return false;
        }

        mesh = unit.Node.Mesh;
        material = unit.Node.MaterialOverride;
        return true;
    }

    public override void _Process(double delta)
    {
        _fog?.EveryFrame(delta);

        var fraction = Mathf.Clamp(TickFraction, 0f, 1f);
        foreach (var unit in _units.Values)
        {
            unit.Node.Position = unit.PreviousPosition.Lerp(unit.Position, fraction);
            unit.Node.Rotation = new Vector3(0f, Mathf.LerpAngle(unit.PreviousHeading, unit.Heading, fraction), 0f);
        }

        // The layers come after the units, so that what follows a unit is exactly on it.
        foreach (var layer in _layers)
        {
            layer.EveryFrame(delta);
        }
    }

    /// <summary>A point on the map as a point on the ground: map x is world X and map y is world Z.</summary>
    public static Vector3 ToWorld(FixedVector2 point) => new(ToFloat(point.X), 0f, ToFloat(point.Y));

    private static float ToFloat(Fixed value) => value.Raw / (float)Fixed.RawOne;

    // A facing is an angle in map coordinates, where y grows downward. The unit mesh points along
    // -Z, and turning it by this much about the vertical axis points it along the facing.
    private static float ToHeading(Angle facing)
    {
        var angle = facing.Units * Mathf.Tau / Angle.UnitsPerTurn;
        return Mathf.Atan2(-Mathf.Cos(angle), -Mathf.Sin(angle));
    }

    private UnitView CreateUnit(EntityId entity, int owner)
    {
        var node = new MeshInstance3D
        {
            Name = $"Entity{entity.Value}",
            Mesh = _unitMesh,
            MaterialOverride = _playerMaterials[owner % _playerMaterials.Length],
        };
        AddChild(node);

        var unit = new UnitView(node);
        _units.Add(entity, unit);
        return unit;
    }

    // Forest and cliff tiles block movement, so a player has to see them. These are stand-ins: a
    // tree on every forest tile and a block of rock on every cliff tile. Every tile of the map
    // has its shape from the start, and the fog's shader leaves out those the player has not
    // explored: the player learns the terrain as the units find it.
    private void BuildTerrain(MapDef map)
    {
        var trees = new List<Transform3D>();
        var rocks = new List<Transform3D>();

        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                // Both shapes have their center at half their height, so each is lifted to stand on the ground.
                var center = new Vector3(x + 0.5f, 0f, y + 0.5f);
                switch (map.TerrainAt(x, y))
                {
                    case TerrainKind.Forest:
                        // Trees of one size in rows look like a fence, so each is sized and turned by its tile.
                        var variation = TileVariation(x, y);
                        var scale = 0.85f + (0.3f * variation);
                        var basis = new Basis(Vector3.Up, variation * Mathf.Tau).Scaled(Vector3.One * scale);
                        trees.Add(new Transform3D(basis, center + (Vector3.Up * (PlaceholderMeshes.TreeHeight * scale / 2f))));
                        break;
                    case TerrainKind.Cliff:
                        rocks.Add(new Transform3D(Basis.Identity, center + (Vector3.Up * (PlaceholderMeshes.RockHeight / 2f))));
                        break;
                    default:
                        break;
                }
            }
        }

        AddTiles("Forest", PlaceholderMeshes.Tree(), PlaceholderMeshes.TreeColor, trees);
        AddTiles("Cliffs", PlaceholderMeshes.Rock(), PlaceholderMeshes.RockColor, rocks);
    }

    private void AddTiles(string name, Mesh mesh, Color color, List<Transform3D> transforms)
    {
        if (transforms.Count == 0 || _fog is null)
        {
            return;
        }

        var tiles = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = mesh,
            InstanceCount = transforms.Count,
        };
        for (var i = 0; i < transforms.Count; i++)
        {
            tiles.SetInstanceTransform(i, transforms[i]);
        }

        var material = new ShaderMaterial { Shader = GD.Load<Shader>(TerrainShader) };
        material.SetShaderParameter("color", color);
        _fog.Shade(material);
        AddChild(new MultiMeshInstance3D { Name = name, Multimesh = tiles, MaterialOverride = material });
    }

    // A number from 0 to 1 that depends only on the tile, so the forest looks the same every run.
    private static float TileVariation(int x, int y)
    {
        unchecked
        {
            var mixed = ((uint)x * 73856093u) ^ ((uint)y * 19349663u);
            mixed ^= mixed >> 13;
            mixed *= 1274126177u;
            mixed ^= mixed >> 16;
            return (mixed & 0xFFFF) / 65535f;
        }
    }

    private sealed class UnitView(MeshInstance3D node)
    {
        public MeshInstance3D Node { get; } = node;

        public Vector3 PreviousPosition { get; set; }

        public Vector3 Position { get; set; }

        public float PreviousHeading { get; set; }

        public float Heading { get; set; }

        public int Pass { get; set; }
    }
}
