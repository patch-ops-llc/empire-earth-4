using Godot;
using Sim.Core;
using Sim.Data;
using Sim.Numerics;
using Sim.Setup;

namespace Game;

/// <summary>
/// Draws a match: the map's forest and cliff tiles, and one node for every entity that stands on
/// the map. It only reads the match. Nodes are created, moved and removed here and nowhere else,
/// so no entity has a script of its own.
/// </summary>
/// <remarks>
/// The simulation moves in ticks and the screen in frames. After each tick <see cref="AfterTick"/>
/// records where everything stands now, and every frame an entity is drawn between where it stood
/// one tick ago and where it stands now, <see cref="TickFraction"/> of the way along.
/// </remarks>
public partial class WorldView : Node3D
{
    // One color per seat. A match has at most eight players.
    private static readonly Color[] PlayerColors =
    [
        new(0.20f, 0.45f, 0.95f),
        new(0.90f, 0.22f, 0.20f),
        new(0.95f, 0.80f, 0.20f),
        new(0.25f, 0.75f, 0.70f),
        new(0.65f, 0.35f, 0.85f),
        new(0.95f, 0.55f, 0.15f),
        new(0.90f, 0.90f, 0.90f),
        new(0.95f, 0.50f, 0.70f),
    ];

    private readonly Dictionary<EntityId, UnitView> _units = [];
    private readonly List<EntityId> _gone = [];
    private Material[] _playerMaterials = [];
    private Mesh? _unitMesh;
    private Match? _match;
    private int _pass;

    /// <summary>
    /// How far the current tick has gone, from 0 at its start to 1 at its end. Whoever advances the
    /// match sets it once per frame.
    /// </summary>
    public float TickFraction { get; set; } = 1f;

    /// <summary>The number of entities drawn.</summary>
    public int UnitCount => _units.Count;

    /// <summary>Starts drawing a match. Call it once, before the match's first tick.</summary>
    public void Attach(Match match)
    {
        if (_match is not null)
        {
            throw new InvalidOperationException("The world view already draws a match.");
        }

        _match = match;
        _unitMesh = PlaceholderMeshes.Unit();
        _playerMaterials = [.. PlayerColors.Select(color => (Material)PlaceholderMeshes.Flat(color))];

        BuildTerrain(match.Map);
        AfterTick();
    }

    /// <summary>
    /// Reads the match again. Call it after every tick: entities that are new get a node, entities
    /// that are gone lose theirs, and the rest start gliding to where they stand now.
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
            var body = bodies.ValueAt(i);
            var position = ToWorld(body.Position);
            var heading = _match.Movers.TryGet(entity, out var mover) ? ToHeading(mover.Facing) : 0f;

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
    }

    /// <summary>
    /// Where an entity is drawn in this frame, which is where the player sees it. False for an
    /// entity that is not drawn.
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

    public override void _Process(double delta)
    {
        var fraction = Mathf.Clamp(TickFraction, 0f, 1f);
        foreach (var unit in _units.Values)
        {
            unit.Node.Position = unit.PreviousPosition.Lerp(unit.Position, fraction);
            unit.Node.Rotation = new Vector3(0f, Mathf.LerpAngle(unit.PreviousHeading, unit.Heading, fraction), 0f);
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
