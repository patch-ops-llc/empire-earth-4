using Godot;

namespace Game;

/// <summary>A full-screen panel of the <see cref="InterfaceFrame"/>, as <see cref="InterfaceFrame.AddPanel"/> returns it.</summary>
public sealed class FramePanel
{
    private readonly InterfaceFrame _frame;

    internal FramePanel(InterfaceFrame frame, string title, Control content, StringName? toggleAction)
    {
        _frame = frame;
        Title = title;
        Content = content;
        ToggleAction = toggleAction;
    }

    public string Title { get; }

    public Control Content { get; }

    /// <summary>The action of the input map that opens and closes the panel, or null.</summary>
    public StringName? ToggleAction { get; }

    public bool IsOpen => _frame.OpenPanel == this;

    /// <summary>Raised when the panel opens, before it is drawn, so that it can show the match as it stands.</summary>
    public event Action? Opened;

    public void Open() => _frame.Open(this);

    public void Close()
    {
        if (IsOpen)
        {
            _frame.ClosePanel();
        }
    }

    public void Toggle()
    {
        if (IsOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    internal void RaiseOpened() => Opened?.Invoke();
}
