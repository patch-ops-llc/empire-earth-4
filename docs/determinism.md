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

`Snapshot.Write(world, writer)` writes the whole state as bytes: the letters `SNAP`, a layout version, the names of the stores, every field the walk visits, and last the state hash. `Snapshot.Restore(simulation, reader)` reads it back.

- **A snapshot is state and nothing else.** Systems, command handlers and data are not in it. To restore, build the match the same way again and read the snapshot into it. Restoring into a world with other stores is refused, by name.
- **The command queue is not in it.** Restoring empties the queue and makes the snapshot's tick the next to run; queue again what should run from there. Whatever needs the waiting commands kept, such as a save game, writes them beside the snapshot with `CommandQueue.CopyPendingTo` and `CommandEncoding`.
- **A snapshot checks itself.** After reading, `Restore` hashes the world and compares the result with the hash at the end of the snapshot. Damage anywhere, and a walk that does not assign its results back, both end in `InvalidDataException`. The world is then half replaced and must not be used.
- **Restoring leaves nothing behind.** Whatever the world held before is gone, including values in fields a walk forgot.
- **What is worked out from the state is not in a snapshot.** The spatial index and the route cache are such data: the same state always gives the same answers, so they are neither hashed nor written. After restoring into a match, call `Match.RefreshDerivedState()`, which rebuilds what must agree with the world before the next tick.
- Take a snapshot between ticks.

What neither the hash nor a snapshot can notice is a field that is left out of the walk altogether. The restored run then goes its own way some ticks later, and one test per world catches that, by taking a snapshot in the middle of a run, restoring it and continuing beside the run that was never interrupted:

```csharp
SnapshotAssert.RestoresAndContinues(BuildTheMatch, script, snapshotTick: 40, lastTick: 200);
```

`SnapshotAssert` is in `tests/Sim.Tests/Serialization/`. It takes a function that builds the match, or one that builds a `Simulation` for a world that is not a match. Every system adds this test for a run in which its own stores change after the snapshot, with the snapshot taken in each state its values can be in. `MatchSnapshotTests` in the same folder does so for movement: units standing, waiting for a route, walking a route, walking straight and arrived.

## Bytes

Everything that puts simulation data on bytes uses one format, in `src/Sim/Serialization/` (namespace `Sim.Serialization`): encoded commands and snapshots now, replay files and network messages later.

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

## Bytes

Everything that puts simulation data on bytes uses one format, in `src/Sim/Serialization/` (namespace `Sim.Serialization`): encoded commands now, snapshots, replay files and network messages later.

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

## Adding a system

1. Put it in its own folder under `src/Sim/` with its own state type: a struct implementing `IStateValue` for per-entity data, held in a `ComponentStore<T>`, or a class implementing `IStateStore`. Either way its `Walk` hands every field to the walker.
2. Register the store with `World.AddStore`, under a unique name, before the first tick.
3. Add the system with `Simulation.AddSystem`. Its place in that list is part of the rules of the game: moving it changes results.
4. For a new kind of command, add a line to `CommandType` (alphabetical, with the lowest unused number, never renumbered) and register its handler with `Simulation.SetCommandHandler`.
5. Test it by building a small world, queueing commands, running ticks and asserting on the state. `tests/Sim.Tests/Core/ToyMatch.cs` is a worked example.
6. Add the snapshot test for it: one line with `SnapshotAssert.RestoresAndContinues`, described above.

## What the tests pin

Some tests hold recorded values: the reference output of the random generator, the hash of known input, the final state hash of a 600-tick toy match (`DeterminismTests`), and the bytes of an encoded command and of a small snapshot. They fail when the behaviour of the kernel changes in any way: rounding, the sine table, the order of a tick, the hash, the byte format. If the change is intended, update the recorded value in the same pull request and say so. If it is not, the change has broken determinism with earlier builds.

CI runs these tests on Linux for every pull request. The workflow `determinism.yml` runs them on Linux, Windows and macOS, on x86_64 and on Apple silicon, as a Debug and as a Release build: on every push to `main`, every night, and for a pull request that changes the workflow or the build settings. A recorded value that one system computes differently fails there. Golden replays, which arrive later in milestone M6, add whole recorded matches to the same check.
