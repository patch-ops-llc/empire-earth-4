using Godot;
using Sim.Core;
using Sim.Data;
using Sim.Numerics;
using Sim.Setup;

namespace Game;

/// <summary>
/// Draws a match: the map's forest and cliff tiles, and one node for every unit, which is an
/// entity that stands on the map and can move. It only reads the match. The nodes of units are
/// created, moved and removed here and nowhere else, so no entity has a script of its own.
/// </summary>
/// <remarks>
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
    private readonly Dictionary<EntityId, UnitView> _units = [];
    private readonly List<EntityId> _gone = [];
    private readonly List<WorldLayer> _layers = [];
    private MatchView? _view;
    private Material[] _playerMaterials = [];
    private Mesh? _unitMesh;
    private Match? _match;
    private int _pass;

    /// <summary>
    /// How far the current tick has gone, from 0 at its start to 1 at its end. Whoever advances the
    /// match sets it once per frame.
    /// </summary>
    public float TickFraction { get; set; } = 1f;

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
    /// Reads the match again. Call it after every tick: units that are new get a node, units that
    /// are gone lose theirs, and the rest start gliding to where they stand now. Then every layer
    /// reads the match in its turn.
    /// </summary>
    public void AfterTick()
    {
        if (_match is null)
        {
            return;
        }

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
            var span = FrameClock.Begin();
            layer.AfterTick();
            FrameClock.End("after tick", layer, span);
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

    private void EveryFrame(double delta)
    {
        var fraction = Mathf.Clamp(TickFraction, 0f, 1f);
        foreach (var unit in _units.Values)
        {
            unit.Node.Position = unit.PreviousPosition.Lerp(unit.Position, fraction);
            unit.Node.Rotation = new Vector3(0f, Mathf.LerpAngle(unit.PreviousHeading, unit.Heading, fraction), 0f);
        }

        // The layers come after the units, so that what follows a unit is exactly on it.
        foreach (var layer in _layers)
        {
            var span = FrameClock.Begin();
            layer.EveryFrame(delta);
            FrameClock.End("every frame", layer, span);
        }
    }

    public override void _Process(double delta)
    {
        var span = FrameClock.Begin();
        EveryFrame(delta);
        FrameClock.End("every frame: the world view itself", span);
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
    // tree on every forest tile and a block of rock on every cliff tile.
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

        AddTiles("Forest", PlaceholderMeshes.Tree(), trees);
        AddTiles("Cliffs", PlaceholderMeshes.Rock(), rocks);
    }

    private void AddTiles(string name, Mesh mesh, List<Transform3D> transforms)
    {
        if (transforms.Count == 0)
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

        AddChild(new MultiMeshInstance3D { Name = name, Multimesh = tiles });
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
