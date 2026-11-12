using Sim.Construction;
using Sim.Core;
using Sim.Data;

namespace Game;

/// <summary>
/// The smoke test's check that a build order placed in the build menu is answered with a sound:
/// two seconds of play in which the local player selects a worker, opens the menu, places a
/// structure some way off, and cancels the order while the worker is still walking, which gives
/// the whole price back.
/// </summary>
/// <remarks>
/// The cancel is given as the Cancel build command gives it. In a match that command is one of
/// the frame's, which the sound part hears by itself, so this part does not test its sound.
/// </remarks>
public sealed class BuildMenuSoundSmoke : PanelSoundSmoke
{
    // The site is at least this far from the worker, in tiles, so that the worker is still on
    // its way when the order is cancelled, and no farther than the second number.
    private const int NearestSite = 8;
    private const int FarthestSite = 48;

    private EntityId _worker;
    private bool _placed;

    public override string Name => "Sound of the build menu";

    public override void GiveOrders(int tick, SmokeOrders orders)
    {
        switch (tick)
        {
            case 0:
                Place(orders);
                break;

            case TakeBackAt when _placed:
                Cancel(orders);
                break;

            case GoneAt when _placed:
                orders.View.Selection.Set([_worker]);
                if (CancelBuild.Applies(orders.View))
                {
                    orders.Fail($"worker {_worker.Value} still has a build order {GoneAt - TakeBackAt} ticks after it was cancelled.");
                }

                orders.View.Selection.Clear();
                break;
        }
    }

    private void Place(SmokeOrders orders)
    {
        var match = orders.Match;
        var view = orders.View;
        if (view.Interface.Parts.OfType<BuildMenu>().FirstOrDefault() is not { } menu)
        {
            orders.Fail("the view has no build menu.");
            return;
        }

        var workers = Array.FindAll(orders.OwnUnits(), match.Workers.Contains);
        if (workers.Length == 0)
        {
            orders.Fail("the local player has no worker to build with.");
            return;
        }

        _worker = workers[0];
        view.Selection.Set([_worker]);
        if (CancelBuild.Applies(view))
        {
            orders.Fail($"worker {_worker.Value} has a build order already when the part begins.");
            return;
        }

        menu.Open();
        if (!menu.IsOpen)
        {
            orders.Fail("the build menu did not open with a worker selected.");
            return;
        }

        if (FindOrder(view, _worker) is not { } found)
        {
            orders.Fail(
                $"the local player can place no structure now: none that stands in territory is allowed and paid for, " +
                $"or none has a site between {NearestSite} and {FarthestSite} tiles from worker {_worker.Value}.");
            return;
        }

        var (structure, x, y) = found;
        if (!menu.Choose(structure))
        {
            orders.Fail($"the build menu did not start placing the {structure.Id}.");
            return;
        }

        _placed = Answered(orders, $"placed a {structure.Id} in the build menu", () =>
        {
            if (!menu.Place(structure, x, y))
            {
                orders.Fail($"the build menu did not send the order for the {structure.Id}.");
            }
        });
    }

    private void Cancel(SmokeOrders orders)
    {
        var view = orders.View;
        view.Selection.Set([_worker]);
        if (!CancelBuild.Applies(view) || !CancelBuild.Give(view))
        {
            orders.Fail($"worker {_worker.Value} has no build order to cancel {TakeBackAt} ticks after the build menu sent one.");
        }
    }

    // A structure the player may order now with a site the rules accept, as the menu asks them.
    private static (StructureDef Structure, int X, int Y)? FindOrder(MatchView view, EntityId worker)
    {
        var match = view.Match;
        var checks = BuildChecks.For(view);
        if (!match.Bodies.TryGet(worker, out var body))
        {
            return null;
        }

        var nearX = body.Position.X.FloorToInt();
        var nearY = body.Position.Y.FloorToInt();
        foreach (var structure in match.Data.Structures)
        {
            if (!checks.Lists(structure)
                || checks.Structure(structure) != BuildProblem.None
                || !ConstructionRules.NeedsTerritory(structure))
            {
                continue;
            }

            for (var away = NearestSite; away <= FarthestSite; away++)
            {
                for (var dy = -away; dy <= away; dy++)
                {
                    for (var dx = -away; dx <= away; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == away
                            && Placement.Of(checks, structure, nearX + dx, nearY + dy).IsAccepted)
                        {
                            return (structure, nearX + dx, nearY + dy);
                        }
                    }
                }
            }
        }

        return null;
    }
}
