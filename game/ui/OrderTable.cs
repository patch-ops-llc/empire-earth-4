using Godot;
using Sim.Core;

namespace Game;

/// <summary>What a row of the <see cref="OrderTable"/> is asked to turn into an order.</summary>
/// <param name="View">The match and its view. Commands leave through <c>View.Client</c>.</param>
/// <param name="Selected">The selected entities in ascending ID order, the order a command lists them in. Never empty.</param>
/// <param name="Target">What the pointer is on. Never <see cref="PointerTargets.None"/>.</param>
public readonly record struct OrderRequest(MatchView View, EntityId[] Selected, PointerTarget Target);

/// <summary>
/// One row of the <see cref="OrderTable"/>: for a kind of thing under the pointer, and a
/// modifier or none, the order the selection is given.
/// </summary>
public sealed class OrderRow
{
    /// <summary>The order in a word or two, as "Move". It names the row in the log.</summary>
    public required string Name { get; init; }

    /// <summary>The kinds of thing under the pointer this row is for.</summary>
    public PointerTargets Targets { get; init; } = PointerTargets.Any;

    /// <summary>
    /// An action of the input map that must be held for this row, or null for a row that needs
    /// none. A key or a button is never named here.
    /// </summary>
    public StringName? Modifier { get; init; }

    /// <summary>The color of the mark the order leaves on the ground.</summary>
    public Color Mark { get; init; } = new(1f, 0.95f, 0.4f);

    /// <summary>
    /// Gives the order: sends one or more commands through <c>View.Client</c>. Returns false when
    /// this row has no order for this selection, as when none of the selected can carry it out,
    /// or when the session took no command. The next row is then asked.
    /// </summary>
    public required Func<OrderRequest, bool> Give { get; init; }
}

/// <summary>
/// The one table that turns the selection, what is under the pointer and a modifier into a
/// command. The <see cref="UnitControl"/> asks it on every order click. A piece that adds a kind
/// of order adds a row, and leaves the unit control alone.
/// </summary>
/// <remarks>
/// The rows are asked in three rounds, and inside a round in the order they were added. The
/// first row that gives an order ends the search.
/// <list type="number">
/// <item>Rows with a modifier that is held.</item>
/// <item>Rows without a modifier that name some kinds of target and not all, as "an enemy unit".</item>
/// <item>Rows without a modifier for any target, as the move order.</item>
/// </list>
/// So a row for a deposit wins over the move order when workers are selected, and falls back to
/// it when the row says it has no order for that selection.
/// </remarks>
public sealed class OrderTable
{
    private readonly List<OrderRow> _rows = [];

    /// <summary>The rows, in the order they were added.</summary>
    public IReadOnlyList<OrderRow> Rows => _rows;

    /// <summary>
    /// The kinds of target some row names, beside the ground. The pointer is asked for these on
    /// an order click, so that what no order is about never hides what one is about: a deposit
    /// is found under the player's own units standing on it, as long as no row names those.
    /// </summary>
    public PointerTargets Wanted
    {
        get
        {
            var wanted = PointerTargets.Ground;
            foreach (var row in _rows)
            {
                if (row.Targets != PointerTargets.Any)
                {
                    wanted |= row.Targets;
                }
            }

            return wanted;
        }
    }

    public void Add(OrderRow row) => _rows.Add(row);

    /// <summary>
    /// Gives the selection the order that fits what the pointer is on. Returns the row that gave
    /// it, or null when no order was given.
    /// </summary>
    public OrderRow? Give(MatchView view, PointerTarget target)
    {
        if (view.Selection.Count == 0 || !target.IsOnWorld)
        {
            return null;
        }

        var request = new OrderRequest(view, [.. view.Selection.Entities], target);
        for (var round = 0; round < 3; round++)
        {
            foreach (var row in _rows)
            {
                if (RoundOf(row) == round && row.Give(request))
                {
                    return row;
                }
            }
        }

        return null;

        int RoundOf(OrderRow row)
        {
            if (!target.Is(row.Targets))
            {
                return -1;
            }

            if (row.Modifier is not null)
            {
                return Input.IsActionPressed(row.Modifier) ? 0 : -1;
            }

            return row.Targets == PointerTargets.Any ? 2 : 1;
        }
    }
}
