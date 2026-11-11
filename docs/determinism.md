# Determinism

Multiplayer is deterministic lockstep: every machine runs the whole simulation and only player commands cross the network. That works only if every machine computes exactly the same state, bit for bit, on Windows, macOS and Linux, on x86_64 and Apple Silicon. One differing bit is a desync.

This document has the rules that keep the simulation deterministic and describes the kernel in `src/Sim/` that the rest of the simulation is built on.

## The rules

They apply to everything in `src/Sim/`. Code outside it (the Godot project, the AI, tools, tests) may do as it likes, because it can only change the simulation by submitting commands.

| # | Rule | Enforced by |
| --- | --- | --- |
| 1 | No `float`, `double` or `decimal`, and no other floating-point type. | Test. `SimAssemblyTests` scans the compiled `Sim` assembly: fields, properties, signatures, local variables, instructions and everything the code refers to. |
| 2 | No wall-clock time, threads, `async` or environment access inside a tick. Work budgets are counted in operations (for example path nodes expanded per tick), never in milliseconds. | Review. |
| 3 | No `System.Random`. The world owns the only random generator. | Test, by the same scan. |
| 4 | Visit entities in ascending ID order. Never iterate over a hash-based collection (`Dictionary`, `HashSet`). Every sort is a total order with the entity ID as the last tie-break. | Review. `ComponentStore<T>` is always in ID order. |
| 5 | No string hashing. Content IDs become dense integers when data is loaded, assigned in sorted order. `GetHashCode()` of a string, a tuple or an `Int128`, and `HashCode.Combine`, give different values in every process, so no result may depend on them. | Review. |
| 6 | All state that changes during a match lives in the `World`. No static fields that change. | Review. |
| 7 | Whole-number overflow is an error, not a wrap. | The compiler, in Debug builds. `Fixed` and `Fraction` check themselves in every build. See below. |
| 8 | Numbers in data files are whole numbers or decimal strings, read with whole-number arithmetic. | `Fraction.Parse` reads a decimal string exactly. |

Rule 7 has two parts. `Fixed` and `Fraction` check every operation in every build configuration and throw `OverflowException`. All other arithmetic, in every project, is checked by `<CheckForOverflowUnderflow>true</CheckForOverflowUnderflow>` in `Directory.Build.props`, which applies to every build configuration except `Release` and `ExportRelease`. So the tests and a game started from the Godot editor throw on overflow, and an exported release build, which is optimized for speed, wraps. An overflow is therefore a bug for the tests to find; a release build is not protected from it, though every machine running that build wraps alike. Code that wraps on purpose (the random generator, the state hash) says `unchecked`, and behaves the same in every build.

## Numbers

The number types are in `src/Sim/Math/`, in the namespace `Sim.Numerics`.

The namespace is not `Sim.Math` on purpose. A namespace of that name hides `System.Math` in every file under `Sim` and `Sim.Tests`: `Math.Max(a, b)` stops compiling. For the same reason the tests are in `Sim.Tests.Numerics`. Do not add a namespace called `Math` anywhere under `Sim`.

| Type | What it is | Rounding |
| --- | --- | --- |
| `Fixed` | Q16.16 fixed point: a 32-bit integer counting 1/65,536 of a tile. For positions, speeds and ranges. Range ±32,768. | `+`, `-` and multiplying by a whole number are exact. `Fixed * Fixed` and `/` round to the nearest unit, a tie going away from zero, so `(-a) * b == -(a * b)` and mirrored positions behave alike. `Sqrt` rounds down. |
| `Fraction` | An exact fraction of two 128-bit integers, in lowest terms. For the design's multipliers (1.25 is 5/4) and every value derived from them. | None until asked. `RoundHalfUp(scale)` rounds once, a tie going up: scale 1 for a price, 1,000 for thousandths, `SimInfo.TicksPerSecond` for a time in seconds. `ToFixed()` rounds once to 1/65,536. |
| `Angle` | A whole number of 1/4,096 turns, wrapping. | `Sin()` and `Cos()` come from a committed table of 1,025 values, each the true sine rounded to the nearest 1/65,536. |
| `IntegerMath.Sqrt` | Square root of a 64-bit integer. | Rounds down. |

Which number to use, following the technical plan:

- Positions, speeds, ranges and radii: `Fixed`. Compare distances by their squares, as `long`: `dx.Raw * dx.Raw + dy.Raw * dy.Raw`. `IntegerMath.Sqrt` of that sum is the distance in raw `Fixed` units.
- Hit points, damage and stockpiles: `long`, counted in thousandths.
- Prices: whole resource units. Timers: whole ticks.
- Anything the design computes by multiplying factors: build it as a `Fraction` and round once at the end with `RoundHalfUp` or `ToFixed`. Do not round in between, and do not multiply `Fixed` values where the design asks for an exact product.

All of these throw on overflow or division by zero. That is deterministic too: every machine throws at the same point.

## The world

The kernel is in `src/Sim/Core/`, in the namespace `Sim.Core`.

- **`World`** holds all state: the tick number, the random generator, the entities and the registered stores.
- **`EntityId`** is an integer. IDs start at 1, ascend, and are never reused, so a lower ID is always an older entity. `EntityId.None` is 0.
- **`ComponentStore<T>`** holds one value of a struct type per entity, in arrays kept in ascending ID order. The struct implements `IStateValue`. A system walks the store by index, oldest entity first. State that does not belong to an entity goes in a class of its own that implements `IStateStore`.
- **`DeterministicRandom`** (`World.Random`) is a SplitMix64 generator. Every draw changes its state, so draw only inside a tick, from code that runs in a fixed order. Never draw from presentation code.

Destroying an entity is settled in two steps. `Entities.Destroy(id)` makes `IsAlive(id)` false at once. The entity keeps its place and its values in every store until the end of the tick, when they are all removed together. So a system may destroy entities while it walks a store, and positions in the store stay valid for the whole tick. Check `IsAlive` where it matters that something was destroyed earlier in the same tick.

Do not add a value to a store, or remove one directly, while walking that store, and do not hold a `ref` from `Get` or `ValueAt` across an `Add`: both move values. Creating an entity and adding its values is safe while walking, because the newest entity goes to the end.

## A tick

`Simulation.Tick()` runs one tick, in this order:

1. The commands scheduled for this tick, in the order they were queued.
2. Each system, in the order it was added.
3. Destroyed entities are removed from the entity list and from every store.
4. The tick number goes up by one.

Nothing reads a clock. The caller decides when a tick runs; there are `SimInfo.TicksPerSecond` (20) per second of game time. Single-player, network play, replays and tests all go through `Simulation.Tick()`.

## The systems of a match

`MatchSetup.Create` builds a match and adds its systems. After the commands of a tick, they run in this order:

| # | System | What it does | Why it is here |
| --- | --- | --- | --- |
| 1 | `RoutePlanningSystem` | Finds the routes that movers are waiting for, oldest mover first, until the searches of this tick have expanded `ExpansionsPerTick` (4,096) tiles. The rest wait for the next tick. | Before anyone moves, so that a unit ordered in this tick takes its first step in this tick. |
| 2 | `MovementSystem` | Moves every mover one step. | |
| 3 | `SpatialIndexSystem` | Rebuilds the spatial index from the positions as they are now. | Right after movement, so that every system after it sees where things stand. A system that moves bodies goes before it. |

The order is a rule of the game: moving a system changes results, on every machine alike, and so changes every recorded hash. A pull request that adds a system adds a row here, says where the system goes and why, and updates the golden replays.

## Derived data

Some of what a match holds is worked out from the state and is not state: the spatial index and the route cache. Such data is not walked, so it is in neither the state hash nor a snapshot. That is safe only under one rule:

**Derived data is a function of the state and of the match's fixed data, and of nothing else.** The same state gives the same answers, in the same order, whatever happened before.

- No history. An answer may not depend on what was asked earlier, on how long the match has run, or on whether the data was kept up to date step by step or rebuilt from nothing. The route cache may drop a route at any moment because the route found again is the same route, and the route budget charges a route its search cost whether or not the cache still had it.
- Nothing in the state may depend on how much derived data there is: on what a cache holds, how full it is or how many searches have run.
- `Match.RefreshDerivedState()` rebuilds whatever must agree with the world before the next tick. Everything that changes the world from outside a tick calls it: `Snapshot.Restore(match, reader)` does, and a test that places units does.
- New derived data is added to `RefreshDerivedState`, and to the comparison in `SnapshotAssert` that checks a restored match answers as the match it was taken from.

## Commands

A `Command` is the only way anything outside the simulation changes it. It is immutable data: a type, the player, a list of entities, and a target position, a target entity or a content ID.

- Whoever orders commands (the host, the local session, a replay) schedules each for a tick with `Simulation.Commands.Enqueue(tick, command)`. Commands for one tick run in the order they were queued, so that order must be the same on every machine. Scheduling a command for a tick that has already run throws.
- Each command type has one `ICommandHandler`. It checks the command against the world as it is when the command runs (ownership, affordability, placement) and returns `false`, having changed nothing, if the command is not valid. The command is then dropped, on every machine alike. A type with no handler is dropped too.
- The queue is input, not state. It is not in the state hash, because peers may hold different amounts of the future when they compare hashes.

## The state hash

`World.ComputeHash()` returns a 64-bit hash of the whole state: the tick, the random generator, the entity list, then every store in the ordinal order of its name. Peers compare it to detect a desync. The order in which stores were registered does not change it.

The hash is not computed over bytes. It comes from the walk described next, which hands every field to a `StateHasher` as a whole number.

## One walk over the state

Every store has one method, `Walk(IStateWalker walker)`, that hands each of its fields to the walker and takes back the value the walker returns:

```csharp
public struct Unit : IStateValue
{
    public int Owner;
    public Fixed Speed { get; set; }
    public Stance Stance { get; set; }
    public Point Target { get; set; }

    public void Walk(IStateWalker walker)
    {
        Owner = walker.Field(Owner);
        Speed = walker.Field(Speed);
        Stance = (Stance)walker.Field((int)Stance);
        Target = new Point(walker.Field(Target.X), walker.Field(Target.Y));
    }
}
```

The same walk serves three purposes, each with its own walker:

| Purpose | What the walker does with a field | What it returns |
| --- | --- | --- |
| The state hash, `World.ComputeHash()` | Feeds it to the hash | The value it was given |
| Writing a snapshot, `Snapshot.Write` | Writes it as bytes | The value it was given |
| Restoring a snapshot, `Snapshot.Restore` | Ignores it | The value read from the snapshot |

Because there is one walk, a field cannot be in the hash and missing from a snapshot, or the other way round. The rules for a walk:

- **Walk every field that can change during a match**, always in the same order, entities in ascending ID order and never in the order of a hash-based collection. A field left out is in neither the hash nor a snapshot.
- **Always assign the result back.** `walker.Field(Speed);` on its own hashes and writes correctly and restores nothing.
- **A walker takes whole numbers and the kernel's number types**: `bool`, `int`, `uint`, `long`, `ulong`, `Int128`, `EntityId`, `Fixed`, `Angle` and `Fraction`. There is no method for a floating-point value or for text. Cast an enum to `int` and back, and walk the parts of a compound value one by one and build it again from the results, as `Stance` and `Target` are above. Fields and properties work alike.
- **A store whose number of items changes** walks the number first, with `walker.Count`, and makes room for it when `walker.IsRestoring` is true:

```csharp
public void Walk(IStateWalker walker)
{
    var count = walker.Count(_entries.Count, minimumBytesPerItem: sizeof(long));
    if (walker.IsRestoring)
    {
        _entries.Clear();
        _entries.AddRange(new long[count]);
    }

    for (var i = 0; i < count; i++)
    {
        _entries[i] = walker.Field(_entries[i]);
    }
}
```

  `minimumBytesPerItem` is the least one item can take in a snapshot: four bytes for each `int`, `EntityId` or `Fixed`, eight for each `long`, one for each `bool`. A damaged snapshot then cannot announce more items than it has bytes for. `ComponentStore<T>` does all of this itself.
- A walk that finds a restored value impossible throws `InvalidDataException`.
- The last parameter of `Field` and `Count` is the text of the argument, which the compiler fills in. Leave it out. A tool that shows or compares two states uses it to name each field.

`World.Walk(walker)` walks the whole state: the tick, the random generator, the entity list, the number of stores, then every store in name order. Anything else that needs every field, such as a tool that compares two worlds, implements `IStateWalker` and calls it.

## Snapshots

`Snapshot.Write(world, writer)` writes the whole state as bytes: the letters `SNAP`, a layout version, the names of the stores, every field the walk visits, and last the state hash. `Snapshot.Restore(match, reader)` reads it back into a match, and `Snapshot.Restore(simulation, reader)` into a simulation that is not part of one.

- **A snapshot is state and nothing else.** Systems, command handlers and data are not in it. To restore, build the match the same way again and read the snapshot into it. Restoring into a world with other stores is refused, by name.
- **The command queue is not in it.** Restoring empties the queue and makes the snapshot's tick the next to run; queue again what should run from there. Whatever needs the waiting commands kept, such as a save game, writes them beside the snapshot with `CommandQueue.CopyPendingTo` and `CommandEncoding`.
- **A snapshot checks itself.** After reading, `Restore` hashes the world and compares the result with the hash at the end of the snapshot. Damage anywhere, and a walk that does not assign its results back, both end in `InvalidDataException`. The world is then half replaced and must not be used.
- **Restoring leaves nothing behind.** Whatever the world held before is gone, including values in fields a walk forgot.
- **What is worked out from the state is not in a snapshot.** The spatial index and the route cache are such data (see "Derived data"). Restore a match with `Snapshot.Restore(match, reader)`, which restores its simulation and then calls `Match.RefreshDerivedState()`. Everything that restores a match goes through that one method; restoring only the simulation of a match leaves its spatial index answering for the old world.
- Take a snapshot between ticks.

What neither the hash nor a snapshot can notice is a field that is left out of the walk altogether. One test per world catches that, by taking a snapshot in the middle of a run, restoring it and continuing beside the run that was never interrupted:

```csharp
SnapshotAssert.RestoresAndContinues(BuildTheMatch, script, snapshotTick: 40, lastTick: 200);
```

`SnapshotAssert` is in `tests/Sim.Tests/Serialization/`. It takes a function that builds the match, or one that builds a `Simulation` for a world that is not a match. It checks three things:

- The run continued from the restored world has the state hash of the uninterrupted run after every tick. This catches a missing field that the rules read, and state kept outside the stores.
- Every value in every `ComponentStore<T>` of the restored world equals, field for field, the value in the world the snapshot was taken from. This catches a missing field that no rule reads yet, such as the way a unit faces before anything turns to shoot. Nothing else would: the hash leaves the field out too.
- For a match, the spatial index of the restored match lists the same entities as that of the match the snapshot was taken from, for every square of the map. This catches a restore that does not refresh derived data.

Every system adds this test for a run in which its own stores change before and after the snapshot, with the snapshot taken in each state its values can be in. `MatchSnapshotTests` in the same folder does so for movement: units standing, waiting for a route, walking a route, walking straight and arrived.

## Bytes

Everything that puts simulation data on bytes uses one format, in `src/Sim/Serialization/` (namespace `Sim.Serialization`): encoded commands, snapshots, the description of a match, replay files and the messages of network play.

- **`ByteWriter`** appends values to a buffer and **`ByteReader`** reads them back in the same order. The format is a plain sequence of values: no tags, no padding, and no lengths except the counts described here.
- Whole numbers are little-endian at a fixed width, so the bytes are the same on every machine. Each method names its width (`WriteInt32`, `WriteUInt16`); nothing is chosen by overload.

| Value | Bytes |
| --- | --- |
| `byte`, `bool` | 1. A `bool` is 1 or 0. |
| `ushort` | 2 |
| `int`, `uint`, `EntityId`, `Fixed` (its raw value) | 4 |
| `long`, `ulong` | 8 |
| `Int128` | 16: the low 64 bits, then the high 64 bits |
| `Angle` | 2: its units, from 0 to 4,095 |
| `Fraction` | 32: numerator, then denominator |
| A count | 4. Never negative. |
| Text | A count of bytes, then the text as UTF-8 |

- **Every value has exactly one encoding.** The reader rejects every other form: a `bool` byte of 2, an angle of 4,096 or more, a fraction that is not in lowest terms, text that is not valid UTF-8. Bytes that were read without an error therefore write back as the same bytes, and two values are equal exactly when their bytes are.
- **Nothing about the input is trusted.** Bytes come from other machines and from files. Whatever is wrong with them, the reader throws `InvalidDataException` and nothing else. `ReadCount` takes the smallest size one item can have and rejects a count that could not fit in the bytes that are left, so a caller may allocate for the count it gets.
- After the last value of a message or a file, call `ExpectEnd()`: bytes left over are an error too.

**`CommandEncoding`** is the one encoding of a `Command`: 26 bytes plus 4 for each entity, laid out in the comment on the class. It keeps every value a command can hold and judges none of them. An unknown type or an entity that does not exist reads back as written, and the simulation drops such a command when it runs, as it does for one that never left the machine. A command does not carry its tick; whatever holds commands says which tick they are for. `CommandEncoding.Version` goes up whenever the layout changes, including when `Command` gains a field. Do not write a second encoding of a command anywhere.

## The description of a match

`MatchDescription` holds what every machine needs to build the same match: the game version, the content hash of the data, the map ID, the seed, and the players with their starts. It has one encoding, written with `ByteWriter`: a replay file begins with it, and the host of a network match sends the same bytes to every client.

```text
2 bytes    the layout version, MatchDescription.Version
text       the game version
32 bytes   the content hash of the data
text       the ID of the map
8 bytes    the seed
a count    the number of players, 1 to 8
4 bytes    for each player, the index of its start in the map's list, or -1 for none
```

- `description.FindProblem(data)` says in a sentence why the match cannot be built from the data a machine has (other data, no such map, a start the map does not have), or returns null. `description.CreateMatch(data)` builds the match with `MatchSetup.Create`, or throws `InvalidDataException` with that sentence.
- The content hash decides whether a match can be built. The game version is text that the description only carries. Network peers refuse each other when theirs differ. A replay is run by whatever build reads it, because showing that a later build still computes the same match is what a recorded replay is for.
- A player's number is its position in the list. Do not write a second encoding of these values anywhere.

## Replays

A replay is a recorded match: its description, the commands of every tick, and state hashes taken on the way. Running it again builds the match from the description, feeds it the commands and compares the state with every recorded hash, so a replay verifies itself. It holds no state; it is small, and a build that computes anything differently fails it at the first hash after the difference.

```text
4 bytes   the letters RPLY
2 bytes   the layout version, Replay.Version
2 bytes   CommandEncoding.Version
2 bytes   the hash interval: a state hash is recorded every so many ticks, at least 1
...       the match, as MatchDescription writes it
then records, to the end of the file. Each starts with one byte that says which it is:
  1  commands   4 bytes: the tick they run in. A count, at least 1, then each command as
                CommandEncoding writes it, in the order they run.
  2  hash       4 bytes: a number of ticks. 8 bytes: the state hash when that many had run.
  3  end        4 bytes: the number of ticks the match ran. 8 bytes: its final state hash.
                Nothing follows it.
```

- The hash records are those of tick 0, which is the match as it was built, and of every multiple of the hash interval the match reached, in order and none left out. The recorder's interval is `SimInfo.TicksPerSecond` unless it is told another: one hash for each second of game time.
- Commands records come in ascending order of their ticks. Commands and hashes are not sorted against each other, because a network session learns the commands of a tick some ticks before the tick runs and records them then.
- The end record comes less than one hash interval after the last hash. A file without one is the replay of a recording that stopped, because the game was closed or failed; it runs to its last hash, and on for as long as it has commands, but less than one interval more. These two rules bound the ticks a replay asks for by the size of its file, so a damaged file cannot ask for a run without end.
- Commands for ticks at or after the end were scheduled and never ran. They stay in the file and are not run.

**`ReplayRecorder`** writes a replay while the match runs. Create it when the match is built and before its first tick; it writes the header and the hash of tick 0. Then call `RecordCommands(tick, commands)` when the commands of a tick are known, in tick order and before that tick runs; `AfterTick()` after every tick; and `Finish()` when the match ends. It reads the world and changes nothing. It only ever appends to its `ByteWriter`, so the bytes written so far can be moved to a file at any moment (write out `WrittenSpan`, then `Clear()`), and a file that stops anywhere is still a replay of what came before.

**`Replay.Read(reader)`** reads a replay and rejects, with `InvalidDataException`, anything that breaks the rules above. `Replay.Read(reader, keepWhatIsWhole: true)` instead keeps every record before the first one that is damaged or cut short and sets `DamagedAt`, for the file of a game that failed while writing. **`replay.Run(data)`** builds the match and runs it; the `ReplayResult` has the ticks run, the final hash, the number of hashes compared, and the first `ReplayMismatch` (the tick, the recorded hash and the computed one) or null. It stops at the first mismatch. **`replay.RecordAgain(match, description, writer)`** runs the commands in a match built from other rules or other data and writes the replay of that run.

The `replay` tool does this from the command line, with no Godot:

```sh
dotnet run --project src/Tools -- replay verify <file or folder>... [--data <folder>]
dotnet run --project src/Tools -- replay update <file or folder>... [--data <folder>]
dotnet run --project src/Tools -- replay record <file> --map <id> [--data <folder>] [--starts 0,1] [--seed <n>] [--ticks <n>] [--orders <n>]
```

`verify` prints one line for each replay (`ok`, `PARTIAL` for a file that is cut short or has no end record, or `FAILED` with the tick and both hashes) and exits with 1 unless every replay is whole and reproduced. A folder is searched for files that end in `.replay`. The lines name no folder and are the same on every system. `update` and `record` are for golden replays, below. Replays are tooling for developers: the game has no screen for them.

## Golden replays

`tests/golden/replays/` holds recorded matches that every build must reproduce, and `tests/golden/data/` holds the data they were recorded with. `ci.yml` verifies them for every pull request:

```sh
dotnet run --project src/Tools -- replay verify tests/golden/replays --data tests/golden/data
```

`determinism.yml` does the same on Linux, Windows, macOS on Apple silicon and macOS on Intel, as a Debug and as a Release build, and a last job fails unless all eight reports are the same. So the final hash of every golden replay must match the value stored in the file and must be the same on every system.

- **The data is frozen.** `tests/golden/data/` is a copy of `game/data/` from when the replays were last updated, plus the map `switchback`, which was made for these tests: long routes whose searches use up the route budget of a tick. Balance changes in `game/data/` do not touch it, so they do not change the golden replays. The folder must satisfy the loader like any data folder; when the loader comes to need a new file or a new value, add it there in the same pull request, and update the replays if their hashes change.
- **The matches are scripted.** `replay record` plays a match with orders from a seeded generator (`ScriptedOrders` in `src/Tools/Replay/`): groups and single units sent across the map, every unit ordered separately in one tick, short hops, targets on blocked ground and off the map, and commands the simulation must drop. Nobody needs to play a match to record one. A test in `ReplayToolTests` lists what the golden replays must reach between them, such as a unit that waits more than a tick for its route, and fails when they no longer do.
- **When a golden replay fails and the change was not meant to change the rules,** the change has broken determinism with earlier builds: fix the change. `verify` names the first tick whose hash differs, within one second of game time of the cause.
- **When the change was meant to change the rules** (a system added or moved, a rule or a number in the frozen data changed, a field added to the state), update the stored hashes in the same pull request and say so in its description:

  ```sh
  dotnet run --project src/Tools -- replay update tests/golden/replays --data tests/golden/data
  ```

  `update` runs the commands of each replay under the rules as they are now and writes the file again with the new hashes and the content hash of the data folder. The commands stay as they were. If they no longer exercise the game (the units they name no longer exist, say), record a new replay instead.
- **A milestone that adds rules adds a replay that reaches them:** it adds its commands to `ScriptedOrders`, after the kinds that are there, records a replay with `replay record`, and adds what the replay must reach to the list in `ReplayToolTests`. The replays there now were recorded with:

  ```sh
  replay record tests/golden/replays/ridge-skirmish.replay --data tests/golden/data --map ridge --starts 0,1 --seed 1 --orders 1 --ticks 3600
  replay record tests/golden/replays/switchback-crossing.replay --data tests/golden/data --map switchback --starts 1,0 --seed 2 --orders 3 --ticks 6000
  replay record tests/golden/replays/switchback-three-players.replay --data tests/golden/data --map switchback --starts 0,-,1 --seed 3 --orders 5 --ticks 2410
  ```

  Recording again with the same arguments gives the same file only while `ScriptedOrders` and the rules are unchanged.

## Adding a system

1. Put it in its own folder under `src/Sim/` with its own state type: a struct implementing `IStateValue` for per-entity data, held in a `ComponentStore<T>`, or a class implementing `IStateStore`. Either way its `Walk` hands every field to the walker.
2. Register the store with `World.AddStore`, under a unique name, before the first tick.
3. Add the system with `Simulation.AddSystem`. Its place in that list is part of the rules of the game: moving it changes results. For a system of a match, add a row to the table in "The systems of a match".
4. For a new kind of command, add a line to `CommandType` (alphabetical, with the lowest unused number, never renumbered) and register its handler with `Simulation.SetCommandHandler`.
5. Test it by building a small world, queueing commands, running ticks and asserting on the state. `tests/Sim.Tests/Core/ToyMatch.cs` is a worked example.
6. Add the snapshot test for it: one line with `SnapshotAssert.RestoresAndContinues`, described above.
7. Data it works out from the state follows the rule in "Derived data".
8. Update the golden replays, whose hashes it changes, and add a replay that reaches its rules: see "Golden replays".

## What the tests pin

Some tests hold recorded values: the reference output of the random generator, the hash of known input, the final state hash of a 600-tick toy match (`DeterminismTests`), and the bytes of an encoded command, of a small snapshot and of a match description. They fail when the behaviour of the kernel changes in any way: rounding, the sine table, the order of a tick, the hash, the byte format. If the change is intended, update the recorded value in the same pull request and say so. If it is not, the change has broken determinism with earlier builds.

CI runs these tests on Linux for every pull request. The workflow `determinism.yml` runs them on Linux, Windows and macOS, on x86_64 and on Apple silicon, as a Debug and as a Release build: on every push to `main`, every night, and for a pull request that changes the workflow or the build settings. A recorded value that one system computes differently fails there.

The golden replays add whole recorded matches to both checks. The pinned values above guard the kernel; the golden replays guard the rules of a match built by `MatchSetup`, and they are the values that a deliberate change to those rules updates.
