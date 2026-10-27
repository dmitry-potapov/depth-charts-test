# Stage 4 — Producer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The `DepthCharts.Producer` CLI: publish add/remove commands to Kafka, read charts from the Api, and run the brief's example end to end with `demo`. Plus the README.

**Architecture:** `KafkaCommandPublisher` (Infrastructure) implements `ICommandPublisher`. The Producer parses args into a `CliRequest`, validates them with the domain value types, then either publishes a command or calls the Api through a small `HttpClient` wrapper. Parsing and formatting are pure and unit-tested; Kafka and HTTP are covered by the manual demo.

**Tech Stack:** .NET 10 console app, `Microsoft.Extensions.Hosting` 10.0.12 (config + DI), `Confluent.Kafka` (already in Infrastructure), `System.Net.Http.Json` (BCL).

**Spec:** `docs/superpowers/specs/2026-10-27-event-sourced-depth-charts-design.md` (§5 Kafka publisher, §7, §8, §10 stage 4)

**Depends on:** Stages 1–3 merged. Uses `ICommandPublisher`, `AddPlayerCommand`, `RemovePlayerCommand`, `CommandSerializer.Serialize`, `KafkaOptions`, `DepthChart.StreamIdFor`, the value types, and the Api routes/shape from stage 3 (`GET /depth-charts/{sport}/{teamId}`, `…/positions/{position}/players/{playerId}/below`, items `{playerId, name}`).

## Global Constraints

- CLI grammar exactly as spec §7. Arguments are validated before anything is sent. Each command gets a fresh `CommandId` (`Guid.NewGuid()`).
- Kafka message key = stream id (`DepthChart.StreamIdFor`), value = `CommandSerializer.Serialize(command)`, timestamp left to the producer.
- Config: `Kafka:BootstrapServers` (default `localhost:9092`), `Api:BaseUrl` (default `http://localhost:5000`).
- `demo`: team `NFL/demo`; Bob(1) WR 0, Alice(2) WR 0, Charlie(3) WR 2, Bob KR; poll until WR has 3 and KR has 1, 10 s timeout; then print exactly:
  ```
  WR: [2, 1, 3]
  KR: [1]
  below Alice WR: [1, 3]
  ```
- Exit code `0` on success, `1` on usage error, unreachable Api/Kafka, or demo timeout; errors go to stderr.
- Before calling the stage done: `dotnet build` and `dotnet test` both pass.

## Review Focus

1. A player name with spaces (`"Tom Brady"`, quoted by the shell) arrives as one argument and is kept whole. (Task 2 test `Parse_Add_NameWithSpaces`.)
2. `add` with an invalid value (`depth -1`, `playerId 0`, team `t:b`) fails before publishing, with a message naming the bad value. (Task 2 `[Theory] Parse_InvalidValues_UsageError`.)
3. `--as-of` with an offset is sent as an ISO-8601 instant, URL-escaped (the `+` must not become a space). (Task 3 test `ChartUri_EscapesAsOf`.)
4. Running `demo` twice in a row still prints the same output (re-adds are moves, not duplicates). Covered by the domain; checked in Task 4's manual run.
5. Producer started while Kafka is down fails with a readable error and exit `1` instead of hanging forever: set `MessageTimeoutMs = 10000` on the producer config. (Manual check in Task 4.)

## File Structure

```
src/DepthCharts.Infrastructure/
  Messaging/KafkaCommandPublisher.cs
  DependencyInjection.cs               (modify: AddCommandPublisher)
src/DepthCharts.Producer/              (git mv from src/DepthCharts.Console)
  DepthCharts.Producer.csproj
  Program.cs
  Cli/CliRequest.cs                    (request records)
  Cli/CliParser.cs                     (+ CliUsageException)
  Cli/ChartFormatter.cs
  Api/DepthChartApiClient.cs           (+ PlayerDto)
  DemoRunner.cs
README.md
tests/DepthCharts.Tests/
  Infrastructure/KafkaCommandPublisherTests.cs
  Producer/CliParserTests.cs
  Producer/ChartFormatterTests.cs
  Producer/DepthChartApiClientTests.cs
```

---

### Task 1: Kafka command publisher

**Files:**
- Create: `src/DepthCharts.Infrastructure/Messaging/KafkaCommandPublisher.cs`
- Modify: `src/DepthCharts.Infrastructure/DependencyInjection.cs`
- Test: `tests/DepthCharts.Tests/Infrastructure/KafkaCommandPublisherTests.cs`

**Interfaces:**
- Consumes: `ICommandPublisher`, `KafkaOptions`, `CommandSerializer`, `DepthChart.StreamIdFor`.
- Produces:
  - `public sealed class KafkaCommandPublisher(IOptions<KafkaOptions> options) : ICommandPublisher, IDisposable` — one `IProducer<string, string>` (`ProducerConfig { BootstrapServers, MessageTimeoutMs = 10000 }`) built in the constructor; `PublishAsync` → `ProduceAsync(Topic, new Message<string,string> { Key = KeyFor(command), Value = CommandSerializer.Serialize(command) }, ct)`; `Dispose` flushes (5 s) and disposes.
  - `public static string KeyFor(DepthChartCommand command)` → `DepthChart.StreamIdFor(new Sport(command.Sport), new TeamId(command.TeamId))`.
  - `public static IServiceCollection AddCommandPublisher(this IServiceCollection services, IConfiguration configuration)` — `Configure<KafkaOptions>(Kafka section)`, singleton `ICommandPublisher → KafkaCommandPublisher`. Does not register the store or consumer.

- [ ] **Step 1: Write the failing tests:**
  - `KeyFor_NormalisesToStreamId`: `AddPlayerCommand` with `Sport "nfl"`, `TeamId "TB"` → `"nfl:tb"`.
  - `KeyFor_InvalidTeam_ThrowsDomainValidation`: `TeamId "t:b"` → `DomainValidationException`.
- [ ] **Step 2: Run** `dotnet test --filter FullyQualifiedName~KafkaCommandPublisherTests` — Expected: build FAIL.
- [ ] **Step 3: Implement** the publisher and `AddCommandPublisher`.
- [ ] **Step 4: Run** the same filter — Expected: PASS.
- [ ] **Step 5: Commit**
```bash
git add src/DepthCharts.Infrastructure tests/DepthCharts.Tests/Infrastructure/KafkaCommandPublisherTests.cs
git commit -m "feat(infrastructure): publish depth chart commands to Kafka"
```

---

### Task 2: Rename to Producer and parse the CLI

**Files:**
- Rename: `git mv src/DepthCharts.Console src/DepthCharts.Producer`, then `git mv` the csproj to `DepthCharts.Producer.csproj`; update `DepthCharts.sln` (`dotnet sln remove` the old path, `dotnet sln add` the new one).
- Modify: `DepthCharts.Producer.csproj` (ProjectReference to Infrastructure; `Microsoft.Extensions.Hosting` 10.0.12; drop the now-transitive Domain/Application refs only if the build stays green), `tests/DepthCharts.Tests/DepthCharts.Tests.csproj` (ProjectReference to Producer)
- Create: `src/DepthCharts.Producer/Cli/CliRequest.cs`, `Cli/CliParser.cs`
- Test: `tests/DepthCharts.Tests/Producer/CliParserTests.cs`

**Interfaces:**
- Produces (namespace `DepthCharts.Producer.Cli`):
  ```csharp
  public abstract record CliRequest;
  public sealed record PublishRequest(DepthChartCommand Command) : CliRequest;
  public sealed record ChartRequest(string Sport, string TeamId, DateTimeOffset? AsOf) : CliRequest;
  public sealed record BelowRequest(string Sport, string TeamId, string Position, int PlayerId, DateTimeOffset? AsOf) : CliRequest;
  public sealed record DemoRequest : CliRequest;
  public sealed class CliUsageException(string message) : Exception(message);
  public static class CliParser
  {
      public const string Usage = "...";   // the five lines of spec §7
      public static CliRequest Parse(string[] args);
  }
  ```
  `Parse` checks verb and arg count, parses ints and `--as-of` (`DateTimeOffset.TryParse`, invariant culture, assume UTC), then validates by constructing `Sport`, `TeamId`, `Position`, `PlayerId`, `Player` (for `add`), and rejects negative depth. Any `DomainValidationException` or parse failure is rethrown as `CliUsageException` whose message names the argument. `add` builds `AddPlayerCommand` with `PlayerPosition = null`; string fields are passed as typed (the Processor normalises).

- [ ] **Step 1: Write the failing tests:**
  - `Parse_Add_WithDepth`: `["add","NFL","tb","WR","1","Bob","0"]` → `PublishRequest` with `AddPlayerCommand { PlayerId 1, PlayerName "Bob", Depth 0, Position "WR" }` and non-empty `CommandId`.
  - `Parse_Add_WithoutDepth`: `Depth == null`.
  - `Parse_Add_NameWithSpaces`: name arg `"Tom Brady"` → `PlayerName == "Tom Brady"`.
  - `Parse_Add_FreshCommandIdEachTime`: two parses of the same args → different `CommandId`.
  - `Parse_Remove`: → `RemovePlayerCommand`.
  - `Parse_Chart_WithAsOf`: `["chart","NFL","tb","--as-of","2026-10-27T10:00:00+02:00"]` → `AsOf == 2026-10-27T08:00:00Z`.
  - `Parse_Below_WithoutAsOf`: → `BelowRequest` with `AsOf == null`.
  - `Parse_Demo`: → `DemoRequest`.
  - `[Theory] Parse_InvalidValues_UsageError`: `add NFL tb WR 1 Bob -1`, `add NFL tb WR 0 Bob`, `add NFL t:b WR 1 Bob`, `add NFL tb WR x Bob`, `chart NFL tb --as-of yesterday` → `CliUsageException`.
  - `[Theory] Parse_BadShape_UsageError`: `[]`, `["nope"]`, `["add","NFL"]`, `["remove","NFL","tb","WR"]`, `["chart","NFL","tb","--as-of"]`.
- [ ] **Step 2: Run** `dotnet test --filter FullyQualifiedName~CliParserTests` — Expected: build FAIL.
- [ ] **Step 3: Do the rename and implement** the parser. Leave `Program.cs` printing `CliParser.Usage` for now.
- [ ] **Step 4: Run** `dotnet build && dotnet test` — Expected: all PASS; no remaining references to `DepthCharts.Console` (`git grep DepthCharts.Console` returns nothing outside `docs/`).
- [ ] **Step 5: Commit**
```bash
git add -A DepthCharts.sln src/DepthCharts.Console src/DepthCharts.Producer tests/DepthCharts.Tests
git commit -m "feat(producer): rename console to Producer and parse CLI verbs"
```

---

### Task 3: Api client, formatter, demo and wiring

**Files:**
- Create: `src/DepthCharts.Producer/Api/DepthChartApiClient.cs`, `Cli/ChartFormatter.cs`, `DemoRunner.cs`
- Modify: `src/DepthCharts.Producer/Program.cs`
- Test: `tests/DepthCharts.Tests/Producer/ChartFormatterTests.cs`, `Producer/DepthChartApiClientTests.cs`

**Interfaces:**
- Consumes: Task 1 `AddCommandPublisher`, `ICommandPublisher`; Task 2 parser types.
- Produces:
  - `public sealed record PlayerDto(int PlayerId, string Name)`
  - `public sealed class DepthChartApiClient(HttpClient http)`:
    - `Task<Dictionary<string, List<PlayerDto>>> GetChartAsync(string sport, string teamId, DateTimeOffset? asOf, CancellationToken ct)`
    - `Task<List<PlayerDto>> GetBelowAsync(string sport, string teamId, string position, int playerId, DateTimeOffset? asOf, CancellationToken ct)`
    - `public static string ChartUri(string sport, string teamId, DateTimeOffset? asOf)` and `BelowUri(...)` — path segments and `asOf.ToString("O")` through `Uri.EscapeDataString`. Non-success status → `EnsureSuccessStatusCode` (Program reports it).
  - `public static class ChartFormatter`:
    - `string FormatIds(IEnumerable<PlayerDto> players)` → `"[2, 1, 3]"`, `"[]"`.
    - `string FormatChart(IReadOnlyDictionary<string, List<PlayerDto>> chart)` → one `"{POS}: {ids}"` line per position, in dictionary order, joined by `Environment.NewLine`.
  - `public sealed class DemoRunner(ICommandPublisher publisher, DepthChartApiClient api, TextWriter output)` → `Task<int> RunAsync(CancellationToken ct)`: publish the four commands, poll `GetChartAsync("NFL","demo")` every 250 ms until WR count 3 and KR count 1 (10 s `CancellationTokenSource` linked to `ct`), then print `FormatChart` and `below Alice WR: {FormatIds(GetBelowAsync(... "WR", 2 ...))}`; on timeout print an error and return `1`.
  - `Program.cs`: `Host.CreateApplicationBuilder()` **without** `args` (keeps CLI tokens out of configuration), `AddCommandPublisher(builder.Configuration)`; `HttpClient { BaseAddress = new Uri(config["Api:BaseUrl"] ?? "http://localhost:5000") }`; parse, then `switch` on the request: publish → print `Published {type} {commandId}`; chart → `FormatChart`; below → `FormatIds`; demo → `DemoRunner`. Catch `CliUsageException` (message + `Usage` to stderr, exit 1), `HttpRequestException` and `ProduceException<string,string>` (message to stderr, exit 1). Ctrl+C cancels the token.

- [ ] **Step 1: Write the failing tests:**
  - `FormatChart_BriefExample`: `{WR:[2,1,3], KR:[1]}` → `"WR: [2, 1, 3]" + NewLine + "KR: [1]"`.
  - `FormatIds_Empty`: `"[]"`.
  - `ChartUri_NoAsOf`: `ChartUri("NFL","demo",null) == "depth-charts/NFL/demo"`.
  - `ChartUri_EscapesAsOf`: `asOf = 2026-10-27T10:00:00+02:00` → URI contains `asOf=2026-10-27T10%3A00%3A00.0000000%2B02%3A00`.
  - `BelowUri_Shape`: `"depth-charts/NFL/demo/positions/WR/players/2/below"`.
  - `GetChart_DeserialisesInOrder`: `HttpClient` over a stub `HttpMessageHandler` returning `{"WR":[{"playerId":2,"name":"Alice"}],"KR":[{"playerId":1,"name":"Bob"}]}` → keys `["WR","KR"]`, `WR[0] == new PlayerDto(2,"Alice")`.
- [ ] **Step 2: Run** `dotnet test --filter "FullyQualifiedName~ChartFormatterTests|FullyQualifiedName~DepthChartApiClientTests"` — Expected: build FAIL.
- [ ] **Step 3: Implement** the client, formatter, demo runner and `Program.cs`.
- [ ] **Step 4: Run** `dotnet build && dotnet test` — Expected: all PASS.
- [ ] **Step 5: Commit**
```bash
git add src/DepthCharts.Producer tests/DepthCharts.Tests/Producer
git commit -m "feat(producer): add chart, below and demo verbs"
```

---

### Task 4: README and end-to-end demo

**Files:**
- Modify: `README.md` (currently empty)

- [ ] **Step 1: Write `README.md`** with: what it is (one paragraph), prerequisites (.NET 10 SDK, Docker), the three run commands of spec §8, the CLI grammar of §7 with one example per verb, the two endpoints of §6, `dotnet build` / `dotnet test`, the assumptions of §3 (free-form sport/position, normalisation rules, `TeamId` charset, insert-and-bump and move semantics, natural position informational only), and the limits of §11.
- [ ] **Step 2: Manual end-to-end:**
```bash
docker compose up -d
dotnet run --project src/DepthCharts.Api &
dotnet run --project src/DepthCharts.Producer -- demo
```
Expected output, exactly:
```
WR: [2, 1, 3]
KR: [1]
below Alice WR: [1, 3]
```
- [ ] **Step 3:** run `demo` a second time — Expected: identical output. Stop Kafka (`docker compose stop`) and run `producer add NFL tb QB 12 "Tom Brady"` — Expected: an error on stderr within ~10 s, exit code `1`.
- [ ] **Step 4: Run** `dotnet build && dotnet test` — Expected: all PASS.
- [ ] **Step 5: Commit**
```bash
git add README.md
git commit -m "docs: add README with run instructions and assumptions"
```
