using Godot;

namespace Game;

/// <summary>
/// A command for the selection that needs no target, as "Stop": a button in the bottom panel,
/// with an action of the input map as its key.
/// </summary>
public sealed class SelectionCommand
{
    /// <summary>The text of the button.</summary>
    public required string Label { get; init; }

    /// <summary>The action of the input map that gives the command from the keyboard, or null for a button only.</summary>
    public StringName? Action { get; init; }

    /// <summary>Whether the command is offered for the selection as it is now. Asked when the selection changes and after every tick.</summary>
    public required Func<MatchView, bool> Applies { get; init; }

    /// <summary>Gives the command: sends what it stands for through <c>View.Client</c>.</summary>
    public required Action<MatchView> Run { get; init; }
}
