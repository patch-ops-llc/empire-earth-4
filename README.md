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
dotnet run --project src/Tools -- replay verify tests/golden/replays --data tests/golden/data
```

The `datacheck` command loads the game data through the loader the game uses and lists every problem it finds. [docs/data-authoring.md](docs/data-authoring.md) describes it and the data files. The last command runs the recorded matches of `tests/golden/` again and fails unless every state hash is the recorded one. [docs/determinism.md](docs/determinism.md) describes them.

Debug builds, which the tests and the Godot editor use, treat whole-number overflow as an error. Release builds and the exported game do not. `Directory.Build.props` sets this for every project, and [docs/determinism.md](docs/determinism.md) says why.

The smoke test needs the Godot 4.7.2 .NET build on the `PATH` as `godot`:

```sh
godot --headless --path game --import --build-solutions
godot --headless --path game --quit-after 600 -- --smoke-test
```

The second command starts the game in its main scene with the argument `--smoke-test`. The game loads the data, builds a match with a fixed seed, gives it a fixed script of orders through the session, runs it to the script's end, prints one line that starts with `SMOKE_TEST_OK` and quits. It takes about a second. Check for that line and not the exit code: Godot exits with 0 even when the C# assembly or a script fails to load.

The script is made of parts that run one after another, one for each kind of order the view can give: `game/scripts/SmokeTest.cs` lists them, and the line's `tick` and `orders` say how many ticks and orders they came to.

The line carries two hashes. `content_hash` is the hash of the data files as the game read them, and it must equal the one `datacheck` prints. `state_hash` is the hash of the match's state after the script, and it must be the same on every system. `ci.yml` checks the first. `export.yml` checks both for every build it makes, and an exported build takes the same argument:

```sh
./Game.x86_64 --headless --quit-after 600 -- --smoke-test
```

## Run the game

After the import command above has run once:

```sh
godot --path game
```

The game starts in `scenes/main.tscn`. It loads `game/data/` through the simulation's loader and opens a menu: play alone, host a match, or join one by the host's address. A match is for two players on the map `ridge`. The host draws its seed and each player's start, and sends the description of the match to whoever joins: game version, content hash, map, seed and starts. A joining game that has another version or other data refuses, and tells its player why. The game draws the match: the ground with a grid, a tree on every forest tile, a block of rock on every cliff tile and one shape for every unit, in its owner's color. The host is player 0, in blue.

The match runs through the lockstep session of `src/Net/`: every frame the game takes in what the network brought, hands the session the time that passed, and the session runs the ticks, 20 a second. Alone, the host and the player are in one process with no network. Hosting, the game listens on UDP port 24642 unless told otherwise, and its own player joins over the loopback. The game only reads the match and never changes it. A move order leaves as a command through the session and runs a few ticks later. A player who leaves, or closes the window, tells the host so; the others play on. When the host leaves, the match ends for everyone.

Every match writes its replay, with the state hash of every tick, to the folder `matches` in the game's user data folder. A match that stops on a desync also writes a snapshot of its state beside the replay, and `desync-diff` compares the files of two players:

```sh
dotnet run --project src/Tools -- desync-diff host_p0.replay guest_p1.replay
```

The game version is the text that two players must share. `Directory.Build.props` takes it from the environment variable `GAME_VERSION`, which the export workflow sets to the tag or to `dev-` and the commit; any other build is `dev`.

Arguments after `--` on the command line start a match without the menu:

| Argument | What it does |
| --- | --- |
| `--alone` | Starts a match alone at once, with no network. |
| `--host [port]` | Hosts a match at once. |
| `--join <address>[:port]` | Joins a match at once. |
| `--match-files <folder>` | Writes replays and snapshots to this folder. |
| `--net-test <ticks>` | With `--host` or `--join`: plays a match with nobody at the keyboard, on the local address only. The host starts when the other game is ready, each game orders its units to move, the host ends the match after this many ticks, and each game prints a line that starts with `NET_TEST_OK` with the last tick and the state hash. The two lines must agree. |

Two games on one machine, the second in another terminal once the first has printed that it is hosting. CI runs the same pair:

```sh
godot --headless --path game -- --host --net-test 300
godot --headless --path game -- --join 127.0.0.1 --net-test 300
```

Selection works without physics. A click or a box is turned into an area of the ground, the simulation's spatial index names the units there, and each is tested again where it appears on screen. Started with Godot's `--verbose`, the game prints every selection, every order and every message to the player.

What the player sees and does in a match is put together from pieces, each in a folder of its own under `game/pieces/` and named in `game/pieces/ViewPieces.cs`. A piece adds what it has to four things the game gives it:

| What | Where | A piece adds |
| --- | --- | --- |
| The world view | `game/scripts/WorldView.cs` | A `WorldLayer` for each kind of thing it draws. The world view draws the units and calls every layer after every tick and in every frame. |
| The frame of the interface | `game/ui/InterfaceFrame.cs` | An `InterfacePart` that puts controls into the top bar or the bottom panel, a full-screen panel, a command for the selection, or a sentence in the message line. |
| What is under the pointer | `game/ui/UnderPointer.cs` | A `PointerPicker` for each kind of thing that can be pointed at. The answer is one of: a unit of the player's, an enemy unit, a deposit, a structure, the ground. |
| The order table | `game/ui/OrderTable.cs` | An `OrderRow`: for a kind of target, and a modifier or none, the command the selection is given. |

Its orders in the smoke test are a `SmokePart`, a stretch of ticks of its own that runs after the parts before it. The smoke test shows its match through every piece, and it fails on the first error the engine logs while it runs: a piece that throws, when it is added or later, fails it. Warnings do not. Three files are shared by all pieces: `ViewPieces.cs`, the list of parts in `game/scripts/SmokeTest.cs`, and the input map in `game/project.godot`. In each, every piece of the plan has a comment line, and a piece adds its lines directly under its own. Pieces built at the same time then never add at the same place, and their pull requests merge in either order.

| Control | Keys and mouse |
| --- | --- |
| Pan | W A S D or the arrow keys, or move the pointer to a window edge |
| Zoom | Mouse wheel, `=` and `-`, or a trackpad pinch or two-finger scroll |
| Select a unit of your own | Left click on it |
| Select several | Drag a box round them with the left button |
| Add to the selection | Hold Shift and drag a box. Shift and a click adds a unit, or takes it out if it is in |
| Clear the selection | Left click on the ground |
| Send the selected units somewhere | Right click on the ground |
| Show or hide the overlay | F3 |
| Attack a unit of the other player | Right click on it |
| Go somewhere and fight what is met on the way | Hold Ctrl and right click on the place |
| Stop | X, or the Stop button in the selection panel |
| Development aid, while `debug.spawn` is on in `rules.jsonc`: make units at the pointer | G for your own. Alone, H for the other player's. The orange panel at the left edge chooses which and how many |

## Test builds

The Export workflow (`export.yml`) makes the builds that testers get: a zip for Windows x86_64 and a zip for macOS, Intel and Apple silicon in one app. Start it from the Actions tab, or push a tag that starts with `v`, which also names the builds. The zips are artifacts of the workflow run and never a GitHub release. They are not signed. [docs/testers.md](docs/testers.md) tells a tester how to get one and start it, and a copy travels in each zip as `HOW-TO-RUN.txt`.

Godot builds every project as `ExportRelease` for an export. `Directory.Build.props` gives that configuration the optimization of a Release build, and the workflow fails if the exported simulation is not optimized.

## Continuous integration

| Workflow | Runs on | What it does |
| --- | --- | --- |
| `ci.yml` | Every pull request, on Linux | Build with warnings as errors, format check, tests, `datacheck`, the golden replays, Godot import and build, a check that Godot changed or created no file in the working tree, the smoke test, and a network match between two copies of the game on the runner, which must end on the same tick with the same state hash and leave two replays that reproduce. |
| `determinism.yml` | Every push to `main`, every night, a manual run, and pull requests that change anything it builds, runs or reads: `src/`, `tests/` (with the golden replays), `game/data/`, the workflow, `Directory.Build.props`, `global.json`, the solution file or `.gitattributes` | The tests on Linux, Windows, macOS on Apple silicon and macOS on Intel, as a Debug and as a Release build. The tests hold recorded values, so this is where a result that differs between systems shows up. The transport's tests use real sockets, which also differ between systems. Each of the eight jobs then runs the golden replays, and a last job fails unless all eight report the same final hashes. |
| `export.yml` | Every tag that starts with `v`, a manual run, and pull requests that change the workflow, `game/export_presets.cfg` or `Directory.Build.props` | Exports the Windows build on Linux and the macOS build on macOS, and uploads both. Runs the smoke test in a Linux export, in the Windows build on Windows and in the macOS build as an Apple silicon and as an Intel program. Opens the Windows and the macOS build in a window for 300 frames. Fails unless every build reports the game version of the run, the content hash that `datacheck` prints and the same state hash as the others. |

## Layout

| Path | Contents |
| --- | --- |
| `src/Sim/` | The simulation. A plain .NET library that must not reference Godot or any package; a test enforces this. One folder per part: `Core/` (world, entities, tick loop, commands, state hash), `Math/` (fixed-point numbers, exact fractions, angles), `Data/` (the loader for `game/data/`) `Serialization/` (the byte format, the encoding of a command, and snapshots), `Economy/` (stockpiles, territories, deposits and Generators, workers and their places, income and supply) and `Combat/` (hit points, attacks, shots, target picking, the four orders and death). |
| `src/Net/` | The lockstep network session. A plain .NET library that references only `Sim`: `Protocol/` (the messages and their bytes), `Session/` (the host, the client, and the two in one process for single-player), `Transport/` (the connection interface, and the loopback used by single-player and tests), `Lobby/` (a hosted match and a joining player: the description of the match, the version and data check, the draw of starts) and `Files/` (the replay a session writes while it runs). |
| `src/Net.Udp/` | The real network under the session: a connection over UDP, a listener for the host and a connector for a client. A plain .NET library that references `Net` and the LiteNetLib package. Sockets, the package and the thread that keeps connections alive live here and nowhere else. |
| `src/Tools/` | The console tools, as one program: `dotnet run --project src/Tools -- <tool> [arguments]`. Each tool is a folder. So far: `datacheck` checks a data folder, `balance-report` prints what the tech rules make of tier mixes, `replay` runs recorded matches again and checks their state hashes, and `desync-diff` takes the replays and snapshots two players wrote when their match came apart and names the tick and the values that differ. |
| `tests/Sim.Tests/` | Tests for `Sim` (xUnit v3 on Microsoft.Testing.Platform), in folders that match those of `src/Sim/`. |
| `tests/Net.Tests/` | Tests for `Net`: whole matches between a host and several clients in one process, over a loopback with latency and jitter that the test controls. |
| `tests/Net.Udp.Tests/` | Tests for `Net.Udp` over real sockets on the local address, on ports the system picks: what a connection promises, the orderly close, and whole matches between a host and clients, through a relay that delays, reorders and loses datagrams. |
| `tests/golden/` | The golden replays: `replays/` holds recorded matches that every build must reproduce on every system, and `data/` holds the frozen data they run on, which changes to `game/data/` do not touch. [determinism.md](docs/determinism.md) says how to update them. |
| `tests/Tools.Tests/` | Tests for the tools, for the data in `game/data/` and for the schemas. |
| `game/` | The Godot project: `project.godot`, `Game.csproj`, `scenes/`, `scripts/`, `ui/`, `pieces/`. References `Sim` and `Net`. |
| `game/export_presets.cfg` | The export presets: Windows x86_64, macOS universal, and Linux x86_64, which the export workflow uses for its first smoke test. Each packs the data files with the game. |
| `game/data/` | The content of the game as JSON-with-comments files: tracks, rules, resources and the worker, unit lines and maps. |
| `game/ui/` | What the player does to the match, as scripts: the menu and lobby where a match is started alone, hosted or joined; the frame of the interface round a match; the selection, the box and the rings that show it; the one answer to what is under the pointer; and the table that turns a click into an order, with the mark it leaves on the ground. Orders leave through the session of `Net`. |
| `game/pieces/` | The pieces of the view, a folder each: what a piece draws in the world, shows in the interface, lets the pointer find and adds to the order table and to the smoke test. `ViewPieces.cs` lists them. |
| `schemas/` | JSON Schemas for the files in `game/data/`. |
| `docs/` | [determinism.md](docs/determinism.md): the rules that keep the simulation deterministic, the kernel, the byte format, snapshots, replays and the golden replays. [data-authoring.md](docs/data-authoring.md): how the data files are written and checked. [testers.md](docs/testers.md): how a tester gets a build and starts it. |
| `.github/workflows/` | `ci.yml`, `determinism.yml` and `export.yml`, described above. |
| `global.json` | Pins the .NET SDK and the Godot version, and selects the test runner. |
| `Directory.Build.props` | C# settings shared by every project: target framework, nullable, warnings as errors, analyzers, overflow checking, and optimization for exports. |
| `empire-earth-4.sln` | All projects. The Godot editor finds it through `dotnet/project/solution_directory` in `project.godot`. |

A path that is not in this table does not exist yet. Whoever creates one adds its row in the same pull request.

Dependencies point one way: `game` and `Tools` may reference `Ai`, `Net`, `Net.Udp` and `Sim`; `Net.Udp` may reference `Net` and `Sim`, and is the only project that references the LiteNetLib package; `Ai` and `Net` may reference `Sim`; `Sim` references nothing.

## Target framework

Every project targets `net10.0`. This was checked against Godot 4.7.2 on 10 Nov 2026 with .NET SDK 10.0.404:

- The editor builds the project (`--build-solutions`) and the smoke scene runs on the .NET 10 runtime.
- Release exports complete for Windows x86_64, macOS universal and Linux x86_64, each carrying the .NET 10 runtime.
- The Linux export runs headless and passes the smoke test.

Since then every run of `export.yml` starts the exports on GitHub's machines: the Windows build on Windows, the macOS build on Apple silicon and, through Rosetta, as an Intel program. The macOS build is exported on macOS, because macOS 26 refuses to start one exported on Linux. None of this stands in for a tester's own machine.

The target framework is set in two files that must agree: `Directory.Build.props` and `game/Game.csproj`. The Godot editor requires the property in `Game.csproj` itself. If it is missing there, the editor adds `net8.0` to the file when it opens the project.
