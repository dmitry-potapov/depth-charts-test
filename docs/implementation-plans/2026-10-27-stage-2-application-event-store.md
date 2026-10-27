# Stage 2 — Application + Event Store Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Command and query handlers over an EF Core InMemory event store, with idempotency, optimistic concurrency and `asOf` reads. No Kafka yet.

**Architecture:** Application defines `IEventStore`, `ICommandPublisher`, the JSON message contracts and the handlers. Infrastructure implements `IEventStore` with EF Core InMemory and registers it in `DependencyInjection.cs`. Handlers load a stream, `DepthChart.Replay` it, call the command, append the returned events.

**Tech Stack:** .NET 10, `Microsoft.EntityFrameworkCore.InMemory` 10.0.12, `Microsoft.Extensions.DependencyInjection.Abstractions` 10.0.x, `System.Text.Json` (BCL), xUnit 2.9.3.

**Spec:** `docs/superpowers/specs/2026-10-27-event-sourced-depth-charts-design.md` (§4, §5 Event store, §9 Application + EF store, §10 stage 2)

**Depends on:** Stage 1 (`docs/implementation-plans/2026-10-27-stage-1-domain.md`) merged — uses `DepthChart`, `DepthChart.Replay`, `DepthChart.StreamIdFor`, the value types, `IDomainEvent` and both events, `DomainValidationException`.

## Global Constraints

- Application references Domain only, plus `Microsoft.Extensions.DependencyInjection.Abstractions`. No ASP.NET Core, EF or Kafka in Application.
- Every async method takes and forwards a `CancellationToken`.
- `IEventStore` and `ICommandPublisher` signatures exactly as spec §4.
- Commands serialise as JSON with a `type` discriminator: `"add-player"` / `"remove-player"`, camelCase property names (`JsonSerializerDefaults.Web`).
- `shortcut:` comment on `EfEventStore` verbatim in spirit of spec §5: InMemory enforces no unique index or transaction; correct only with the single sequential consumer; upgrade = relational provider with unique `(StreamId, Version)` and unique `CommandId`.
- Before calling the stage done: `dotnet build` and `dotnet test` both pass.

## Review Focus

1. `asOf` exactly equal to an event's `OccurredAt` includes that event (`<=`). (Task 2 test `Load_AsOfEqualToEvent_IncludesIt`.)
2. Commands for the same team in different case (`"nfl"/"TB"` vs `"NFL"/"tb"`) hit the same stream. (Task 3 test `Handle_DifferentCasing_SameStream`.)
3. A redelivered command (same `CommandId`) after a successful append is ignored, even if it would now be valid to apply again. (Task 3 test `Handle_DuplicateCommandId_Ignored`.)
4. Hand-typed JSON where `type` is not the first property still deserialises (the stage 3 smoke test types it by hand). (Task 1 test `Deserialize_TypeNotFirst_Works`.)
5. Stored events round-trip every field, including `Player.NaturalPosition` both null and set. (Task 2 test `AppendThenLoad_RoundTripsAllFields`.)

## File Structure

```
src/DepthCharts.Application/
  Abstractions/IEventStore.cs
  Abstractions/ICommandPublisher.cs
  Abstractions/ConcurrencyException.cs
  Contracts/DepthChartCommand.cs        (base + AddPlayerCommand + RemovePlayerCommand)
  Contracts/CommandSerializer.cs
  DepthChartCommandHandler.cs
  DepthChartQueryHandler.cs             (+ GetFullDepthChartQuery, GetPlayersUnderQuery records)
  DependencyInjection.cs
src/DepthCharts.Infrastructure/
  Persistence/StoredEvent.cs
  Persistence/EventStoreDbContext.cs
  Persistence/EfEventStore.cs
  DependencyInjection.cs
tests/DepthCharts.Tests/
  TestStore.cs
  Application/CommandSerializerTests.cs
  Application/DepthChartCommandHandlerTests.cs
  Application/DepthChartQueryHandlerTests.cs
  Infrastructure/EfEventStoreTests.cs
```

---

### Task 1: Abstractions and message contracts

**Files:**
- Create: `Abstractions/IEventStore.cs`, `Abstractions/ICommandPublisher.cs`, `Abstractions/ConcurrencyException.cs`, `Contracts/DepthChartCommand.cs`, `Contracts/CommandSerializer.cs` (all under `src/DepthCharts.Application/`)
- Test: `tests/DepthCharts.Tests/Application/CommandSerializerTests.cs`

**Interfaces:**
- Produces:
  - `IEventStore` and `ICommandPublisher` exactly as spec §4 (namespace `DepthCharts.Application.Abstractions`).
  - `public sealed class ConcurrencyException(string message) : Exception(message)`
  - Contracts (namespace `DepthCharts.Application.Contracts`):
    ```csharp
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
    [JsonDerivedType(typeof(AddPlayerCommand), "add-player")]
    [JsonDerivedType(typeof(RemovePlayerCommand), "remove-player")]
    public abstract record DepthChartCommand(Guid CommandId, string Sport, string Team, string Position);
    public sealed record AddPlayerCommand(Guid CommandId, string Sport, string Team, string Position,
        int PlayerId, string PlayerName, string? PlayerPosition, int? Depth) : DepthChartCommand(CommandId, Sport, Team, Position);
    public sealed record RemovePlayerCommand(Guid CommandId, string Sport, string Team, string Position,
        int PlayerId) : DepthChartCommand(CommandId, Sport, Team, Position);
    ```
  - `public static class CommandSerializer { string Serialize(DepthChartCommand); DepthChartCommand Deserialize(string json); }` — one static `JsonSerializerOptions(JsonSerializerDefaults.Web) { AllowOutOfOrderMetadataProperties = true }`. `Deserialize` throws `JsonException` on bad input or a null result. Stage 3 consumer and stage 4 publisher both use this.

- [ ] **Step 1: Write the failing tests:**
  - `RoundTrip_AddPlayer`: serialise an `AddPlayerCommand` with all fields, deserialise → `Assert.Equal(original, result)`; JSON contains `"type":"add-player"` and `"playerName"`.
  - `RoundTrip_RemovePlayer`: same, `"type":"remove-player"`.
  - `Deserialize_TypeNotFirst_Works`: `{"commandId":"<guid>","sport":"NFL","team":"tb","position":"WR","playerId":1,"playerName":"Bob","type":"add-player"}` → `AddPlayerCommand` with `Depth == null`.
  - `[Theory] Deserialize_Invalid_ThrowsJsonException` with `"not json"`, `"{}"`, `"null"`, `{"type":"nope"}`.
- [ ] **Step 2: Run** `dotnet test --filter FullyQualifiedName~CommandSerializerTests` — Expected: build FAIL.
- [ ] **Step 3: Implement** the five files.
- [ ] **Step 4: Run** the same filter — Expected: PASS.
- [ ] **Step 5: Commit**
```bash
git add src/DepthCharts.Application tests/DepthCharts.Tests/Application/CommandSerializerTests.cs
git commit -m "feat(application): add event store abstractions and command contracts"
```

---

### Task 2: EF Core InMemory event store

**Files:**
- Modify: `src/DepthCharts.Infrastructure/DepthCharts.Infrastructure.csproj` (add `Microsoft.EntityFrameworkCore.InMemory` 10.0.12), `tests/DepthCharts.Tests/DepthCharts.Tests.csproj` (add ProjectReference to Infrastructure)
- Create: `Persistence/StoredEvent.cs`, `Persistence/EventStoreDbContext.cs`, `Persistence/EfEventStore.cs`, `DependencyInjection.cs` (under `src/DepthCharts.Infrastructure/`); `tests/DepthCharts.Tests/TestStore.cs`
- Test: `tests/DepthCharts.Tests/Infrastructure/EfEventStoreTests.cs`

**Interfaces:**
- Consumes: `IEventStore`, `ConcurrencyException` (Task 1); domain events (stage 1).
- Produces:
  - `StoredEvent` with the fields of spec §5 (`long Id` is the key, value-generated).
  - `public sealed class EventStoreDbContext(DbContextOptions<EventStoreDbContext> options) : DbContext(options)` with `DbSet<StoredEvent> Events`.
  - `public sealed class EfEventStore(EventStoreDbContext db) : IEventStore`.
  - `public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)` — registers a singleton `InMemoryDatabaseRoot`, `AddDbContext<EventStoreDbContext>((sp, o) => o.UseInMemoryDatabase("depth-charts", sp.GetRequiredService<InMemoryDatabaseRoot>()))`, and `IEventStore → EfEventStore` scoped. The per-provider root keeps each host (and each `WebApplicationFactory` in stage 3) isolated. `configuration` is unused until stage 3; keep the parameter so the Api call site never changes.
  - Test helper `static class TestStore { static EfEventStore Create(); }` — new context on `UseInMemoryDatabase(Guid.NewGuid().ToString())`.

Decisions:
- `EventType` = `event.GetType().Name`; deserialise through a private `Dictionary<string, Type>` of the two event types; unknown type throws `InvalidOperationException`. `Data` = `JsonSerializer.Serialize(event, event.GetType())` with web defaults.
- `AppendAsync`: current = `MaxAsync(e => (int?)e.Version) ?? 0` over the stream; mismatch throws `ConcurrencyException` naming stream, expected and actual. Each event gets `Version = expectedVersion + i + 1`, the same `CommandId`, and its own `OccurredAt`. One `SaveChangesAsync(ct)`.
- `LoadAsync`: filter stream (and `OccurredAt <= asOf`), `OrderBy(Version)`, `ToListAsync(ct)`, then deserialise.
- `HasProcessedAsync`: `AnyAsync(e => e.CommandId == commandId, ct)`.

- [ ] **Step 1: Write the failing tests** in `EfEventStoreTests.cs` (build events directly with the stage 1 records; times `T0`, `T0+1m`, `T0+2m`):
  - `AppendThenLoad_RoundTripsAllFields`: append an added event with `NaturalPosition = WR`, one with `null`, and a removed event → `Assert.Equal` each loaded event to the original (record equality), in version order.
  - `Load_UnknownStream_Empty`.
  - `Load_OtherStreamsExcluded`: append to `nfl:tb` and `nfl:ne`; loading `nfl:tb` returns only its events.
  - `Load_AsOfBetweenEvents_ReturnsEarlier`: three events, `asOf = T0+1m+30s` → first two.
  - `Load_AsOfEqualToEvent_IncludesIt`: `asOf = T0+1m` → first two.
  - `Append_StaleExpectedVersion_Throws`: append one event at expected 0, then append at expected 0 again → `ConcurrencyException`; stream still has 1 event.
  - `Append_SecondBatch_ContinuesVersions`: append 1 at expected 0, then 2 at expected 1 → load returns 3.
  - `HasProcessed_TrueOnlyForAppendedCommandId`.
- [ ] **Step 2: Run** `dotnet test --filter FullyQualifiedName~EfEventStoreTests` — Expected: build FAIL.
- [ ] **Step 3: Implement** the Infrastructure files and `TestStore`.
- [ ] **Step 4: Run** the same filter — Expected: PASS.
- [ ] **Step 5: Commit**
```bash
git add src/DepthCharts.Infrastructure tests/DepthCharts.Tests
git commit -m "feat(infrastructure): add EF Core InMemory event store"
```

---

### Task 3: Command and query handlers

**Files:**
- Modify: `src/DepthCharts.Application/DepthCharts.Application.csproj` (add `Microsoft.Extensions.DependencyInjection.Abstractions` 10.0.x)
- Create: `src/DepthCharts.Application/DepthChartCommandHandler.cs`, `DepthChartQueryHandler.cs`, `DependencyInjection.cs`
- Test: `tests/DepthCharts.Tests/Application/DepthChartCommandHandlerTests.cs`, `DepthChartQueryHandlerTests.cs`

**Interfaces:**
- Consumes: Task 1 contracts and `IEventStore`; `TestStore.Create()` (Task 2); stage 1 `DepthChart`.
- Produces:
  - `public sealed class DepthChartCommandHandler(IEventStore store)` → `Task HandleAsync(DepthChartCommand command, DateTimeOffset occurredAt, CancellationToken ct)`. The four steps of spec §4; value types are built before loading, so invalid input throws `DomainValidationException` without touching the store. An unknown command subtype throws `InvalidOperationException`.
  - `public sealed record GetFullDepthChartQuery(string Sport, string Team, DateTimeOffset? AsOf)`
  - `public sealed record GetPlayersUnderQuery(string Sport, string Team, string Position, int PlayerId, DateTimeOffset? AsOf)`
  - `public sealed class DepthChartQueryHandler(IEventStore store)`:
    - `Task<IReadOnlyDictionary<Position, IReadOnlyList<Player>>> GetFullChartAsync(GetFullDepthChartQuery query, CancellationToken ct)`
    - `Task<IReadOnlyList<Player>> GetPlayersUnderAsync(GetPlayersUnderQuery query, CancellationToken ct)`
    Both build value types first (invalid → `DomainValidationException`), load with `AsOf`, `Replay`, read.
  - `public static IServiceCollection AddApplication(this IServiceCollection services)` — both handlers scoped.

- [ ] **Step 1: Write the failing tests.** `DepthChartCommandHandlerTests` (store from `TestStore.Create()`):
  - `Handle_AddPlayer_AppendsEvent`: one `AddPlayerCommand` → `LoadAsync("nfl:tb", null)` has one `PlayerAddedToDepthChart` with `OccurredAt == occurredAt`.
  - `Handle_DuplicateCommandId_Ignored`: add Bob WR (id X), then remove Bob WR, then replay the add with id X → stream has 2 events and Bob is not on WR.
  - `Handle_NoOpRemove_AppendsNothing`: remove on empty chart → stream empty, `HasProcessedAsync` false.
  - `Handle_InvalidInput_ThrowsDomainValidation`: `Team = "t:b"` → `DomainValidationException`; `PlayerId = 0` → same; stream empty.
  - `Handle_DifferentCasing_SameStream`: add via `("nfl","TB")`, add via `("NFL","tb")` → `"nfl:tb"` has 2 events.
  - `Handle_StaleVersion_PropagatesConcurrencyException`: use a small fake `IEventStore` in the test file whose `LoadAsync` returns `[]` but `AppendAsync` delegates to a real store already holding one event → `ConcurrencyException`.

  `DepthChartQueryHandlerTests` (seed through the command handler):
  - `FullChart_BriefExample`: the brief's four adds → `WR` ids `[2,1,3]`, `KR` `[1]`.
  - `PlayersUnder_BriefExample`: Alice WR → `[1,3]`.
  - `FullChart_AsOf_ReturnsEarlierChart`: adds at `T0`, `T0+1m`; `AsOf = T0+30s` → only the first player.
  - `UnknownTeam_EmptyResults`: both queries return empty.
  - `InvalidQueryInput_ThrowsDomainValidation`: `Team = ""` → `DomainValidationException`.
- [ ] **Step 2: Run** `dotnet test --filter "FullyQualifiedName~HandlerTests"` — Expected: build FAIL.
- [ ] **Step 3: Implement** the handlers, query records and `AddApplication`.
- [ ] **Step 4: Run** `dotnet build && dotnet test` — Expected: all PASS.
- [ ] **Step 5: Commit**
```bash
git add src/DepthCharts.Application tests/DepthCharts.Tests/Application
git commit -m "feat(application): add depth chart command and query handlers"
```
