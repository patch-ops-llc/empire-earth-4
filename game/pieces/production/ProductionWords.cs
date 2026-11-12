using System.Globalization;
using System.Text;
using Sim;
using Sim.Economy;
using Sim.Production;
using Sim.Setup;

namespace Game;

/// <summary>
/// The words the production panel uses: what a structure trains, what an order costs, and why
/// an order cannot be placed.
/// </summary>
public static class ProductionWords
{
    /// <summary>What an order trains, in a word or two: "Worker", or the chassis from its ID, as "Man at arms".</summary>
    public static string NameOf(Match match, int unit)
    {
        if (unit == TrainOrder.Worker)
        {
            return "Worker";
        }

        var chassis = match.Data.Chassis;
        return unit >= 0 && unit < chassis.Count ? Name(chassis[unit].Id) : "Unit";
    }

    /// <summary>An order by its name and how many units it makes: "Worker", or "Peltast ×2" for a pair.</summary>
    public static string Order(Match match, int unit, int count) =>
        count > 1
            ? string.Create(CultureInfo.InvariantCulture, $"{NameOf(match, unit)} ×{count}")
            : NameOf(match, unit);

    /// <summary>A price as the research panel words one: "50 M", "120 M 40 E". M is Materials and E is Energy.</summary>
    public static string Price(int materials, int energy)
    {
        var text = new StringBuilder();
        if (materials > 0 || energy == 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"{materials} M");
        }

        if (energy > 0)
        {
            text.Append(text.Length > 0 ? " " : string.Empty).Append(CultureInfo.InvariantCulture, $"{energy} E");
        }

        return text.ToString();
    }

    /// <summary>Why an order cannot be placed, short enough for a button. Empty when it can.</summary>
    public static string Reason(Match match, int player, in ProductionOffer offer) => offer.Check switch
    {
        TrainCheck.Allowed => string.Empty,
        TrainCheck.Locked => Needs(match, player, offer.Unit, fitButton: true) is { Length: > 0 } research ? $"Needs {research}" : "Locked",
        TrainCheck.QueueFull => "Queue full",
        TrainCheck.TooExpensive => Lacks(match, player, offer),
        TrainCheck.NoSupply => "No free supply",
        TrainCheck.NotTrainedHere => "Not trained here",
        _ => "Not ready",
    };

    /// <summary>Why an order cannot be placed, as a sentence for the player. Empty when it can.</summary>
    public static string ReasonSentence(Match match, int player, int unit, TrainCheck check)
    {
        var name = NameOf(match, unit);
        return check switch
        {
            TrainCheck.Allowed => string.Empty,
            TrainCheck.Locked => Needs(match, player, unit, fitButton: false) is { Length: > 0 } research
                ? $"{name} is locked: it needs {research}."
                : $"{name} is locked: research has not reached it yet.",
            TrainCheck.QueueFull => string.Create(CultureInfo.InvariantCulture, $"The queue is full: a structure holds {Producer.MaxOrders} orders."),
            TrainCheck.TooExpensive => $"{name} costs more than you have.",
            TrainCheck.NoSupply => $"No free supply for {name}.",
            TrainCheck.NotTrainedHere => $"{name} is not trained at this structure.",
            _ => "This structure trains nothing now.",
        };
    }

    /// <summary>The time left of an order in whole seconds, rounded up: "8 s".</summary>
    public static string TimeLeft(int progress, int buildTicks)
    {
        var ticks = Math.Max(0, buildTicks - progress);
        var seconds = (ticks + SimInfo.TicksPerSecond - 1) / SimInfo.TicksPerSecond;
        return string.Create(CultureInfo.InvariantCulture, $"{seconds} s");
    }

    // What research a locked chassis waits for: its line's first track at the chassis's epoch
    // and its second at the epoch before (design 2.2). Only what is missing is named, by the
    // tracks' full names, or by their short ones where two must fit on a button.
    private static string Needs(Match match, int player, int unit, bool fitButton)
    {
        var data = match.Data;
        if (unit < 0 || unit >= data.Chassis.Count)
        {
            return string.Empty;
        }

        var chassis = data.Chassis[unit];
        if (data.LineOf(chassis.Line) is not { } line)
        {
            return string.Empty;
        }

        var tiers = match.Research.Tech(player).Tiers;
        var first = tiers[line.PrimaryTrack] < chassis.Epoch;
        var second = tiers[line.SecondaryTrack] < chassis.Epoch - 1;
        if (first && second)
        {
            return fitButton
                ? string.Create(CultureInfo.InvariantCulture, $"{data.Tracks[line.PrimaryTrack].ShortName} {chassis.Epoch}, {data.Tracks[line.SecondaryTrack].ShortName} {chassis.Epoch - 1}")
                : string.Create(CultureInfo.InvariantCulture, $"{data.Tracks[line.PrimaryTrack].Name} {chassis.Epoch} and {data.Tracks[line.SecondaryTrack].Name} {chassis.Epoch - 1}");
        }

        if (first)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{data.Tracks[line.PrimaryTrack].Name} {chassis.Epoch}");
        }

        return second
            ? string.Create(CultureInfo.InvariantCulture, $"{data.Tracks[line.SecondaryTrack].Name} {chassis.Epoch - 1}")
            : string.Empty;
    }

    // What the stockpile is short of for one unit, rounded up to whole resources: "Needs 30 M more".
    private static string Lacks(Match match, int player, in ProductionOffer offer)
    {
        var stock = match.Stockpiles;
        var materials = Short(offer.UnitMaterials, stock.Materials(player));
        var energy = Short(offer.UnitEnergy, stock.Energy(player));
        return materials > 0 || energy > 0 ? $"Needs {Price(materials, energy)} more" : "Too expensive";

        static int Short(int price, long haveThousandths)
        {
            var missing = (price * Stockpiles.PerResource) - haveThousandths;
            return missing <= 0 ? 0 : (int)((missing + Stockpiles.PerResource - 1) / Stockpiles.PerResource);
        }
    }

    private static string Name(string id)
    {
        var words = id.Replace('_', ' ');
        return words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];
    }
}
