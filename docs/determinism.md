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
| 7 | Whole-number overflow is an error, not a wrap. | Partly. See below. |
| 8 | Numbers in data files are whole numbers or decimal strings, read with whole-number arithmetic. | `Fraction.Parse` reads a decimal string exactly. |

Rule 7 is half in place. `Fixed` and `Fraction` check every operation in every build configuration and throw `OverflowException`. The rest of the code is not checked yet: that needs `<CheckForOverflowUnderflow>true</CheckForOverflowUnderflow>` for debug and test builds in `Directory.Build.props`, which has not been added. Until it is, write `checked(...)` around arithmetic that could overflow. Code that wraps on purpose (the random generator, the state hash) says `unchecked`, so it keeps working when the switch is turned on.

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
- **`ComponentStore<T>`** holds one value of a struct type per entity, in arrays kept in ascending ID order. A system walks it by index, oldest entity first. State that does not belong to an entity goes in a class of its own that implements `IStateStore`.
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

A store feeds its fields to the `StateHasher` in `AddToHash`. The hasher takes whole numbers only, and there is no overload for a floating-point value or a string. Feed every field that can change, in a fixed order, entities in ascending ID order. A field left out is a desync nobody will detect.

The hash is fed directly from the stores, with no bytes written in between. Snapshots, which arrive with milestone M6, will need a second walk over the same fields.

## Adding a system

1. Put it in its own folder under `src/Sim/` with its own state type: a struct implementing `IStateHashable` for per-entity data, held in a `ComponentStore<T>`, or a class implementing `IStateStore`.
2. Register the store with `World.AddStore`, under a unique name, before the first tick.
3. Add the system with `Simulation.AddSystem`. Its place in that list is part of the rules of the game: moving it changes results.
4. For a new kind of command, add a line to `CommandType` (alphabetical, with the lowest unused number, never renumbered) and register its handler with `Simulation.SetCommandHandler`.
5. Test it by building a small world, queueing commands, running ticks and asserting on the state. `tests/Sim.Tests/Core/ToyMatch.cs` is a worked example.

## What the tests pin

Some tests hold recorded values: the reference output of the random generator, the hash of known input, and the final state hash of a 600-tick toy match (`DeterminismTests`). They fail when the behaviour of the kernel changes in any way: rounding, the sine table, the order of a tick, the hash. If the change is intended, update the recorded value in the same pull request and say so. If it is not, the change has broken determinism with earlier builds.

These tests run on one operating system. Comparing hashes across Linux, Windows and macOS starts with the golden replays of milestone M6.
