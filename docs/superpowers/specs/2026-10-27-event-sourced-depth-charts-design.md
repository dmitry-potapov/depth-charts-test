# Event-Sourced Depth Charts — Tech Spec

Date: 2026-10-27
Status: Draft, awaiting review

## 1. Goal

Implement the four depth chart use cases from `brief.md`:

- add a player at a depth, with insert-and-bump
- remove a player
- get the full chart
- get the players under a player

The design has these properties:

- It uses **event sourcing**, so the full change history is kept and a chart can be rebuilt as of any point in time.
- It uses an **EF Core InMemory** event store.
- It runs as **two processes**: a Producer CLI that sends commands over **Kafka**, and a Processor that consumes them and serves reads over **REST**.

Brief item 5 says not to spend effort on a database-backed solution. Persistence and transport are added here on purpose, but the depth chart logic stays pure domain code that can be tested without Kafka or EF.

### Decisions taken during design

| Topic | Decision |
|---|---|
| Kafka role | Kafka carries **commands** only. The EF event store is the source of truth. |
| Reads | The Processor exposes REST endpoints and the Producer calls them. |
| Aggregate | One `DepthChart` per team, keyed by `(sport, team)`. |
| Sports and positions | **Not restricted to a list.** Sport (letters and digits) and position are free-form identifiers, so adding a sport takes no code. NFL and MLB work because nothing blocks them. |

## 2. Solution structure

The dependency direction is Domain ← Application ← Infrastructure ← Api/Producer.

| Project | Contents |
|---|---|
| `src/DepthCharts.Domain` | `Sport`, `Team`, `Position`, `Player`, the `DepthChart` aggregate and its domain events. No package references. |
| `src/DepthCharts.Application` | Command handlers, query handlers, the `IEventStore` and `ICommandPublisher` interfaces, and the message contracts (`AddPlayerCommand`, `RemovePlayerCommand`). |
| `src/DepthCharts.Infrastructure` | `EventStoreDbContext` (EF Core InMemory), `EfEventStore`, `KafkaCommandPublisher`, `KafkaCommandConsumer` (`BackgroundService`) and `DependencyInjection.cs`. Packages: `Microsoft.EntityFrameworkCore.InMemory`, `Confluent.Kafka`, `Microsoft.Extensions.Hosting.Abstractions`. |
| `src/DepthCharts.Api` (new) | The **Processor**: an ASP.NET Core minimal API host that runs the consumer and maps the read endpoints. |
| `src/DepthCharts.Producer` (renamed from `DepthCharts.Console`) | The CLI. It publishes commands through `ICommandPublisher` and queries the Api through `HttpClient`. |
| `tests/DepthCharts.Tests` | Unit, store and endpoint tests. Adds references to Infrastructure and Api, plus `Microsoft.AspNetCore.Mvc.Testing`. |
| `docker-compose.yml` | A single `apache/kafka` broker in KRaft mode, plaintext on `localhost:9092`, with no auth. |

## 3. Domain

### Value types

Each value type enforces its own invariants and throws in its constructor.

- `Sport`: a non-empty string, trimmed and upper-cased (`NFL`, `MLB`, …). It may contain only `[A-Z0-9]`, because it is part of the stream id.
- `Position`: a non-empty string, trimmed and upper-cased (`WR`, `1B`, …). It is not checked against the sport.
- `Team`: a non-empty string, trimmed and lower-cased. It may contain only `[a-z0-9-]`, which keeps `:` out of stream ids so they can't collide.
- `Player(int Id, string Name, Position? NaturalPosition)`: the id must be positive and the name non-empty. The natural position is informational only, because a player may appear on any position's chart.

### Aggregate: `DepthChart`

- Identity: `(Sport, Team)`. The stream id is `"{sport}:{team}"` in lower case, for example `nfl:tb`.
- State: `Dictionary<Position, List<Player>>`, where the list order is the depth.
- `Version`: the number of events applied.

Commands are methods that validate and return new events. They do not mutate state directly.

- `AddPlayer(Player, Position, int? depth, DateTimeOffset occurredAt)`
  - `depth < 0` throws.
  - If `depth` is null or greater than the current count, the player is added at the end.
  - If a player already holds that depth, they and everyone below move down one place. The last player entered gets the spot.
  - If the player is already on that position's chart, they are moved: removed, then inserted at the new depth, which is resolved against the list *without* them.
  - Emits `PlayerAddedToDepthChart(Sport, Team, Player, Position, ResolvedDepth, OccurredAt)`.
- `RemovePlayer(int playerId, Position, DateTimeOffset occurredAt)`
  - A non-positive `playerId` throws.
  - If the player is not on that chart, nothing happens and no event is emitted.
  - Otherwise emits `PlayerRemovedFromDepthChart(Sport, Team, int PlayerId, Position, OccurredAt)`.
- `Apply(event)` mutates state. Replaying a stream means calling `Apply` for each event in order.
  - An event whose `Sport` or `Team` differs from the chart's, or of an unknown type, throws `InvalidOperationException` and leaves the state unchanged.
  - A recorded depth outside `0..count` is clamped, so a filtered or corrupt stream can't lose players.
  - Adding a player replaces their stored name at every position they hold, so the latest event wins.

Queries are pure reads of the state:

- `GetFullChart()` returns `IReadOnlyDictionary<Position, IReadOnlyList<Player>>`. Positions appear in the order they were first added, and positions with no players are left out.
- `GetPlayersUnder(int playerId, Position)` returns the players after this one in the list, or `[]` if the player or position is absent. A non-positive `playerId` throws.

### Events

Events are immutable records that implement `IDomainEvent { Sport Sport; Team Team; DateTimeOffset OccurredAt }`.

- Each event records the **resolved** depth, so replaying it is deterministic and never re-runs the defaulting rules.
- Each event carries its own `Sport` and `Team`, so history can later be filtered by team without parsing stream ids. `Apply` checks both against the chart's identity and rejects a mismatch, so a wrong stream can't silently build the wrong team's chart.

## 4. Application

```csharp
public interface IEventStore
{
    Task<IReadOnlyList<IDomainEvent>> LoadAsync(string streamId, DateTimeOffset? asOf, CancellationToken ct);
    Task<bool> HasProcessedAsync(Guid commandId, CancellationToken ct);
    Task AppendAsync(string streamId, int expectedVersion, Guid commandId,
                     IReadOnlyList<IDomainEvent> events, CancellationToken ct);
}

public interface ICommandPublisher
{
    Task PublishAsync(DepthChartCommand command, CancellationToken ct);
}
```

### Message contracts

The contracts are shared by the Producer and the Processor and serialised as JSON with a `type` discriminator.

- `AddPlayerCommand(Guid CommandId, string Sport, string Team, string Position, int PlayerId, string PlayerName, string? PlayerPosition, int? Depth)`
- `RemovePlayerCommand(Guid CommandId, string Sport, string Team, string Position, int PlayerId)`

### Handlers

`DepthChartCommandHandler.HandleAsync(command, occurredAt, ct)` works in four steps:

1. If `HasProcessedAsync(commandId)` is true, it returns without doing anything.
2. It builds the value types. An invalid value throws a domain validation exception.
3. It loads the stream, replays it into a `DepthChart`, and calls the command method.
4. It always calls `AppendAsync(streamId, chart.Version, commandId, events)`, even when `events` is empty, so a no-op command is still recorded as processed and a redelivery can't run against later state.

There are two query handlers:

- `GetFullDepthChartQuery(sport, team, asOf?)`
- `GetPlayersUnderQuery(sport, team, position, playerId, asOf?)`

Each one loads the stream with `asOf`, replays it, and reads the result. A stream with no events is an empty chart.

## 5. Infrastructure

### Event store (EF Core InMemory)

`StoredEvent { long Id; string StreamId; int Version; string EventType; string Data; DateTimeOffset OccurredAt; Guid CommandId }`

`ProcessedCommand { Guid CommandId }` is a separate table with one row per handled command, including commands that emitted no events. `HasProcessedAsync` reads it.

- `LoadAsync` filters by `StreamId`, and also by `OccurredAt <= asOf` when `asOf` is given. It orders by `Version` and deserialises each event by `EventType`.
- `AppendAsync` checks in code that the stream's current max version equals `expectedVersion`. If it doesn't, the call throws a `ConcurrencyException` and records nothing, so the command stays retryable. Otherwise it writes the events as versions `expectedVersion+1 …` and the `ProcessedCommand` row in the same `SaveChangesAsync`.
- `shortcut:` the InMemory provider enforces neither unique indexes nor transactions. Correctness relies on the single, sequential consumer described in §5.2. To upgrade, move to a relational provider with a unique `(StreamId, Version)` index and a unique `ProcessedCommand.CommandId`. Keep the processed-commands table, or a redelivered no-op command would run again.

### Kafka

- Topic: `depth-chart-commands`. It is created by the compose file or auto-created.
- **Publisher:** the message key is the stream id, which gives per-team ordering through a single partition per key. The value is the JSON command. The Kafka message timestamp is set by the producer when it sends.
- **Consumer (`KafkaCommandConsumer : BackgroundService`):**
  - **It starts from the earliest offset on every start and never commits offsets.** The store is in memory, so each Processor start replays the whole topic to rebuild history.
  - `occurredAt` is set to the Kafka message timestamp, not the wall clock. Replays therefore give identical history, and `asOf` answers stay stable across restarts.
  - Messages are handled one at a time, using a fresh DI scope per message.
  - A failure to deserialise, or a domain validation exception, is logged at Warning and the message is skipped, so a bad message can't poison-loop. An unexpected exception is logged at Error and the message is also skipped. This is a demo, so there is no retry or dead-letter queue.
  - The `CancellationToken` is passed to `Consume` and to the handler.
- **Configuration:** `Kafka:BootstrapServers` (default `localhost:9092`), `Kafka:Topic` and `Kafka:ConsumerEnabled` (set to false in Api tests).

## 6. Api (Processor) endpoints

| Method | Route | Result |
|---|---|---|
| GET | `/depth-charts/{sport}/{team}?asOf={iso8601}` | `{ "WR": [ {playerId, name}, … ], "KR": [ … ] }` |
| GET | `/depth-charts/{sport}/{team}/positions/{position}/players/{playerId}/below?asOf=` | `[ {playerId, name}, … ]` |

- Endpoints stay thin. They bind the request, call the query handler and map the result.
- An invalid identifier (empty, or a non-positive player id) or an unparseable `asOf` returns `400` with ProblemDetails.
- An unknown team or player returns `200` with an empty result.

## 7. Producer CLI

```
producer add    <sport> <team> <position> <playerId> <name> [depth]
producer remove <sport> <team> <position> <playerId>
producer chart  <sport> <team> [--as-of <iso8601>]
producer below  <sport> <team> <position> <playerId> [--as-of <iso8601>]
producer demo
```

- Arguments are validated before anything is sent. Each command gets a fresh `CommandId`.
- `demo` publishes the brief's example for `NFL/demo`:
  - Bob → WR at depth 0, Alice → WR at depth 0, Charlie → WR at depth 2, Bob → KR.
  - It then polls `chart` until WR has 3 players and KR has 1, with a 10 s timeout. It prints the chart (`WR: [2, 1, 3]`, `KR: [1]`), and prints `below Alice WR` (`[1, 3]`).
- Configuration: `Kafka:BootstrapServers` and `Api:BaseUrl` (default `http://localhost:5000`).

## 8. Running

```
docker compose up -d                       # Kafka
dotnet run --project src/DepthCharts.Api   # Processor
dotnet run --project src/DepthCharts.Producer -- demo
```

The README documents these steps and the assumptions in §3.

## 9. Testing

| Layer | Tests |
|---|---|
| Domain | Emitted events carry the chart's `Sport` and `Team`. `Team` rejects empty values and `:`; `Sport` rejects anything but letters and digits. `Apply` rejects another team's events, clamps an out-of-range depth, and updates a re-added player's name on every position. Empty positions are left out of the full chart. The brief's example end to end. Insert-and-bump. Null depth and depth past the end both append. A negative depth throws. Moving a player within a position. Removing a player who is absent emits no event. `GetPlayersUnder` for the last player, an absent player and an absent position. The same player on several positions. MLB positions such as `1B`. Value-type invariants, and a non-positive player id throws in `Player`, `RemovePlayer` and `GetPlayersUnder`. |
| Event sourcing | Replaying the emitted events rebuilds an equal state. Loading with `asOf` between events gives the earlier chart. |
| Application + EF store | A handler appends events. A duplicate `CommandId` is ignored. A stale `expectedVersion` throws `ConcurrencyException`. A no-op remove appends no events but is still recorded as processed. A stale version leaves the command unprocessed. |
| Api | `WebApplicationFactory` with `Kafka:ConsumerEnabled=false`, the store seeded through the handler: chart, below, `asOf`, and 400 cases. |

Out of scope:

- Automated Kafka tests, which would need Testcontainers and Docker in CI. Kafka is covered manually by `producer demo`.
- Auth.
- Publishing domain events outward.
- Snapshots.
- Retry and DLQ.
- Durable storage.

## 10. Implementation stages

Each stage ends with `dotnet build` and `dotnet test` passing, and is committed on its own. Every stage depends only on the stages before it.

| # | Stage | Scope | Done when |
|---|---|---|---|
| 1 | **Domain** | The value types (`Sport`, `Team`, `Position`) and `Player`, the `DepthChart` aggregate, the events, and `Apply`/replay (§3). | The domain and event-sourcing tests from §9 pass. All four brief use cases work in memory, and the brief's example gives `WR: [2,1,3]`, `KR: [1]`. |
| 2 | **Application + event store** | The `IEventStore` and `ICommandPublisher` interfaces, the message contracts, the command handler and the query handlers (§4). Then `EventStoreDbContext`, `EfEventStore` and `DependencyInjection.cs` in Infrastructure (§5.1). No Kafka yet. | The Application and EF store tests pass: append, idempotency, concurrency, no-op remove, and `asOf`. |
| 3 | **Processor** | The new `DepthCharts.Api` project with the two endpoints (§6), `KafkaCommandConsumer` (§5.2), Kafka configuration and `docker-compose.yml`. | The Api endpoint tests pass with the consumer off. Manual smoke test: `docker compose up`, start the Api, send one command JSON with `kafka-console-producer`, and `GET` shows the player. |
| 4 | **Producer** | `KafkaCommandPublisher`, renaming `DepthCharts.Console` to `DepthCharts.Producer`, the CLI verbs and `demo` (§7), and the README (§8). | Unit tests for argument parsing pass. With Kafka and the Api running, `producer demo` prints the brief's expected output. |

## 11. Risks and limits

- History lives only while the topic does. If the topic retention expires or the topic is deleted, history is lost on the next restart.
- Replaying from the beginning on every start makes startup O(total commands). That is fine at this scale. The upgrade is a durable store plus committed offsets.
- Consumption is single-threaded. Throughput is bounded by one consumer, which is acceptable here and is what the InMemory shortcut relies on.
