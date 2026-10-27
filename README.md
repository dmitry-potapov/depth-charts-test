# Depth Charts

Event-sourced sports depth charts on .NET 10 (Clean Architecture). The Domain and Application layers, an in-memory event store, a Kafka command consumer and read-only HTTP endpoints are implemented.

## Domain rules

Value types normalise their input and throw `DomainValidationException` when it is invalid:

| Type | Rule |
|------|------|
| `Sport` | Trimmed, upper-cased, letters and digits only (`NFL`). |
| `Team` | Trimmed, lower-cased, letters, digits and `-` only (`tampa-bay`). |
| `Position` | Non-empty, trimmed, upper-cased (`qb` → `QB`). |
| `Player` | Has a positive integer id, a non-empty trimmed name and an optional natural position. |

`DepthChart` is the aggregate for one sport and team. Its stream id is `"{sport}:{team}"` in lower case, e.g. `nfl:tampa-bay`.

- Commands do not change state. `AddPlayer` and `RemovePlayer` return the events to record; `Apply` changes the state, and `Replay` rebuilds a chart from its events. `Version` counts the events applied.
- `AddPlayer` with no depth, or a depth past the end, puts the player last. A negative depth is rejected. Adding a player who is already at that position moves them.
- When a player is added, their name is updated at every position they hold (the latest event wins).
- `RemovePlayer` for a player who is not at the position returns no events.
- `Apply` rejects events for another sport or team and event types it does not know. A depth out of range in a stored event is clamped.
- `GetFullChart` lists the positions in the order they were first added and leaves out empty ones.
- `GetPlayersUnder` returns the players below the given player at a position, or an empty list if the player is not there.

## Use cases

Commands are `AddPlayerCommand` and `RemovePlayerCommand`. `CommandSerializer` reads and writes them as JSON with camelCase names and a `type` discriminator (`add-player` / `remove-player`) that may appear anywhere in the object. A missing or unknown `type` throws `JsonException`.

`DepthChartCommandHandler.HandleAsync(command, occurredAt, ct)`:

1. Rejects an empty `CommandId`, and returns without doing anything if the command was already processed.
2. Builds the value types; invalid input throws `DomainValidationException` and records nothing.
3. Replays the stream, runs the command, and always calls `AppendAsync` with the replayed version, even with no events, so a no-op is still recorded as processed and a redelivery is ignored.
4. If the stream moved on, `AppendAsync` throws `ConcurrencyException` and records nothing, so the command stays retryable.

`DepthChartQueryHandler` has `GetFullChartAsync` and `GetPlayersUnderAsync`. Each takes an optional `AsOf`, replays the stream up to that time and reads the result. An unknown team gives empty results.

`AddApplication()` registers the handlers. `AddInfrastructure(configuration)` registers the store, binds `KafkaOptions` to the `Kafka` section and adds `KafkaCommandConsumer` as a hosted service.

## Kafka consumer

`KafkaCommandConsumer` reads command JSON from the `Kafka:Topic` topic and passes each message to `CommandMessageProcessor`, which deserializes it and calls `DepthChartCommandHandler` in a new scope.

- Every start uses a new consumer group, reads from the earliest offset and never commits, so the whole topic is replayed into the in-memory store.
- The Kafka message timestamp becomes the event's `occurredAt`, so a replay gives the same `asOf` results. A message with no timestamp is skipped with a warning; the consumer never uses the current time instead.
- There is no retry and no dead-letter topic. Tombstones, bad JSON and `DomainValidationException` are logged as warnings and skipped. Other errors, including `ConcurrencyException`, are logged as errors and skipped. Later messages are still applied.
- A non-fatal `ConsumeException` is logged and consuming goes on. A fatal one is rethrown and stops the host. Cancellation stops the loop without logging an error.
- With `Kafka:ConsumerEnabled` set to `false` the consumer does not run.

## Event store

`EfEventStore` implements `IEventStore` on EF Core InMemory. Events are stored as JSON rows with a per-stream `Version`; a separate `ProcessedCommands` table holds one row per handled command, including no-ops. `LoadAsync` with `asOf` returns the version-ordered events up to the first one later than `asOf`. Each service provider gets its own in-memory database.

The InMemory provider enforces no unique index or transaction, so this is only correct with a single sequential consumer. A real deployment needs a relational provider with unique `(StreamId, Version)` and unique `ProcessedCommand.CommandId`.

## Endpoints

`DepthCharts.Api` is a minimal API. Both endpoints are read-only and take an optional `asOf` query parameter.

| Method | Route | Returns |
|--------|-------|---------|
| `GET` | `/depth-charts/{sport}/{team}` | An object keyed by position, in `GetFullChart` order, each an array of `{ playerId, name }`. Unknown team → `{}`. |
| `GET` | `/depth-charts/{sport}/{team}/positions/{position}/players/{playerId}/below` | An array of `{ playerId, name }` below the player. Player not there → `[]`. |

`asOf` accepts ISO-8601 only: `yyyy-MM-dd`, `yyyy-MM-ddTHH:mm[K]` or `yyyy-MM-ddTHH:mm:ss[.fffffff][K]`. Without an offset it is UTC (a date alone is midnight UTC). A space is read as an unencoded `+`. Anything else, e.g. `10/11/2026`, returns a 400 ProblemDetails titled `Invalid asOf`.

An invalid sport, team, position or player id throws `DomainValidationException`, which `DomainValidationExceptionHandler` turns into a 400 ProblemDetails with the message as `detail`.

## Configuration

`appsettings.json` has a `Kafka` section: `BootstrapServers` (`localhost:9092`), `Topic` (`depth-chart-commands`) and `ConsumerEnabled` (`true`). The Api starts `KafkaCommandConsumer` by default, so run `docker compose up -d` first. Without a broker the Api still starts but serves empty charts; set `Kafka:ConsumerEnabled` to `false` to run it without Kafka. The `http` launch profile listens on `http://localhost:5000` in `Development`.

## Commands

```sh
dotnet build
dotnet test
dotnet run --project src/DepthCharts.Api
```

## Testing

xUnit tests are in `tests/DepthCharts.Tests`. `Domain/` covers the value types, the `DepthChart` commands and queries, and replay. `Application/` covers the command serializer and the command and query handlers (idempotency, no-op and stale-version cases, `asOf`). `Infrastructure/` covers `EfEventStore`, the DI registration, `CommandMessageProcessor` (applied, skipped and logged messages, cancellation) and `KafkaCommandConsumer`'s loop with a fake `consume` function (timestamps, consume errors, the disabled switch). No test needs a running broker. `Api/` calls the endpoints through `WebApplicationFactory`. `ApiFactory` turns the consumer off, and `ApiFactory.SeedAsync` records commands before each call. These tests cover the response shape, empty results, `asOf` formats and 400s. `TestStore.Create()` gives a fresh in-memory store.
