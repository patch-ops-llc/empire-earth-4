using System.Globalization;
using Godot;
using Sim.Core;
using Sim.Economy;
using Sim.Production;

namespace Game;

/// <summary>
/// The production panel (design 5.4): with one finished structure of the player's that trains
/// selected, the bottom panel offers what it trains, shows its queue of up to five orders with
/// the progress of the first, and says where its rally point is.
/// </summary>
/// <remarks>
/// <para>
/// A button says what an order placed now would be: one unit or a pair, its whole price, its
/// supply and its time. One that cannot be placed is greyed and says why, in the simulation's
/// own answer (<c>CheckTrain</c>). An order in the queue shows what it was placed with, and a
/// click on it cancels it. Everything leaves as a
/// command through the session and shows once the simulation has carried it out, a tick or more
/// later; an order the simulation then refuses is said in the message line.
/// </para>
/// <para>
/// The panel is one of the controls of the frame's selection area, under the line that names
/// the structure. The keys are actions of the input map, one for each of the first eight
/// buttons and one that cancels the last order.
/// </para>
/// </remarks>
public partial class ProductionPart : InterfacePart
{
    /// <summary>The most buttons that have a key. A structure of the data offers eight things at most.</summary>
    public const int KeyCount = 8;

    /// <summary>The action that cancels the last order of the queue.</summary>
    public static readonly StringName CancelLastAction = "production_cancel_last";

    private const int ButtonsInARow = 4;
    private const int SmallFont = 13;

    // Three lines: what is ordered and its key, the price, the supply and the time.
    private static readonly Vector2 TrainButtonSize = new(172f, 62f);
    private static readonly Vector2 SlotSize = new(136f, 50f);
    private static readonly Color Dim = new(1f, 1f, 1f, 0.55f);

    // Named in the input map of project.godot. No key is read here.
    private static readonly StringName[] TrainActions = [.. Enumerable.Range(1, KeyCount).Select(i => new StringName($"production_train_{i}"))];

    private readonly List<ProductionOffer> _offers = [];
    private readonly List<Button> _trainButtons = [];
    private readonly Button[] _slots = new Button[Producer.MaxOrders];
    private readonly ColorRect[] _slotTracks = new ColorRect[Producer.MaxOrders];
    private readonly ColorRect[] _slotFills = new ColorRect[Producer.MaxOrders];

    private VBoxContainer _root = null!;
    private GridContainer _trainGrid = null!;
    private Label _rally = null!;
    private int _shownType = -1;

    /// <summary>The structure whose production is shown, or none.</summary>
    public EntityId Shown { get; private set; }

    /// <summary>What the shown structure offers, as the buttons say it. Empty while nothing is shown.</summary>
    public IReadOnlyList<ProductionOffer> Offers => _offers;

    /// <summary>The number of orders the queue shows.</summary>
    public int QueueShown { get; private set; }

    protected override void Attach()
    {
        _root = UiStyle.Column(6);
        _root.Name = "Production";
        _root.Visible = false;

        _trainGrid = new GridContainer { Name = "Train", Columns = ButtonsInARow };
        _trainGrid.AddThemeConstantOverride("h_separation", 6);
        _trainGrid.AddThemeConstantOverride("v_separation", 6);
        _root.AddChild(_trainGrid);

        var queue = UiStyle.Row(6);
        queue.Name = "Queue";
        for (var position = 0; position < _slots.Length; position++)
        {
            queue.AddChild(CreateSlot(position));
        }

        _root.AddChild(queue);

        _rally = new Label { Name = "RallyPoint", Modulate = Dim };
        _rally.AddThemeFontSizeOverride("font_size", SmallFont);
        _root.AddChild(_rally);

        Frame.SelectionArea.AddChild(_root);
        View.Selection.Changed += Refresh;
    }

    public override void AfterTick()
    {
        ReadEvents();
        Refresh();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // The keys are the world's: they rest while a page or a panel has the keyboard.
        if (Shown == EntityId.None || !View.Units.CanProcess())
        {
            return;
        }

        for (var i = 0; i < _offers.Count && i < TrainActions.Length; i++)
        {
            if (@event.IsActionPressed(TrainActions[i]))
            {
                Train(i);
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        if (@event.IsActionPressed(CancelLastAction))
        {
            if (QueueShown > 0)
            {
                Cancel(QueueShown - 1);
            }

            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>
    /// Places an order for what the button at a place offers, as a click on it does. An order
    /// that cannot be placed now is not sent, and the player is told why.
    /// </summary>
    public void Train(int place)
    {
        if (Shown == EntityId.None || place < 0 || place >= _offers.Count)
        {
            return;
        }

        var offer = _offers[place];
        var match = View.Match;
        if (!offer.Allowed)
        {
            Frame.Say(ProductionWords.ReasonSentence(match, View.Player, offer.Unit, offer.Check));
            SoundPart.Of(Frame)?.Acknowledge(taken: false);
            return;
        }

        EntityId[] structure = [Shown];
        if (!View.Client.Submit(new Command(CommandType.Train, View.Player, structure, contentId: offer.Unit)))
        {
            return;
        }

        SoundPart.Of(Frame)?.Acknowledge();
        if (OS.IsStdOutVerbose())
        {
            GD.Print($"Train order for {ProductionWords.Order(match, offer.Unit, offer.Count)} at {Shown} at tick {match.World.Tick}.");
        }
    }

    /// <summary>Cancels the order at a position of the shown queue, as a click on it does.</summary>
    public void Cancel(int position)
    {
        if (Shown == EntityId.None || position < 0 || position >= QueueShown)
        {
            return;
        }

        EntityId[] structure = [Shown];
        if (!View.Client.Submit(new Command(CommandType.CancelTrain, View.Player, structure, contentId: position)))
        {
            return;
        }

        SoundPart.Of(Frame)?.Acknowledge();
        if (OS.IsStdOutVerbose())
        {
            GD.Print($"Cancel order {position + 1} at {Shown} at tick {View.Match.World.Tick}.");
        }
    }

    // What the simulation refused of the local player's orders, and what it gave back when a
    // structure fell with orders in its queue.
    private void ReadEvents()
    {
        var match = View.Match;
        var refunded = 0;
        foreach (var happened in match.Simulation.Events)
        {
            if (!TrainEvent.TryRead(happened, out var train) || train.Player != View.Player)
            {
                continue;
            }

            if (train.Kind == SimEventKind.TrainRefused)
            {
                Frame.Say(ProductionWords.ReasonSentence(match, View.Player, train.Unit, train.Reason));
            }
            else if (train.Kind == SimEventKind.TrainRefunded)
            {
                refunded++;
            }
        }

        if (refunded > 0)
        {
            Frame.Say(refunded == 1
                ? "A structure fell with an order in its queue. Its price was given back."
                : string.Create(CultureInfo.InvariantCulture, $"A structure fell with {refunded} orders in its queue. Their prices were given back."));
        }
    }

    private void Refresh()
    {
        var match = View.Match;
        Shown = ProductionOffer.ProducerIn(View);
        if (Shown == EntityId.None)
        {
            _root.Visible = false;
            _offers.Clear();
            QueueShown = 0;
            return;
        }

        ProductionOffer.Read(match, View.Player, Shown, _offers);
        ShowOffers(match.Structures.Get(Shown).Type);
        ShowQueue(match.Production.Producers.Get(Shown));
        _root.Visible = true;
    }

    private void ShowOffers(int structureType)
    {
        // The buttons are those of one kind of structure, and are made again for another.
        if (structureType != _shownType || _trainButtons.Count != _offers.Count)
        {
            _shownType = structureType;
            foreach (var old in _trainButtons)
            {
                _trainGrid.RemoveChild(old);
                old.QueueFree();
            }

            _trainButtons.Clear();
            _trainGrid.Columns = Math.Clamp(_offers.Count, 1, ButtonsInARow);
            for (var place = 0; place < _offers.Count; place++)
            {
                _trainButtons.Add(CreateTrainButton(place));
            }
        }

        var match = View.Match;
        for (var place = 0; place < _offers.Count; place++)
        {
            var offer = _offers[place];
            var button = _trainButtons[place];
            var name = ProductionWords.Order(match, offer.Unit, offer.Count);
            var key = place < TrainActions.Length ? KeyOf(TrainActions[place]) : string.Empty;

            // The whole order's numbers: a pair shows the price and supply of two and the
            // pair's time. One that cannot be placed says why in place of the price.
            var price = ProductionWords.Price(offer.Materials, offer.Energy);
            var second = offer.Allowed ? price : ProductionWords.Reason(match, View.Player, offer);
            var third = offer.Allowed ? ProductionWords.SupplyAndTime(offer) : $"{price} · {ProductionWords.Time(offer.BuildTicks)}";
            button.Text = key.Length > 0 ? $"{name}  ({key})\n{second}\n{third}" : $"{name}\n{second}\n{third}";
            button.Disabled = !offer.Allowed;
            button.TooltipText = offer.Allowed
                ? ProductionWords.OfferSentence(offer)
                : ProductionWords.ReasonSentence(match, View.Player, offer.Unit, offer.Check);
        }

        _trainGrid.Visible = _offers.Count > 0;
    }

    private void ShowQueue(in Producer producer)
    {
        var match = View.Match;
        QueueShown = producer.Count;
        for (var position = 0; position < _slots.Length; position++)
        {
            var slot = _slots[position];
            var track = _slotTracks[position];
            var fill = _slotFills[position];
            if (position >= producer.Count)
            {
                slot.Text = string.Create(CultureInfo.InvariantCulture, $"{position + 1}");
                slot.Disabled = true;
                slot.TooltipText = string.Empty;
                track.Visible = false;
                continue;
            }

            var order = producer[position];
            var name = ProductionWords.Order(match, order.Unit, order.Count);
            slot.Disabled = false;
            slot.TooltipText = ProductionWords.CancelSentence(order);
            track.Visible = position == 0;
            if (position > 0)
            {
                // What it was placed with, which a research since then does not change.
                slot.Text = $"{name}\n{ProductionWords.Paid(order)}";
            }
            else if (producer.BuildTicks <= 0)
            {
                // Production begins in the tick after the order was placed.
                slot.Text = $"{name}\n{ProductionWords.Paid(order)}";
                fill.AnchorRight = 0f;
            }
            else if (producer.Progress >= producer.BuildTicks)
            {
                // Design 5.4: an order whose units have nowhere to stand waits and tries again.
                slot.Text = $"{name}\nno room";
                fill.AnchorRight = 1f;
            }
            else
            {
                slot.Text = $"{name}\n{ProductionWords.Paid(order)} · {ProductionWords.TimeLeft(producer.Progress, producer.BuildTicks)}";
                fill.AnchorRight = producer.Progress / (float)producer.BuildTicks;
            }
        }

        _rally.Text = RallyLine(producer);
    }

    private string RallyLine(in Producer producer)
    {
        if (!producer.RallySet)
        {
            return "Rally point: beside the structure. Right-click sets it.";
        }

        // Only a worker goes to work, so only a structure that trains workers says so.
        return TrainsWorkers() && LiesOnWorkSite(producer.RallyPoint.X.FloorToInt(), producer.RallyPoint.Y.FloorToInt())
            ? "Rally point set on a place of work: new workers go to work there. Right-click moves it."
            : "Rally point set: new units walk there. Right-click moves it.";
    }

    private bool TrainsWorkers()
    {
        foreach (var offer in _offers)
        {
            if (offer.Unit == TrainOrder.Worker)
            {
                return true;
            }
        }

        return false;
    }

    // A deposit, or a Generator of the player's own (design 5.4), as the simulation finds it.
    private bool LiesOnWorkSite(int x, int y)
    {
        var match = View.Match;
        var sites = match.WorkSites;
        for (var i = 0; i < sites.Count; i++)
        {
            var site = sites.ValueAt(i);
            if (site.Covers(x, y)
                && (site.Kind == WorkSiteKind.Deposit || site.Owner == View.Player)
                && match.World.Entities.IsAlive(sites.EntityAt(i)))
            {
                return true;
            }
        }

        return false;
    }

    private Button CreateTrainButton(int place)
    {
        var button = UiStyle.FrameButton(string.Empty);
        button.Name = $"Train{place + 1}";
        button.CustomMinimumSize = TrainButtonSize;
        button.Alignment = HorizontalAlignment.Left;
        button.AddThemeFontSizeOverride("font_size", SmallFont);
        button.Pressed += () => Train(place);
        _trainGrid.AddChild(button);
        return button;
    }

    private Button CreateSlot(int position)
    {
        var slot = UiStyle.FrameButton(string.Empty);
        slot.Name = $"Order{position + 1}";
        slot.CustomMinimumSize = SlotSize;
        slot.ClipText = true;
        slot.AddThemeFontSizeOverride("font_size", SmallFont);
        slot.Pressed += () => Cancel(position);

        // The progress of the order in production, along the lower edge of its slot.
        var track = new ColorRect
        {
            Name = "Progress",
            Color = new Color(0f, 0f, 0f, 0.45f),
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        slot.AddChild(track);
        track.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide);
        track.OffsetTop = -6f;
        track.OffsetLeft = 4f;
        track.OffsetRight = -4f;
        track.OffsetBottom = -3f;

        var fill = new ColorRect { Name = "Done", Color = new Color(0.45f, 0.85f, 0.5f), MouseFilter = Control.MouseFilterEnum.Ignore };
        track.AddChild(fill);
        fill.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        fill.AnchorRight = 0f;

        _slots[position] = slot;
        _slotTracks[position] = track;
        _slotFills[position] = fill;
        return slot;
    }

    // The key of an action as the input map has it, for the text of a button.
    private static string KeyOf(StringName action)
    {
        if (!InputMap.HasAction(action))
        {
            return string.Empty;
        }

        foreach (var input in InputMap.ActionGetEvents(action))
        {
            if (input is InputEventKey key)
            {
                return key.PhysicalKeycode != Key.None ? OS.GetKeycodeString(key.PhysicalKeycode) : OS.GetKeycodeString(key.Keycode);
            }
        }

        return string.Empty;
    }
}
