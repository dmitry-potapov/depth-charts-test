# Depth Charts

Event-sourced sports depth charts on .NET 10 (Clean Architecture).

## Prerequisites

- .NET 10 SDK
- Docker (for Kafka)

## Running it

1. Start Kafka:

   ```sh
   docker compose up -d
   ```

2. Build and run the tests (optional; the Kafka integration tests need Docker running):

   ```sh
   dotnet build
   dotnet test
   ```

3. Start the Api (the Processor) in one terminal. It consumes commands from Kafka and serves charts on `http://localhost:5050`:

   ```sh
   dotnet run --project src/DepthCharts.Api
   ```

4. In a second terminal, run the demo through the Producer CLI:

   ```sh
   dotnet run --project src/DepthCharts.Producer -- demo
   ```

   It uses sport `NFL` and team `demo-team`, and prints exactly:

   ```
   sport: NFL, team: demo-team
   WR: [2, 1, 3]
   KR: [1]
   below Alice WR: [1, 3]
   ```

   To check it yourself, query the same chart: `dotnet run --project src/DepthCharts.Producer -- chart NFL demo-team`, or `curl http://localhost:5050/depth-charts/NFL/demo-team`.

5. When done, stop the Api with Ctrl+C and Kafka with `docker compose down`.

## Examples

The Producer CLI publishes add/remove commands to Kafka and reads charts back from the Api:

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
```

The Api can also be queried directly:

```sh
curl http://localhost:5050/depth-charts/NFL/tb
curl http://localhost:5050/depth-charts/NFL/tb/positions/QB/players/12/below
```

## Documentation

> **Deliberately overengineered.** Kafka, event sourcing and the two-process split are there to demonstrate working with Kafka and event sourcing. For real-world use, this could be a regular CRUD application over a database. See [why it is built like this](docs/architecture.md#why-it-is-built-like-this).

- [docs/architecture.md](docs/architecture.md): the current state of the code: assumptions, limits, domain rules, use cases, Kafka consumer, event store, [endpoints](docs/architecture.md#endpoints), [Producer CLI behaviour](docs/architecture.md#producer-cli-behaviour) (validation, `--as-of` formats, exit codes), [configuration](docs/architecture.md#configuration) and tests.
- [Original tech spec](docs/superpowers/specs/2026-10-27-event-sourced-depth-charts-design.md): the initial design and its reasoning.
- [docs/implementation-plans](docs/implementation-plans/): the plan for each stage.
