using Godot;
using Sim.Core;
using Sim.Data;

namespace Game;

/// <summary>
/// Draws the look a structure has by tier (design 2.5 and 3.2): on every finished structure
/// that shows a track, a turret with one storey for each tier its owner has in that track, in
/// the track's color. When the owner's research raises the track, every such structure gains a
/// storey in the same tick.
/// </summary>
/// <remarks>
/// <para>
/// What is drawn is read from <see cref="StructureLooks"/> after every tick, not from events, so
/// the layer shows whatever that seam lists, which is what the local player sees or remembers:
/// a structure of another player that the player has never seen carries no turret, and one the
/// player remembers carries the turret of the tier it had when last seen, until it is seen again.
/// </para>
/// <para>
/// A turret is shaded as the structure under it is, by <see cref="StructureLayer.ShadeOf"/>, so
/// the turret of a memory is as dim as the memory and dims at the same pace.
/// </para>
/// </remarks>
public partial class TierMarkLayer : WorldLayer
{
    private readonly StructureLayer _structures;
    private readonly List<StructureLook> _looks = [];
    private readonly Dictionary<EntityId, Mark> _marks = [];
    private readonly Dictionary<(int Structure, int Tier, StructureShade Shade), ArrayMesh> _meshes = [];
    private readonly List<EntityId> _gone = [];
    private Material _paint = null!;
    private int _pass;

    /// <param name="structures">The layer that draws the structures the turrets stand on.</param>
    public TierMarkLayer(StructureLayer structures) => _structures = structures;

    /// <summary>The number of structures whose tier is drawn.</summary>
    public int Count => _marks.Count;

    /// <summary>The tier drawn on a structure, or zero when none is.</summary>
    public int TierShown(EntityId structure) => _marks.TryGetValue(structure, out var mark) ? mark.Tier : 0;

    /// <summary>The shade the turret of a structure is drawn in, or null when it has none.</summary>
    public StructureShade? ShadeShown(EntityId structure) => _marks.TryGetValue(structure, out var mark) ? mark.Shade : null;

    protected override void Attach() =>
        _paint = new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Roughness = 1f };

    public override void AfterTick()
    {
        StructureLooks.Collect(View, _looks);
        _pass++;
        foreach (var look in _looks)
        {
            if (!look.IsFinished || look.LookTier < 1 || look.Def.Track is not { } track)
            {
                continue;
            }

            if (!_marks.TryGetValue(look.Entity, out var mark))
            {
                mark = new Mark(new MeshInstance3D { Name = $"Tier{look.Entity.Value}", MaterialOverride = _paint });
                AddChild(mark.Node);
                _marks.Add(look.Entity, mark);
            }

            var shade = ShadeOf(look.Entity);
            if (mark.Tier != look.LookTier || mark.Shade != shade)
            {
                mark.Tier = look.LookTier;
                mark.Shade = shade;
                mark.Node.Mesh = MeshOf(look.Def, track, look.LookTier, shade);
            }

            mark.Def = look.Def;
            mark.Track = track;
            mark.Node.Position = look.Middle;
            mark.Pass = _pass;
        }

        foreach (var (entity, mark) in _marks)
        {
            if (mark.Pass != _pass)
            {
                mark.Node.QueueFree();
                _gone.Add(entity);
            }
        }

        foreach (var entity in _gone)
        {
            _marks.Remove(entity);
        }

        _gone.Clear();
    }

    // A structure dims between ticks, and its turret with it.
    public override void EveryFrame(double delta)
    {
        foreach (var (entity, mark) in _marks)
        {
            var shade = ShadeOf(entity);
            if (mark.Shade != shade && mark.Def is { } def)
            {
                mark.Shade = shade;
                mark.Node.Mesh = MeshOf(def, mark.Track, mark.Tier, shade);
            }
        }
    }

    // The shade of the structure under a turret, in the few steps a mesh is kept for.
    private StructureShade ShadeOf(EntityId structure) => (_structures.ShadeOf(structure) ?? new StructureShade(0f)).Stepped();

    private ArrayMesh MeshOf(StructureDef def, int track, int tier, StructureShade shade)
    {
        var key = (def.Index, tier, shade);
        if (!_meshes.TryGetValue(key, out var mesh))
        {
            mesh = TierShapes.Turret(def, tier, TrackColors.Of(View.Match.Data, track), shade);
            _meshes.Add(key, mesh);
        }

        return mesh;
    }

    private sealed class Mark(MeshInstance3D node)
    {
        public MeshInstance3D Node { get; } = node;

        public StructureDef? Def { get; set; }

        public int Track { get; set; }

        public int Tier { get; set; }

        public StructureShade Shade { get; set; }

        public int Pass { get; set; }
    }
}
