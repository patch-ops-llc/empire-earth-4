# empire-earth-4

A hobby real-time strategy prototype: a deterministic C# simulation with a Godot 4 presentation layer on top.

`empire-earth-4` is a working codename. It appears in two places only, the solution file name and the application name in `game/project.godot`.

## Requirements

| Tool | Version | Pinned in |
| --- | --- | --- |
| .NET SDK | 10.0.404, or a later 10.0 release | `global.json` |
| Godot, .NET build | 4.7.2 | `global.json` (`Godot.NET.Sdk`) and the `Sdk` attribute in `game/Game.csproj` |

The simulation, the tools and their tests need only the .NET SDK. Godot is needed to run the game and the smoke test.

## Build and test

```sh
dotnet build -warnaserror
dotnet test
dotnet format --verify-no-changes
dotnet run --project src/Tools -- datacheck game/data
```

The last command loads the game data through the loader the game uses and lists every problem it finds. [docs/data-authoring.md](docs/data-authoring.md) describes it and the data files.

Debug builds, which the tests and the Godot editor use, treat whole-number overflow as an error. Release builds do not. `Directory.Build.props` sets this for every project, and [docs/determinism.md](docs/determinism.md) says why.

The smoke test needs the Godot 4.7.2 .NET build on the `PATH` as `godot`:

```sh
godot --headless --path game --import --build-solutions
godot --headless --path game res://scenes/smoke_test.tscn --quit-after 600
```

The second command must print a line that starts with `SMOKE_TEST_OK`. Check for that line and not the exit code: Godot exits with 0 even when the C# assembly or a script fails to load.

## Run the game

After the import command above has run once:

```sh
godot --path game
```

The game starts in `scenes/main.tscn`: a flat ground of 96 by 96 tiles with a grid, a strategy camera and a debug overlay. No simulation runs behind it yet.

| Control | Keys and mouse |
| --- | --- |
| Pan | W A S D or the arrow keys, or move the pointer to a window edge |
| Zoom | Mouse wheel, `=` and `-`, or a trackpad pinch or two-finger scroll |
| Show or hide the overlay | F3 |

## Continuous integration

| Workflow | Runs on | What it does |
| --- | --- | --- |
| `ci.yml` | Every pull request, on Linux | Build with warnings as errors, format check, tests, `datacheck`, Godot import and build, a check that Godot changed or created no file in the working tree, and the smoke test. |
| `determinism.yml` | Every push to `main`, every night, and pull requests that change the workflow, `Directory.Build.props` or `global.json` | The tests on Linux, Windows, macOS on Apple silicon and macOS on Intel, as a Debug and as a Release build. The tests hold recorded values, so this is where a result that differs between systems shows up. |

## Layout

| Path | Contents |
| --- | --- |
| `src/Sim/` | The simulation. A plain .NET library that must not reference Godot or any package; a test enforces this. One folder per part: `Core/` (world, entities, tick loop, commands, state hash), `Math/` (fixed-point numbers, exact fractions, angles), `Data/` (the loader for `game/data/`) and `Serialization/` (the byte format and the encoding of a command). |
| `src/Net/` | The lockstep network session. A plain .NET library that references only `Sim`: `Protocol/` (the messages and their bytes), `Session/` (the host, the client, and the two in one process for single-player) and `Transport/` (the connection interface, and the loopback used by single-player and tests). |
| `src/Tools/` | The console tools, as one program: `dotnet run --project src/Tools -- <tool> [arguments]`. Each tool is a folder. So far: `datacheck`. |
| `tests/Sim.Tests/` | Tests for `Sim` (xUnit v3 on Microsoft.Testing.Platform), in folders that match those of `src/Sim/`. |
| `tests/Net.Tests/` | Tests for `Net`: whole matches between a host and several clients in one process, over a loopback with latency and jitter that the test controls. |
| `tests/Tools.Tests/` | Tests for the tools, for the data in `game/data/` and for the schemas. |
| `game/` | The Godot project: `project.godot`, `Game.csproj`, `scenes/`, `scripts/`. References `Sim`. |
| `game/data/` | The content of the game as JSON-with-comments files: tracks, rules, unit lines and maps. |
| `schemas/` | JSON Schemas for the files in `game/data/`. |
| `docs/` | [determinism.md](docs/determinism.md): the rules that keep the simulation deterministic, the kernel and the byte format. [data-authoring.md](docs/data-authoring.md): how the data files are written and checked. |
| `.github/workflows/` | `ci.yml` and `determinism.yml`, described above. |
| `global.json` | Pins the .NET SDK and the Godot version, and selects the test runner. |
| `Directory.Build.props` | C# settings shared by every project: target framework, nullable, warnings as errors, analyzers, overflow checking. |
| `empire-earth-4.sln` | All projects. The Godot editor finds it through `dotnet/project/solution_directory` in `project.godot`. |

A path that is not in this table does not exist yet. Whoever creates one adds its row in the same pull request.

Dependencies point one way: `game` and `Tools` may reference `Ai`, `Net` and `Sim`; `Ai` and `Net` may reference `Sim`; `Sim` references nothing.

## Target framework

Every project targets `net10.0`. This was checked against Godot 4.7.2 on 10 Nov 2026 with .NET SDK 10.0.404:

- The editor builds the project (`--build-solutions`) and the smoke scene runs on the .NET 10 runtime.
- Release exports complete for Windows x86_64, macOS universal and Linux x86_64, each carrying the .NET 10 runtime.
- The Linux export runs headless and passes the smoke test. That check produced the Windows and macOS exports on Linux and did not start them.

The target framework is set in two files that must agree: `Directory.Build.props` and `game/Game.csproj`. The Godot editor requires the property in `Game.csproj` itself. If it is missing there, the editor adds `net8.0` to the file when it opens the project.
