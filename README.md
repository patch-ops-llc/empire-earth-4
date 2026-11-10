# empire-earth-4

A hobby real-time strategy prototype: a deterministic C# simulation with a Godot 4 presentation layer on top.

`empire-earth-4` is a working codename. It appears in two places only, the solution file name and the application name in `game/project.godot`.

## Requirements

| Tool | Version | Pinned in |
| --- | --- | --- |
| .NET SDK | 10.0.404, or a later 10.0 release | `global.json` |
| Godot, .NET build | 4.7.2 | `global.json` (`Godot.NET.Sdk`) and the `Sdk` attribute in `game/Game.csproj` |

## Build and test

```sh
dotnet build -warnaserror
dotnet test
dotnet format --verify-no-changes
```

The smoke test needs the Godot 4.7.2 .NET build on the `PATH` as `godot`:

```sh
godot --headless --path game --import --build-solutions
godot --headless --path game res://scenes/smoke_test.tscn --quit-after 600
```

The second command must print a line that starts with `SMOKE_TEST_OK`. Check for that line and not the exit code: Godot exits with 0 even when the C# assembly or a script fails to load.

CI (`.github/workflows/ci.yml`) runs all of these on every pull request. It also fails if Godot changed or created any file in the working tree.

## Layout

| Path | Contents |
| --- | --- |
| `src/Sim/` | The simulation. A plain .NET library that must not reference Godot or any package; a test enforces this. |
| `tests/Sim.Tests/` | Tests for `Sim` (xUnit v3 on Microsoft.Testing.Platform). |
| `game/` | The Godot project: `project.godot`, `Game.csproj`, `scenes/`, `scripts/`. References `Sim`. |
| `global.json` | Pins the .NET SDK and the Godot version, and selects the test runner. |
| `Directory.Build.props` | C# settings shared by every project: target framework, nullable, warnings as errors, analyzers. |
| `empire-earth-4.sln` | All projects. The Godot editor finds it through `dotnet/project/solution_directory` in `project.godot`. |

Planned and not created yet: `src/Net/`, `src/Ai/` and `src/Tools/` with their test projects, `tests/golden/`, `game/data/`, `game/view-data/`, `game/export_presets.cfg` and `docs/`. Each arrives with the first code or content that belongs in it.

Dependencies point one way: `game` may reference `Ai`, `Net` and `Sim`; `Ai` and `Net` may reference `Sim`; `Sim` references nothing.

## Target framework

Every project targets `net10.0`. This was checked against Godot 4.7.2 on 10 Nov 2026 with .NET SDK 10.0.404:

- The editor builds the project (`--build-solutions`) and the smoke scene runs on the .NET 10 runtime.
- Release exports complete for Windows x86_64, macOS universal and Linux x86_64, each carrying the .NET 10 runtime.
- The Linux export runs headless and passes the smoke test. The Windows and macOS exports were produced on Linux and have not been launched.

The target framework is set in two files that must agree: `Directory.Build.props` and `game/Game.csproj`. The Godot editor requires the property in `Game.csproj` itself. If it is missing there, the editor adds `net8.0` to the file when it opens the project.
