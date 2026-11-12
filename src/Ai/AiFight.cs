using Sim.Combat;
using Sim.Core;
using Sim.Data;
using Sim.Vision;

namespace Ai;

/// <summary>What the army is doing.</summary>
internal enum FightPhase
{
    /// <summary>It gathers at home, and attacks nobody.</summary>
    Gather,

    /// <summary>A force is on its way to the passage it goes through.</summary>
    Advance,

    /// <summary>A force is attacking.</summary>
    Attack,

    /// <summary>A force that lost too much is on its way home.</summary>
    Retreat,
}

/// <summary>
/// How it fights (design 5.5, item 5). Its army gathers at home. When the army is worth the
/// personality's ratio times its estimate of the enemy's, or it is at its supply cap, the units
/// at home become a force that goes through a passage of the map and attacks what it knows of
/// the enemy. A force that has lost the personality's share of its value goes home. Units at
/// home go to a structure of its own that is being damaged.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it knows.</b> Everything here is judged by the sight of its player. Its estimate of
/// the enemy's army is the value of the enemy units it has seen and has not seen die, and at
/// least what its personality assumes. What it attacks is a structure it sees or remembers, one
/// that holds territory before any other; while it knows of none, the starts of the other seats,
/// which are in the settings of the match; and after those the ground it has gone longest
/// without seeing.
/// </para>
/// <para>
/// <b>Orders.</b> A unit of the force with an enemy unit or a Tower near it is on an attack-move
/// and fights that first. With none near, and near the structure the force is after, it is
/// ordered to attack the structure, which an attack-move alone would not do once it has arrived.
/// A unit is given an order only when the one it has is not the one it should have.
/// </para>
/// </remarks>
internal sealed class AiFight
{
    /// <summary>A unit this many tiles from a tile, or nearer, has arrived there.</summary>
    public const int Near = 8;

    /// <summary>An enemy unit or Tower this many tiles from a unit of the force, or nearer, is fought before anything else.</summary>
    public const int NearThreat = 9;

    /// <summary>From this many tiles off, a unit with no enemy near it is ordered to attack the structure the force is after.</summary>
    public const int NearObjective = 12;

    /// <summary>A unit this many tiles from a point it was sent to look at, or nearer, is not sent there again.</summary>
    public const int AtPoint = 3;

    /// <summary>The ticks its units at home keep to a structure after it was last damaged.</summary>
    public const int DefendTicks = 10 * Sim.SimInfo.TicksPerSecond;

    private readonly AiSight _sight;
    private readonly AiSearch _search;
    private readonly AiFightDef _fight;
    private readonly AiRaid _raid;
    private readonly AiIntel _intel;
    private readonly AiResponses? _responses;
    private readonly bool _keepsIntel;
    private readonly List<SimEvent> _events = [];
    private readonly Dictionary<int, int> _force = [];
    private readonly Dictionary<int, long> _hitPoints = [];
    private readonly List<OwnFighter> _forceUnits = [];
    private readonly List<OwnFighter> _homeUnits = [];
    private readonly List<(EntityId Entity, int X, int Y)> _threats = [];
    private readonly List<(int Threat, EntityId Unit)> _toThreats = [];
    private readonly List<EntityId> _toPoint = [];
    private readonly List<EntityId> _toStructure = [];
    private readonly List<EntityId> _group = [];
    private readonly bool[] _startSeen;
    private AiReaction _attackReaction;
    private AiReaction _retreatReaction;
    private AiReaction _reinforceReaction;
    private AiReaction _defendReaction;
    private long _forceWhole;
    private int _phaseDeadline;
    private int _searchDeadline;
    private int _stagingX;
    private int _stagingY;
    private int _defendX;
    private int _defendY;
    private int _defendUntil;
    private Objective _objective;

    /// <param name="sight">What its player knows.</param>
    /// <param name="search">The ground it has seen, and when.</param>
    /// <param name="fight">The numbers of its personality.</param>
    /// <param name="intel">
    /// Its memory of the enemy, kept up to date by whoever made it. Without one it keeps its own:
    /// <see cref="Watch"/> and <see cref="Think"/> then bring it up to date.
    /// </param>
    /// <param name="responses">The response table, which may name another passage than its personality's, or none.</param>
    public AiFight(AiSight sight, AiSearch search, AiFightDef fight, AiIntel? intel = null, AiResponses? responses = null)
    {
        ArgumentNullException.ThrowIfNull(sight);
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(fight);
        _sight = sight;
        _search = search;
        _fight = fight;
        _keepsIntel = intel is null;
        _intel = intel ?? new AiIntel(sight);
        _responses = responses;
        _raid = new AiRaid(sight, fight);
        _startSeen = new bool[sight.OtherStarts.Count];
    }

    private enum ObjectiveKind
    {
        None,
        Structure,
        Start,
        Search,
    }

    public FightPhase Phase { get; private set; }

    public AiRaid Raid => _raid;

    /// <summary>The tick in which it first sent a force to attack, or -1.</summary>
    public int FirstAttackTick { get; private set; } = -1;

    /// <summary>The times it sent a force to attack.</summary>
    public int Attacks { get; private set; }

    /// <summary>The times a force of its went home after its losses.</summary>
    public int Retreats { get; private set; }

    /// <summary>The value of all its fighting units, as the last round found it.</summary>
    public long ArmyValue { get; private set; }

    /// <summary>What it takes the enemy's army to be worth.</summary>
    public long EnemyEstimate => Math.Max(_intel.EnemyValue, _fight.AssumedEnemyValue);

    /// <summary>The kind of map marker its army goes through now: the one a row of the response table names, or its personality's.</summary>
    public MarkerKind Passage => _responses?.Passage ?? _fight.Passage;

    /// <summary>True for a unit that is with a force or a raiding party, and so not at home.</summary>
    public bool IsAway(EntityId unit) => _force.ContainsKey(unit.Value) || _raid.Has(unit);

    /// <summary>True while its units at home keep to a structure that was damaged.</summary>
    public bool IsDefending { get; private set; }

    /// <summary>The units of the force that are alive, as the last round found them.</summary>
    public int ForceCount => _forceUnits.Count;

    /// <summary>
    /// Reads what happened in the tick that has just run, when it keeps its own memory of the
    /// enemy. Call it after every tick: an enemy unit it saw die is taken out of its estimate
    /// here, and the events of a tick are gone after the next.
    /// </summary>
    public void Watch()
    {
        if (_keepsIntel)
        {
            _intel.Watch();
        }
    }

    /// <param name="hasMuster">False when it has no tile to gather on, in which case it only keeps its estimate.</param>
    /// <param name="musterX">The tile its army gathers on.</param>
    /// <param name="musterY">The tile its army gathers on.</param>
    /// <param name="atSupplyCap">True when its army is as large as its supply lets it be.</param>
    /// <param name="commands">The list its orders are added to.</param>
    public void Think(bool hasMuster, int musterX, int musterY, bool atSupplyCap, List<Command> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        if (_keepsIntel)
        {
            _intel.Update();
        }

        SortUnits();
        NoteDamage();
        if (!hasMuster)
        {
            return;
        }

        var mayAttack = !_fight.IsNone && !IsDefending && (atSupplyCap || _fight.IsEnoughToAttack(ArmyValue, EnemyEstimate));
        var setsOut = Phase == FightPhase.Gather
            && _attackReaction.Holds(mayAttack && _homeUnits.Count > 0, _sight.Tick, _fight.ReactionTicks);
        var joins = Phase == FightPhase.Attack
            && _reinforceReaction.Holds(mayAttack && _homeUnits.Count >= _fight.ReinforceUnits, _sight.Tick, _fight.ReactionTicks);
        _raid.Think(_homeUnits, setsOut || joins, IsDefending, musterX, musterY, commands);
        switch (Phase)
        {
            case FightPhase.Gather:
                if (setsOut)
                {
                    Launch(musterX, musterY, commands);
                }

                break;

            case FightPhase.Advance:
            case FightPhase.Attack:
                if (_forceUnits.Count == 0)
                {
                    Dissolve();
                }
                else if (_retreatReaction.Holds(_fight.IsTooMuchLost(_forceWhole - LivingValue(), _forceWhole), _sight.Tick, _fight.ReactionTicks))
                {
                    Phase = FightPhase.Retreat;
                    Retreats++;
                    _phaseDeadline = DeadlineTo(_forceUnits, musterX, musterY);
                    SendAll(CommandType.Move, _forceUnits, musterX, musterY, commands);
                }
                else if (Phase == FightPhase.Advance)
                {
                    Advance(commands);
                }
                else
                {
                    if (joins)
                    {
                        Join();
                    }

                    Attack(commands);
                }

                break;

            case FightPhase.Retreat:
                if (_forceUnits.Count == 0
                    || 3 * AiOrders.CountNear(_forceUnits, musterX, musterY, Near) >= 2 * _forceUnits.Count
                    || _sight.Tick >= _phaseDeadline)
                {
                    Dissolve();
                }
                else
                {
                    SendIdle(CommandType.Move, _forceUnits, musterX, musterY, commands);
                }

                break;
        }

        KeepHome(musterX, musterY, commands);
    }

    private void SortUnits()
    {
        _forceUnits.Clear();
        _homeUnits.Clear();
        _raid.Units.Clear();
        ArmyValue = 0;
        foreach (var unit in _sight.Fighters)
        {
            ArmyValue += _sight.ValueOf(unit.Chassis);
            if (unit.Entity == _sight.Scout)
            {
                // Away to look, and under the orders of the scouting alone.
            }
            else if (_force.ContainsKey(unit.Entity.Value))
            {
                _forceUnits.Add(unit);
            }
            else if (_raid.Has(unit.Entity))
            {
                _raid.Units.Add(unit);
            }
            else
            {
                _homeUnits.Add(unit);
            }
        }
    }

    /// <summary>Finds a structure of its own that has fewer hit points than a round ago.</summary>
    private void NoteDamage()
    {
        var found = false;
        foreach (var structure in _sight.Structures)
        {
            if (!found && _hitPoints.TryGetValue(structure.Entity.Value, out var before) && structure.HitPoints < before)
            {
                found = true;
                _defendX = structure.X + (structure.Size / 2);
                _defendY = structure.Y + (structure.Size / 2);
                _defendUntil = _sight.Tick + DefendTicks;
            }
        }

        _hitPoints.Clear();
        foreach (var structure in _sight.Structures)
        {
            _hitPoints[structure.Entity.Value] = structure.HitPoints;
        }

        IsDefending = _defendReaction.Holds(_sight.Tick < _defendUntil, _sight.Tick, _fight.ReactionTicks);
    }

    private long LivingValue()
    {
        long value = 0;
        foreach (var unit in _forceUnits)
        {
            value += _force[unit.Entity.Value];
        }

        return value;
    }

    /// <summary>Makes the units at home a force, and sends it to its passage, or at the enemy when no passage is on the way.</summary>
    private void Launch(int musterX, int musterY, List<Command> commands)
    {
        _force.Clear();
        _forceWhole = 0;
        Join();
        Attacks++;
        if (FirstAttackTick < 0)
        {
            FirstAttackTick = _sight.Tick;
        }

        _objective = default;
        _retreatReaction.Reset();
        _reinforceReaction.Reset();
        ChooseObjective(musterX, musterY);
        if (_objective.Kind != ObjectiveKind.None
            && AiOrders.TryFindPassage(_sight, Passage, musterX, musterY, _objective.X, _objective.Y, out _stagingX, out _stagingY))
        {
            Phase = FightPhase.Advance;
            _phaseDeadline = DeadlineTo(_forceUnits, _stagingX, _stagingY);
            SendAll(CommandType.AttackMove, _forceUnits, _stagingX, _stagingY, commands);
        }
        else
        {
            Phase = FightPhase.Attack;
            Attack(commands);
        }
    }

    /// <summary>Adds the units at home to the force.</summary>
    private void Join()
    {
        foreach (var unit in _homeUnits)
        {
            var value = _sight.ValueOf(unit.Chassis);
            _force[unit.Entity.Value] = value;
            _forceWhole += value;
            _forceUnits.Add(unit);
        }

        _homeUnits.Clear();
    }

    private void Dissolve()
    {
        _force.Clear();
        _forceWhole = 0;
        _forceUnits.Clear();
        _attackReaction.Reset();
        Phase = FightPhase.Gather;
    }

    /// <summary>On the way to the passage: the force attacks from there when two thirds of it have arrived.</summary>
    private void Advance(List<Command> commands)
    {
        if (3 * AiOrders.CountNear(_forceUnits, _stagingX, _stagingY, Near) >= 2 * _forceUnits.Count || _sight.Tick >= _phaseDeadline)
        {
            Phase = FightPhase.Attack;
            _objective = default;
            Attack(commands);
            return;
        }

        _group.Clear();
        foreach (var unit in _forceUnits)
        {
            if (unit.Order == UnitOrder.Idle
                && AiSearch.DistanceSquared(AiOrders.TileX(unit), AiOrders.TileY(unit), _stagingX, _stagingY) > Near * Near)
            {
                _group.Add(unit.Entity);
            }
        }

        AiOrders.Send(CommandType.AttackMove, _sight.Player, _group, _stagingX, _stagingY, commands);
    }

    private void Attack(List<Command> commands)
    {
        var (middleX, middleY) = AiOrders.MiddleOf(_forceUnits);
        var changed = ChooseObjective(middleX, middleY);

        _threats.Clear();
        foreach (var unit in _sight.Enemies)
        {
            if (!unit.IsWorker && unit.Chassis != SeenUnit.NoChassis)
            {
                _threats.Add((unit.Entity, unit.Position.X.FloorToInt(), unit.Position.Y.FloorToInt()));
            }
        }

        foreach (var structure in _sight.EnemyStructures)
        {
            if (structure.IsFinished && _sight.Data.StructureOf(structure.Type)?.Attack is not null)
            {
                _threats.Add((structure.Entity, structure.X + (structure.Size / 2), structure.Y + (structure.Size / 2)));
            }
        }

        _toThreats.Clear();
        _toPoint.Clear();
        _toStructure.Clear();
        foreach (var unit in _forceUnits)
        {
            var x = AiOrders.TileX(unit);
            var y = AiOrders.TileY(unit);
            var isAtStructure = _objective.Kind == ObjectiveKind.Structure
                && unit.Order == UnitOrder.Attack
                && unit.Target == _objective.Structure;
            var threat = NearestThreat(x, y);
            if (threat >= 0)
            {
                // On an attack-move it picks what is near by itself.
                if (unit.Order != UnitOrder.AttackMove)
                {
                    _toThreats.Add((threat, unit.Entity));
                }

                continue;
            }

            if (_objective.Kind == ObjectiveKind.None)
            {
                continue;
            }

            var distance = AiSearch.DistanceToSquare(_objective.TileX, _objective.TileY, _objective.Size, x, y);
            if (_objective.Kind == ObjectiveKind.Structure && distance <= NearObjective * NearObjective)
            {
                // One that is fighting something on its attack-move finishes that first.
                if (!isAtStructure && !(unit.Order == UnitOrder.AttackMove && unit.Target != EntityId.None))
                {
                    _toStructure.Add(unit.Entity);
                }
            }
            else if (unit.Order == UnitOrder.AttackMove ? changed && unit.Target == EntityId.None : !isAtStructure && distance > AtPoint * AtPoint)
            {
                _toPoint.Add(unit.Entity);
            }
        }

        AiOrders.Send(CommandType.AttackMove, _sight.Player, _toPoint, _objective.X, _objective.Y, commands);
        AiOrders.Attack(_sight.Player, _toStructure, _objective.Structure, commands);

        _toThreats.Sort(static (left, right) =>
            left.Threat != right.Threat ? left.Threat.CompareTo(right.Threat) : left.Unit.CompareTo(right.Unit));
        for (var i = 0; i < _toThreats.Count;)
        {
            var threat = _toThreats[i].Threat;
            _group.Clear();
            for (; i < _toThreats.Count && _toThreats[i].Threat == threat; i++)
            {
                _group.Add(_toThreats[i].Unit);
            }

            AiOrders.Send(CommandType.AttackMove, _sight.Player, _group, _threats[threat].X, _threats[threat].Y, commands);
        }
    }

    /// <summary>The nearest of the round's threats within <see cref="NearThreat"/> tiles of a tile, as a position in the list, or -1.</summary>
    private int NearestThreat(int x, int y)
    {
        var best = -1;
        long bestDistance = (NearThreat * NearThreat) + 1;
        for (var i = 0; i < _threats.Count; i++)
        {
            var distance = AiSearch.DistanceSquared(_threats[i].X, _threats[i].Y, x, y);
            if (distance < bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>
    /// Settles what the force is after, and says whether that changed: the structure it was
    /// after while the player still knows of it, else the nearest structure it knows of that
    /// holds territory, else the nearest other, else the nearest start of another seat it has
    /// not looked at, else the ground it has gone longest without seeing.
    /// </summary>
    private bool ChooseObjective(int fromX, int fromY)
    {
        SeenStructure? best = null;
        var bestHolds = false;
        var bestDistance = long.MaxValue;
        var currentKnown = false;
        var currentHolds = false;
        foreach (var structure in _sight.EnemyStructures)
        {
            if (_sight.Data.StructureOf(structure.Type) is not { Wall: null } def)
            {
                continue;
            }

            var holds = def.TerritoryRadius is not null;
            if (_objective.Kind == ObjectiveKind.Structure && structure.Entity == _objective.Structure)
            {
                currentKnown = true;
                currentHolds = holds;
            }

            var distance = AiSearch.DistanceToSquare(structure.X, structure.Y, structure.Size, fromX, fromY);
            if (best is null || (holds && !bestHolds) || (holds == bestHolds && distance < bestDistance))
            {
                best = structure;
                bestHolds = holds;
                bestDistance = distance;
            }
        }

        if (currentKnown && (currentHolds || !bestHolds))
        {
            return false;
        }

        if (best is { } found)
        {
            _objective = new Objective(ObjectiveKind.Structure, found.Entity, found.X, found.Y, found.Size, 0);
            return true;
        }

        for (var i = 0; i < _startSeen.Length; i++)
        {
            _startSeen[i] |= _sight.IsVisible(_sight.OtherStarts[i].X, _sight.OtherStarts[i].Y);
        }

        if (_objective.Kind == ObjectiveKind.Start && !_startSeen[_objective.Index])
        {
            return false;
        }

        var start = -1;
        for (var i = 0; i < _startSeen.Length; i++)
        {
            var distance = AiSearch.DistanceSquared(_sight.OtherStarts[i].X, _sight.OtherStarts[i].Y, fromX, fromY);
            if (!_startSeen[i] && (start < 0 || distance < bestDistance))
            {
                start = i;
                bestDistance = distance;
            }
        }

        if (start >= 0)
        {
            _objective = new Objective(ObjectiveKind.Start, EntityId.None, _sight.OtherStarts[start].X, _sight.OtherStarts[start].Y, 1, start);
            return true;
        }

        if (_objective.Kind == ObjectiveKind.Search && !_sight.IsVisible(_objective.TileX, _objective.TileY))
        {
            if (_sight.Tick < _searchDeadline)
            {
                return false;
            }

            _search.GiveUp(_objective.TileX, _objective.TileY);
        }

        var before = _objective;
        if (_search.TryFindLongestUnseen(fromX, fromY, out var x, out var y))
        {
            _objective = new Objective(ObjectiveKind.Search, EntityId.None, x, y, 1, 0);
            _searchDeadline = AiSearch.DeadlineFor(_sight.Tick, AiSearch.DistanceSquared(x, y, fromX, fromY));
        }
        else
        {
            _objective = default;
        }

        return _objective != before;
    }

    /// <summary>
    /// The units at home: to a structure of its own that is being damaged, and otherwise back
    /// to the tile the army gathers on when they stand idle away from it.
    /// </summary>
    private void KeepHome(int musterX, int musterY, List<Command> commands)
    {
        _group.Clear();
        if (IsDefending)
        {
            foreach (var unit in _homeUnits)
            {
                if (unit.Order is UnitOrder.Idle or UnitOrder.Move
                    && AiSearch.DistanceSquared(AiOrders.TileX(unit), AiOrders.TileY(unit), _defendX, _defendY) > AtPoint * AtPoint)
                {
                    _group.Add(unit.Entity);
                }
            }

            AiOrders.Send(CommandType.AttackMove, _sight.Player, _group, _defendX, _defendY, commands);
            return;
        }

        SendIdle(CommandType.Move, _homeUnits, musterX, musterY, commands);
    }

    private int DeadlineTo(List<OwnFighter> units, int x, int y)
    {
        long furthest = 0;
        foreach (var unit in units)
        {
            furthest = Math.Max(furthest, AiSearch.DistanceSquared(AiOrders.TileX(unit), AiOrders.TileY(unit), x, y));
        }

        return AiSearch.DeadlineFor(_sight.Tick, furthest);
    }

    private void SendAll(CommandType type, List<OwnFighter> units, int x, int y, List<Command> commands)
    {
        _group.Clear();
        foreach (var unit in units)
        {
            _group.Add(unit.Entity);
        }

        AiOrders.Send(type, _sight.Player, _group, x, y, commands);
    }

    /// <summary>Sends the units of a list that stand idle away from a tile to it.</summary>
    private void SendIdle(CommandType type, List<OwnFighter> units, int x, int y, List<Command> commands)
    {
        _group.Clear();
        foreach (var unit in units)
        {
            if (unit.Order == UnitOrder.Idle && AiSearch.DistanceSquared(AiOrders.TileX(unit), AiOrders.TileY(unit), x, y) > Near * Near)
            {
                _group.Add(unit.Entity);
            }
        }

        AiOrders.Send(type, _sight.Player, _group, x, y, commands);
    }

    /// <summary>What the force is after. <see cref="TileX"/> and <see cref="TileY"/> are the lowest tile of a structure, or the point itself.</summary>
    private readonly record struct Objective(ObjectiveKind Kind, EntityId Structure, int TileX, int TileY, int Size, int Index)
    {
        /// <summary>The middle tile, where the force is sent.</summary>
        public int X => TileX + (Size / 2);

        public int Y => TileY + (Size / 2);
    }
}
