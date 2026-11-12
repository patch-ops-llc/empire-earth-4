using Godot;
using Sim.Core;
using Sim.Economy;
using Sim.Setup;
using Sim.Vision;

namespace Game;

/// <summary>
/// What of the match the local player may be shown (design 5.3): the fog of war as the view asks
/// about it. Everything under <c>game/</c> that draws a unit, a shot or an effect, or that turns a
/// click into a target, asks here first, so that the screen shows the player's own picture of
/// the match and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// The answers come from the simulation's <see cref="PlayerView"/>, which is <see cref="Seen"/>.
/// A player always sees their own units. Another player's unit is shown while the tile under its
/// center is visible, a shot of another player's while it is over a visible tile, and an event of
/// another player's when it is of a kind anyone sees and it happened on a visible tile.
/// </para>
/// <para>
/// <b>The development switch.</b> <see cref="FogOff"/> lifts the fog from the screen for a tester
/// who plays alone and has to see the other seat's units. It is not part of the game. It changes
/// what this class answers and nothing in the simulation, which has no such switch: the units
/// still pick and take as targets only what their player sees, whatever the screen shows.
/// <see cref="Seen"/> never takes the switch into account.
/// </para>
/// </remarks>
public sealed class PlayerSight
{
    private readonly Match _match;
    private bool _fogOff;
    private int _switched;

    public PlayerSight(Match match, int player)
    {
        _match = match;
        Player = player;
        Seen = new PlayerView(match, player);
    }

    /// <summary>The local player's number.</summary>
    public int Player { get; }

    /// <summary>The player's view of the match as the simulation gives it, fog and all.</summary>
    public PlayerView Seen { get; }

    public int Width => Seen.Width;

    public int Height => Seen.Height;

    /// <summary>
    /// DEVELOPMENT ONLY. True while the fog is lifted from the screen. The simulation goes on
    /// with the fog: see the remarks of the class.
    /// </summary>
    public bool FogOff
    {
        get => _fogOff;
        set
        {
            if (_fogOff != value)
            {
                _fogOff = value;
                _switched++;
            }
        }
    }

    /// <summary>A number that changes whenever the answer of <see cref="FogAt"/> may have changed for some tile.</summary>
    public long Revision => ((long)_switched << 32) | unchecked((uint)Seen.FogRevision);

    /// <summary>The state of a tile as it is to be shown. A tile off the map is unexplored.</summary>
    public FogState FogAt(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
        {
            return FogState.Unexplored;
        }

        return _fogOff ? FogState.Visible : Seen.FogAt(x, y);
    }

    /// <summary>The state of the tile under a point of the ground, given as map x and y in tiles.</summary>
    public FogState FogAt(Vector2 ground) => FogAt(Mathf.FloorToInt(ground.X), Mathf.FloorToInt(ground.Y));

    /// <summary>
    /// Writes the state of every tile as it is to be shown, row by row from the top: one
    /// <see cref="FogState"/> as a byte for each. The span must hold width times height bytes.
    /// </summary>
    public void CopyFog(Span<byte> states)
    {
        if (_fogOff)
        {
            states[..(Width * Height)].Fill((byte)FogState.Visible);
        }
        else
        {
            Seen.CopyFog(states);
        }
    }

    /// <summary>True when a unit is to be drawn: one of the player's own, or another player's that the player sees.</summary>
    public bool ShowsUnit(EntityId entity) => _fogOff ? _match.World.Entities.IsAlive(entity) : Seen.Sees(entity);

    /// <summary>
    /// True when the player truly sees a unit, whatever the development switch shows. This is the
    /// simulation's own test for the target of an attack order.
    /// </summary>
    public bool TrulySees(EntityId entity) => Seen.Sees(entity);

    /// <summary>True when a unit of this owner would be shown at a point of the ground, given as map x and y in tiles.</summary>
    public bool ShowsUnitAt(int owner, Vector2 ground) => owner == Player || FogAt(ground) == FogState.Visible;

    /// <summary>True when the player knows of a deposit: some tile under it is explored.</summary>
    public bool ShowsDeposit(in WorkSite deposit)
    {
        for (var y = deposit.Y; y < deposit.Y + deposit.Size; y++)
        {
            for (var x = deposit.X; x < deposit.X + deposit.Size; x++)
            {
                if (FogAt(x, y) != FogState.Unexplored)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>True when something that happened in the last tick is to be shown or told to the player.</summary>
    public bool Knows(in SimEvent happened) => _fogOff || Seen.Knows(in happened);

    /// <summary>
    /// Lists the shots in flight that are to be drawn, in ascending ID order: the player's own,
    /// and those of other players over tiles the player sees. The list is emptied first.
    /// </summary>
    public void CollectShots(List<SeenShot> shots)
    {
        if (!_fogOff)
        {
            Seen.CollectShots(shots);
            return;
        }

        shots.Clear();
        var store = _match.Shots;
        for (var i = 0; i < store.Count; i++)
        {
            var entity = store.EntityAt(i);
            if (_match.World.Entities.IsAlive(entity))
            {
                var shot = store.ValueAt(i);
                shots.Add(new SeenShot(entity, shot.Owner, shot.Position, shot.Line));
            }
        }
    }
}
