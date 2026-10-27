# Stage 3 — Processor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The `DepthCharts.Api` Processor: two read endpoints over the query handler, and a Kafka consumer that feeds commands into the command handler.

**Architecture:** Minimal API host calling `AddApplication()` + `AddInfrastructure(config)`. Endpoints are thin; `DomainValidationException` becomes a 400 ProblemDetails through one `IExceptionHandler`. Message handling lives in `CommandMessageProcessor` (testable without Kafka); `KafkaCommandConsumer` is only the consume loop around it.

**Tech Stack:** ASP.NET Core 10 minimal APIs, `Confluent.Kafka` (latest stable 2.x), `Microsoft.Extensions.Hosting.Abstractions` 10.0.12, `Microsoft.AspNetCore.Mvc.Testing` 10.0.12, `apache/kafka` Docker image (KRaft).

**Spec:** `docs/superpowers/specs/2026-10-27-event-sourced-depth-charts-design.md` (§5 Kafka, §6, §9 Api, §10 stage 3)

**Depends on:** Stages 1–2 merged. Uses `AddApplication()`, `AddInfrastructure(IServiceCollection, IConfiguration)`, `DepthChartCommandHandler.HandleAsync`, `DepthChartQueryHandler.GetFullChartAsync/GetPlayersUnderAsync`, the query records, `CommandSerializer.Deserialize`, `DomainValidationException`.

## Global Constraints

- Topic `depth-chart-commands`. Config keys `Kafka:BootstrapServers` (default `localhost:9092`), `Kafka:Topic`, `Kafka:ConsumerEnabled` (default `true`; `false` in Api tests).
- Consumer: earliest offset on every start, never commits offsets, one message at a time, a fresh DI scope per message, `occurredAt` = Kafka message timestamp (UTC), `CancellationToken` passed to `Consume` and the handler.
- Bad JSON or `DomainValidationException` → log Warning, skip. Any other exception → log Error, skip. No retry, no DLQ.
- Routes and response shapes exactly as spec §6. Unknown team/player → `200` with empty result. Invalid identifier or unparseable `asOf` → `400` ProblemDetails.
- Api listens on `http://localhost:5000` (stage 4's `Api:BaseUrl` default).
- Before calling the stage done: `dotnet build` and `dotnet test` both pass.

## Review Focus

1. A team id with a disallowed character in the URL (`/depth-charts/nfl/t:b`) gives 400, not 500. (Task 1 test `Chart_InvalidTeam_400`.)
2. `asOf` that is valid ISO-8601 with an offset (`2026-10-27T10:00:00+02:00`) compares as an instant, not wall-clock text. (Task 1 test `Chart_AsOfWithOffset_ComparesAsInstant`.)
3. One bad message in the topic doesn't stop later good messages from applying. (Task 2 test `Process_BadMessageThenGood_GoodApplied`.)
4. Restarting the Processor replays the same topic into the same history, because `occurredAt` comes from the message, not the clock. (Task 2 test `Process_UsesMessageTimestamp`.)
5. Response JSON keeps position order (`WR` before `KR`) and camelCase fields (`playerId`, `name`). (Task 1 test `Chart_BriefExample_ShapeAndOrder`.)

## File Structure

```
src/DepthCharts.Api/
  DepthCharts.Api.csproj          (Microsoft.NET.Sdk.Web; refs Application, Infrastructure)
  Program.cs                      (ends with `public partial class Program;`)
  DepthChartEndpoints.cs          (MapDepthChartEndpoints + PlayerResponse record)
  DomainValidationExceptionHandler.cs
  appsettings.json
  Properties/launchSettings.json
src/DepthCharts.Infrastructure/
  Messaging/KafkaOptions.cs
  Messaging/CommandMessageProcessor.cs
  Messaging/KafkaCommandConsumer.cs
  DependencyInjection.cs          (modify)
docker-compose.yml
tests/DepthCharts.Tests/
  Api/ApiFactory.cs
  Api/DepthChartEndpointsTests.cs
  Infrastructure/CommandMessageProcessorTests.cs
```

---

### Task 1: Api project and read endpoints

**Files:**
- Create: everything under `src/DepthCharts.Api/`; `tests/DepthCharts.Tests/Api/ApiFactory.cs`, `Api/DepthChartEndpointsTests.cs`
- Modify: `DepthCharts.sln` (`dotnet sln add src/DepthCharts.Api`), `tests/DepthCharts.Tests/DepthCharts.Tests.csproj` (ProjectReference to Api, `Microsoft.AspNetCore.Mvc.Testing` 10.0.12)

**Interfaces:**
- Consumes: stage 2 handlers and DI extensions.
- Produces:
  - `public sealed record PlayerResponse(int PlayerId, string Name)`
  - `public static IEndpointRouteBuilder MapDepthChartEndpoints(this IEndpointRouteBuilder app)`:
    - `GET /depth-charts/{sport}/{team}` → `Dictionary<string, PlayerResponse[]>` keyed by `Position.Value`, in `GetFullChart()` order.
    - `GET /depth-charts/{sport}/{team}/positions/{position}/players/{playerId:int}/below` → `PlayerResponse[]`.
    - Both take `string? asOf` from the query; null/empty → no filter; otherwise `DateTimeOffset.TryParseExact` against explicit ISO-8601 formats only (`yyyy-MM-dd`, `…THH:mmK`, `…THH:mm:ss[.fffffff]K`; offset optional, default UTC; a space is read as an unencoded `+`), failure → `TypedResults.Problem(statusCode: 400, title: "Invalid asOf")`. This is boundary validation, the only logic allowed in the endpoint besides mapping.
  - `DomainValidationExceptionHandler : IExceptionHandler` → status 400, writes ProblemDetails via `IProblemDetailsService` with `Detail = exception.Message`; returns `false` for other exceptions.
  - `Program.cs`: `AddProblemDetails()`, `AddExceptionHandler<DomainValidationExceptionHandler>()`, `AddApplication()`, `AddInfrastructure(builder.Configuration)`; `UseExceptionHandler()`; `MapDepthChartEndpoints()`.
  - `launchSettings.json`: one `http` profile, `applicationUrl` `http://localhost:5000`.
  - `ApiFactory : WebApplicationFactory<Program>` with `builder.UseSetting("Kafka:ConsumerEnabled", "false")`, and `Task SeedAsync(DepthChartCommand command, DateTimeOffset occurredAt)` that resolves `DepthChartCommandHandler` from a new scope of `Services`.

  `{playerId:int}` makes a non-numeric id 404 (route mismatch). Non-positive ids are rejected by the domain (`DomainValidationException`) and become 400.

- [ ] **Step 1: Write the failing tests** (`IClassFixture<ApiFactory>` is fine; use a distinct team id per test so they don't share streams):
  - `Chart_BriefExample_ShapeAndOrder`: seed the brief's four adds; `GET /depth-charts/nfl/t1` → 200; raw JSON property order `WR` then `KR`; `WR` = `[{"playerId":2,"name":"Alice"},{"playerId":1,"name":"Bob"},{"playerId":3,"name":"Charlie"}]`.
  - `Below_BriefExample`: `GET …/t2/positions/wr/players/2/below` → ids `[1,3]`.
  - `Chart_UnknownTeam_EmptyObject`: → 200, body `{}`.
  - `Below_UnknownPlayer_EmptyArray`: → 200, body `[]`.
  - `Chart_AsOf_ReturnsEarlierChart`: seed at `T0` and `T0+1h`; `?asOf=` `T0+30m` (URL-encoded `O` format) → one player.
  - `Chart_AsOfWithOffset_ComparesAsInstant`: seed at `2026-10-27T09:00:00Z`; `asOf=2026-10-27T10:30:00+02:00` (08:30Z) → `{}`; `asOf=2026-10-27T11:30:00+02:00` → one player.
  - `Chart_InvalidTeam_400`: `/depth-charts/nfl/t:b` → 400, `Content-Type` `application/problem+json`.
  - `Below_NonPositivePlayerId_400`: `…/players/0/below` → 400 problem+json.
  - `Chart_UnparseableAsOf_400`: `?asOf=yesterday` → 400 problem+json.
- [ ] **Step 2: Run** `dotnet test --filter FullyQualifiedName~DepthChartEndpointsTests` — Expected: build FAIL.
- [ ] **Step 3: Implement** the Api project per the Interfaces block. `appsettings.json` has a `Kafka` section with `BootstrapServers`, `Topic`, `ConsumerEnabled: true`.
- [ ] **Step 4: Run** the same filter — Expected: PASS.
- [ ] **Step 5: Commit**
```bash
git add DepthCharts.sln src/DepthCharts.Api tests/DepthCharts.Tests
git commit -m "feat(api): add depth chart read endpoints"
```

---

### Task 2: Kafka consumer

**Files:**
- Modify: `src/DepthCharts.Infrastructure/DepthCharts.Infrastructure.csproj` (add `Confluent.Kafka`, `Microsoft.Extensions.Hosting.Abstractions` 10.0.12), `src/DepthCharts.Infrastructure/DependencyInjection.cs`
- Create: `Messaging/KafkaOptions.cs`, `Messaging/CommandMessageProcessor.cs`, `Messaging/KafkaCommandConsumer.cs`, `docker-compose.yml`
- Test: `tests/DepthCharts.Tests/Infrastructure/CommandMessageProcessorTests.cs`

**Interfaces:**
- Consumes: `CommandSerializer.Deserialize`, `DepthChartCommandHandler`, `DomainValidationException`.
- Produces:
  - `public sealed class KafkaOptions { string BootstrapServers = "localhost:9092"; string Topic = "depth-chart-commands"; bool ConsumerEnabled = true; }` bound from section `Kafka`. Stage 4's publisher reuses it.
  - `public sealed class CommandMessageProcessor(IServiceScopeFactory scopes, ILogger<CommandMessageProcessor> logger)` → `Task ProcessAsync(string json, DateTimeOffset timestamp, CancellationToken ct)`: deserialise, new scope, resolve `DepthChartCommandHandler`, `HandleAsync(command, timestamp, ct)`. Catches `JsonException` and `DomainValidationException` (Warning) and other exceptions except `OperationCanceledException` when `ct` is cancelled (Error); never rethrows them.
  - `public sealed class KafkaCommandConsumer(IOptions<KafkaOptions> options, CommandMessageProcessor processor, ILogger<KafkaCommandConsumer> logger) : BackgroundService`. `ExecuteAsync` returns immediately when `ConsumerEnabled` is false (read at run time, so the test factory's setting always applies). Otherwise run the blocking loop on `Task.Run`: `ConsumerConfig { BootstrapServers, GroupId = $"depth-charts-processor-{Guid.NewGuid()}", AutoOffsetReset = Earliest, EnableAutoCommit = false, AllowAutoCreateTopics = true }`, `Subscribe(Topic)`, loop `Consume(ct)` → `processor.ProcessAsync(msg.Message.Value, msg.Message.Timestamp.UtcDateTime, ct)` until cancelled; `Close()` on exit. The random group id is what guarantees "earliest on every start".
  - `AddInfrastructure` additionally: `Configure<KafkaOptions>(configuration.GetSection("Kafka"))`, `AddSingleton<CommandMessageProcessor>()`, `AddHostedService<KafkaCommandConsumer>()`.
  - `docker-compose.yml`: one service `kafka`, `image: apache/kafka:4.1.0`, `ports: ["9092:9092"]`, **no `KAFKA_*` environment variables** (any one replaces the image's single-node KRaft defaults, which already listen on `localhost:9092` and auto-create topics).

- [ ] **Step 1: Write the failing tests** (build a `ServiceCollection` with `AddLogging()`, `AddApplication()`, `IEventStore` → a shared `TestStore.Create()` instance; logger from a tiny list-collecting `ILogger<CommandMessageProcessor>` fake in the test file):
  - `Process_ValidCommand_Applied`: JSON from `CommandSerializer.Serialize(add Bob)` → store has one event.
  - `Process_UsesMessageTimestamp`: timestamp `T0` → loaded event `OccurredAt == T0`.
  - `Process_BadJson_LogsWarningAndSkips`: `"garbage"` → no throw, one Warning, store empty.
  - `Process_InvalidDomainValue_LogsWarningAndSkips`: add with `Team = "t:b"` → no throw, one Warning.
  - `Process_UnexpectedException_LogsError`: register an `IEventStore` fake that throws `InvalidOperationException` → no throw, one Error.
  - `Process_BadMessageThenGood_GoodApplied`.
- [ ] **Step 2: Run** `dotnet test --filter FullyQualifiedName~CommandMessageProcessorTests` — Expected: build FAIL.
- [ ] **Step 3: Implement** the Messaging files, DI changes and `docker-compose.yml`.
- [ ] **Step 4: Run** `dotnet build && dotnet test` — Expected: all PASS, including the Task 1 Api tests (consumer disabled there).
- [ ] **Step 5: Commit**
```bash
git add docker-compose.yml src/DepthCharts.Infrastructure tests/DepthCharts.Tests
git commit -m "feat(infrastructure): consume depth chart commands from Kafka"
```

---

### Task 3: Manual smoke test

No code. Needs Docker. Record the outcome in the PR/hand-off note.

- [ ] **Step 1:** `docker compose up -d` — Expected: `kafka` container running.
- [ ] **Step 2:** `dotnet run --project src/DepthCharts.Api` (separate terminal) — Expected: listening on `http://localhost:5000`, no consumer errors.
- [ ] **Step 3:** send one command:
```bash
echo 'nfl:tb|{"type":"add-player","commandId":"6f1c2e1a-0000-4000-8000-000000000001","sport":"NFL","team":"tb","position":"QB","playerId":12,"playerName":"Tom Brady","playerPosition":"QB","depth":0}' \
  | docker compose exec -T kafka /opt/kafka/bin/kafka-console-producer.sh \
      --bootstrap-server localhost:9092 --topic depth-chart-commands \
      --property parse.key=true --property key.separator='|'
```
- [ ] **Step 4:** `curl -s http://localhost:5000/depth-charts/nfl/tb` — Expected: `{"QB":[{"playerId":12,"name":"Tom Brady"}]}`.
- [ ] **Step 5:** restart the Api and repeat Step 4 — Expected: same output (replayed from earliest).
