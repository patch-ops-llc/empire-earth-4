using System.Reflection;
using System.Runtime.Versioning;
using Godot;
using Sim;

namespace Game;

/// <summary>
/// Root of <c>scenes/smoke_test.tscn</c>, which CI runs headless. Reaching <see cref="_Ready"/> shows
/// that Godot loaded the game assembly and that the game can call into the engine-free simulation
/// library. CI looks for the marker line, because a script that fails to load never reaches this code.
/// </summary>
public partial class SmokeTest : Node
{
    public const string Marker = "SMOKE_TEST_OK";

    public override void _Ready()
    {
        var simFramework = typeof(SimInfo).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;
        var gameFramework = typeof(SmokeTest).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;

        GD.Print($"{Marker} runtime={System.Environment.Version} game=[{gameFramework}] sim=[{simFramework}] " +
            $"ticks_per_second={SimInfo.TicksPerSecond}");
        GetTree().Quit();
    }
}
