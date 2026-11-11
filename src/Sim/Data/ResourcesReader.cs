namespace Sim.Data;

internal static class ResourcesReader
{
    /// <summary>Reads <c>resources.jsonc</c>.</summary>
    public static ResourceRules Read(JsonValue root)
    {
        var file = root.Object();
        file.Optional(DataRules.SchemaKey)?.String();

        var materials = file.Required("materials").Object();
        var materialsRate = materials.Required("per_worker_per_minute").IncomeRate();
        var workersPerDeposit = materials.Required("workers_per_deposit").Integer(1, DataRules.MaxWorkerPlaces);
        var fullRateAmount = materials.Required("full_rate_amount").Integer(1, DataRules.MaxFullRateAmount);
        var depletedRate = materials.Required("depleted_rate").Share();
        materials.End();

        var energy = file.Required("energy").Object();
        var energyRate = energy.Required("per_worker_per_minute").IncomeRate();
        var workersPerGenerator = energy.Required("workers_per_generator").Integer(1, DataRules.MaxWorkerPlaces);
        energy.End();

        var worker = ReadWorker(file.Required("worker"));
        file.End();

        return new ResourceRules(
            new MaterialsRules(materialsRate, workersPerDeposit, fullRateAmount, depletedRate),
            new EnergyRules(energyRate, workersPerGenerator),
            worker);
    }

    private static WorkerProfile ReadWorker(JsonValue value)
    {
        var worker = value.Object();
        var hitPoints = worker.Required("hp").Number(DataRules.Zero, DataRules.MaxHitPoints, aboveMin: true);
        var speed = worker.Required("speed").Number(DataRules.Zero, DataRules.MaxSpeed, aboveMin: true);
        var sight = worker.Required("sight").Integer(1, 64);
        var supply = worker.Required("supply").Integer(1, 100);
        var spacing = worker.Required("spacing").Number(DataRules.Zero, DataRules.MaxDistance, aboveMin: true);

        var cost = worker.Required("cost").Object();
        var materials = cost.Required("materials").Integer(0, DataRules.MaxPrice);
        var energy = cost.Required("energy").Integer(0, DataRules.MaxPrice);
        cost.End();

        var buildTicks = worker.Required("build_seconds").SecondsAsTicks();
        worker.End();
        return new WorkerProfile(hitPoints, speed, sight, supply, spacing, materials, energy, buildTicks);
    }

    /// <summary>What one worker earns in a minute: above zero and at most <see cref="DataRules.MaxIncomeRate"/>.</summary>
    private static DataNumber IncomeRate(this JsonValue value) => value.Number(DataRules.Zero, DataRules.MaxIncomeRate, aboveMin: true);
}
