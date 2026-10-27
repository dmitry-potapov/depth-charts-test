# Depth Charts

Event-sourced sports depth charts on .NET 10 (Clean Architecture). A `Producer` CLI publishes add/remove commands to Kafka; the `Api` (the Processor) consumes them into an in-memory event store and serves read-only HTTP endpoints. The Domain and Application layers hold the rules.

> **Deliberately overengineered.** Kafka, event sourcing and the two-process split are there to demonstrate working with Kafka and event sourcing. For real-world use, this could be a regular CRUD application over a database. See [docs/architecture.md](docs/architecture.md#why-it-is-built-like-this).

## Prerequisites and running

.NET 10 SDK and Docker (for Kafka).

```sh
docker compose up -d                                  # Kafka
dotnet run --project src/DepthCharts.Api              # Processor, http://localhost:5000
dotnet run --project src/DepthCharts.Producer -- demo
```

`demo` prints exactly:

```
WR: [2, 1, 3]
KR: [1]
below Alice WR: [1, 3]
```

## Producer CLI

```sh
dotnet run --project src/DepthCharts.Producer -- <verb> ...
```

```
producer add    <sport> <team> <position> <playerId> <name> [depth]
producer remove <sport> <team> <position> <playerId>
producer chart  <sport> <team> [--as-of <iso8601>]
producer below  <sport> <team> <position> <playerId> [--as-of <iso8601>]
producer demo
```

```sh
producer add NFL tb QB 12 "Tom Brady" 0     # quote names with spaces
producer remove NFL tb QB 12
producer chart NFL tb --as-of 2026-10-27T10:00:00+02:00
producer below NFL tb QB 12
producer demo
```

Validation, `--as-of` formats, exit codes and configuration: see [Producer CLI behaviour](docs/architecture.md#producer-cli-behaviour).

## Commands

```sh
dotnet build
dotnet test
dotnet run --project src/DepthCharts.Api
dotnet run --project src/DepthCharts.Producer -- <verb> ...
```

## Documentation

- [docs/architecture.md](docs/architecture.md): the current state of the code: assumptions, limits, domain rules, use cases, Kafka consumer, event store, endpoints, configuration and tests.
- [Original tech spec](docs/superpowers/specs/2026-10-27-event-sourced-depth-charts-design.md): the initial design and its reasoning.
- [docs/implementation-plans](docs/implementation-plans/): the plan for each stage.
