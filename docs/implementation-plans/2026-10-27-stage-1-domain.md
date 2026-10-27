# Stage 1 — Domain Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The pure depth chart domain: value types, the event-sourced `DepthChart` aggregate, its events and replay, with all four brief use cases working in memory.

**Architecture:** Commands on `DepthChart` validate and *return* events without mutating state; `Apply` is the only mutator, and replay is `Apply` over a stream. No packages, no I/O.

**Tech Stack:** .NET 10 (`net10.0`), C#, xUnit 2.9.3 with `Assert` (already in the test project).

**Spec:** `docs/superpowers/specs/2026-10-27-event-sourced-depth-charts-design.md` (§3, §9 Domain + Event sourcing, §10 stage 1)

## Global Constraints

- Domain has **no package references** and no project references.
- Value types throw `DomainValidationException` in their constructor on invalid input.
- `Sport`, `Position`: non-empty, trimmed, upper-cased. `TeamId`: non-empty, trimmed, lower-cased, only `[a-z0-9-]`. `PlayerId`: positive int. `Player.Name`: non-empty.
- Stream id: `"{sport}:{teamId}"` in lower case, e.g. `nfl:tb`.
- Events record the **resolved** depth and carry `Sport` and `TeamId`; `Apply` ignores those two fields.
- Positions are free-form; nothing is checked against the sport.
- Before calling the stage done: `dotnet build` and `dotnet test` both pass.

## Review Focus

1. Moving a player who is already on the chart to a depth past the end: depth resolves against the list *without* them, so `[A,B,C]` + add A at 3 → `[B,C,A]`, event depth `2`. (Task 2 test `AddPlayer_MovePastEnd_ResolvesAgainstListWithoutPlayer`.)
2. Removing the last player of a position: the position disappears from `GetFullChart()` instead of printing `QB: []`. (Task 2 test `RemovePlayer_LastOnPosition_PositionNotInFullChart`.)
3. Position order in the full chart: positions appear in the order first added (`WR` then `KR` in the brief), and stay stable after removals. (Task 2, brief example test asserts key order.)
4. Same identifier in different case or with spaces (`" wr "`, `"Tb"`) is the same position/team. (Task 1 tests.)
5. A player re-added with a different name replaces the stored name (latest event wins). (Task 2 test `AddPlayer_ExistingPlayerNewName_UpdatesName`.)

## File Structure

```
src/DepthCharts.Domain/
  DomainValidationException.cs
  Sport.cs  TeamId.cs  Position.cs  PlayerId.cs  Player.cs
  DepthChart.cs
  Events/IDomainEvent.cs
  Events/PlayerAddedToDepthChart.cs
  Events/PlayerRemovedFromDepthChart.cs
tests/DepthCharts.Tests/Domain/
  ValueTypeTests.cs
  DepthChartTests.cs
  ReplayTests.cs
```

Namespaces: `DepthCharts.Domain` and `DepthCharts.Domain.Events`.

---

### Task 1: Value types

**Files:**
- Create: `src/DepthCharts.Domain/DomainValidationException.cs`, `Sport.cs`, `TeamId.cs`, `Position.cs`, `PlayerId.cs`, `Player.cs`
- Test: `tests/DepthCharts.Tests/Domain/ValueTypeTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class DomainValidationException(string message) : Exception(message)`
  - `public sealed record Sport(string value)` → `string Value`, `ToString() => Value`. Same shape for `Position(string)`, `TeamId(string)`, `PlayerId(int)` (`int Value`).
  - `public sealed record Player(PlayerId id, string name, Position? naturalPosition)` → `Id`, `Name` (trimmed), `NaturalPosition`.

All value types are `sealed record` classes (value equality, usable as dictionary keys, no default-struct bypass of the constructor). Constructor parameter names must match property names case-insensitively, so System.Text.Json can deserialise them in stage 2. Pattern:

```csharp
public sealed record Sport
{
    public Sport(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DomainValidationException("Sport must not be empty.");
        Value = value.Trim().ToUpperInvariant();
    }
    public string Value { get; }
    public override string ToString() => Value;
}
```

- [ ] **Step 1: Write the failing tests** in `ValueTypeTests.cs`:
  - `Sport_NormalisesToUpper`: `new Sport(" nfl ").Value == "NFL"`; `new Sport("nfl") == new Sport("NFL")`.
  - `Position_NormalisesToUpper`: `new Position(" 1b ").Value == "1B"`.
  - `TeamId_NormalisesToLower`: `new TeamId(" Tb ").Value == "tb"`; `new TeamId("new-york-1").Value == "new-york-1"`.
  - `[Theory] TeamId_RejectsInvalid` with `""`, `"  "`, `"t:b"`, `"t b"`, `"tb!"` → `Assert.Throws<DomainValidationException>`.
  - `[Theory] Sport_And_Position_RejectEmpty` with `""`, `"   "` → both throw.
  - `[Theory] PlayerId_RejectsNonPositive` with `0`, `-1` → throws; `new PlayerId(1).Value == 1`.
  - `Player_RejectsEmptyName`: `new Player(new PlayerId(1), " ", null)` throws; `new Player(new PlayerId(1), " Bob ", null).Name == "Bob"`.
- [ ] **Step 2: Run** `dotnet test --filter FullyQualifiedName~ValueTypeTests` — Expected: build FAIL (types missing).
- [ ] **Step 3: Implement** the six files per the Interfaces block. `TeamId` validates the normalised value against `^[a-z0-9-]+$`.
- [ ] **Step 4: Run** `dotnet test --filter FullyQualifiedName~ValueTypeTests` — Expected: PASS.
- [ ] **Step 5: Commit**
```bash
git add src/DepthCharts.Domain tests/DepthCharts.Tests/Domain/ValueTypeTests.cs
git commit -m "feat(domain): add depth chart value types"
```

---

### Task 2: `DepthChart` aggregate, events and queries

**Files:**
- Create: `src/DepthCharts.Domain/DepthChart.cs`, `Events/IDomainEvent.cs`, `Events/PlayerAddedToDepthChart.cs`, `Events/PlayerRemovedFromDepthChart.cs`
- Test: `tests/DepthCharts.Tests/Domain/DepthChartTests.cs`

**Interfaces:**
- Consumes: Task 1 types.
- Produces:
  - `public interface IDomainEvent { Sport Sport { get; } TeamId TeamId { get; } DateTimeOffset OccurredAt { get; } }`
  - `public sealed record PlayerAddedToDepthChart(Sport Sport, TeamId TeamId, Player Player, Position Position, int Depth, DateTimeOffset OccurredAt) : IDomainEvent`
  - `public sealed record PlayerRemovedFromDepthChart(Sport Sport, TeamId TeamId, PlayerId PlayerId, Position Position, DateTimeOffset OccurredAt) : IDomainEvent`
  - `public sealed class DepthChart`
    - `DepthChart(Sport sport, TeamId teamId)`
    - `Sport Sport`, `TeamId TeamId`, `string StreamId`, `int Version` (events applied)
    - `static string StreamIdFor(Sport sport, TeamId teamId)` → `"nfl:tb"`
    - `IReadOnlyList<IDomainEvent> AddPlayer(Player player, Position position, int? depth, DateTimeOffset occurredAt)` — always one event
    - `IReadOnlyList<IDomainEvent> RemovePlayer(PlayerId playerId, Position position, DateTimeOffset occurredAt)` — zero or one event
    - `void Apply(IDomainEvent @event)` — unknown event type throws `InvalidOperationException`
    - `IReadOnlyDictionary<Position, IReadOnlyList<Player>> GetFullChart()`
    - `IReadOnlyList<Player> GetPlayersUnder(PlayerId playerId, Position position)`

State is `Dictionary<Position, List<Player>>`. Players are matched by `Id` only. Decisions the spec leaves open:
- Depth resolution in `AddPlayer`: take the position's list, remove this player's id if present, then `resolved = depth is null || depth > count ? count : depth`. `depth < 0` throws `DomainValidationException`.
- `Apply(PlayerAdded…)`: remove any entry with the same id from that position, insert at `Depth`. `Apply(PlayerRemoved…)`: remove by id. Never remove the dictionary key (keeps first-added order); `GetFullChart()` skips empty lists.

Test helper in the test class: `static void Do(DepthChart c, IEnumerable<IDomainEvent> es) { foreach (var e in es) c.Apply(e); }` and `static int[] Ids(DepthChart c, string pos)` reading `GetFullChart()` (empty array if absent). Use `NFL`/`tb` and a fixed `DateTimeOffset T0`.

- [ ] **Step 1: Write the failing tests** in `DepthChartTests.cs`:
  - `BriefExample_ProducesExpectedChart`: Bob(1) WR 0, Alice(2) WR 0, Charlie(3) WR 2, Bob KR null → `Ids(WR) == [2,1,3]`, `Ids(KR) == [1]`, full-chart keys in order `["WR","KR"]`, `GetPlayersUnder(2, WR)` ids `== [1,3]`.
  - `AddPlayer_OccupiedDepth_BumpsDown`: [1,2,3], add 4 at 1 → `[1,4,2,3]`.
  - `[Theory] AddPlayer_NullOrPastEnd_Appends` (null, 5) on [1,2] → `[1,2,3]`, emitted event `Depth == 2`.
  - `AddPlayer_NegativeDepth_Throws` → `DomainValidationException`.
  - `AddPlayer_ExistingPlayer_Moves`: [1,2,3], add 3 at 0 → `[3,1,2]`.
  - `AddPlayer_MovePastEnd_ResolvesAgainstListWithoutPlayer`: [1,2,3], add 1 at 3 → `[2,3,1]`, event `Depth == 2`.
  - `AddPlayer_ExistingPlayerNewName_UpdatesName`: add (1,"Bob") then (1,"Robert") at same position → single entry named `"Robert"`.
  - `AddPlayer_EmitsEventWithChartIdentity`: event is `PlayerAddedToDepthChart` with `Sport == NFL`, `TeamId == tb`, `OccurredAt == T0`.
  - `AddPlayer_DoesNotMutateUntilApplied`: after `AddPlayer` without `Apply`, `GetFullChart()` is empty and `Version == 0`; after `Do`, `Version == 1`.
  - `RemovePlayer_Present_EmitsEventAndRemoves`: [1,2] remove 1 → one `PlayerRemovedFromDepthChart` with `Sport`/`TeamId` set; after apply `[2]`.
  - `RemovePlayer_Absent_EmitsNothing`: on empty chart and on a position without the player → `Assert.Empty(events)`.
  - `RemovePlayer_LastOnPosition_PositionNotInFullChart`: WR [1], KR [1]; remove 1 from WR → keys `["KR"]`.
  - `GetPlayersUnder_LastPlayer_Empty`, `GetPlayersUnder_AbsentPlayer_Empty`, `GetPlayersUnder_AbsentPosition_Empty`.
  - `SamePlayer_OnSeveralPositions_Independent`: player 1 on WR and KR; remove from WR leaves KR `[1]`.
  - `MlbPositions_Work`: sport `MLB`, positions `1B`, `SP` → charts `[1]` and `[2]`.
  - `StreamIdFor_IsLowerCase`: `StreamIdFor(new Sport("NFL"), new TeamId("TB")) == "nfl:tb"`.
- [ ] **Step 2: Run** `dotnet test --filter FullyQualifiedName~DepthChartTests` — Expected: build FAIL.
- [ ] **Step 3: Implement** the events and `DepthChart` per the Interfaces block and the decisions above.
- [ ] **Step 4: Run** `dotnet test --filter FullyQualifiedName~DepthChartTests` — Expected: PASS.
- [ ] **Step 5: Commit**
```bash
git add src/DepthCharts.Domain tests/DepthCharts.Tests/Domain/DepthChartTests.cs
git commit -m "feat(domain): add event-sourced DepthChart aggregate"
```

---

### Task 3: Replay

**Files:**
- Modify: `src/DepthCharts.Domain/DepthChart.cs`
- Test: `tests/DepthCharts.Tests/Domain/ReplayTests.cs`

**Interfaces:**
- Consumes: Task 2.
- Produces: `public static DepthChart Replay(Sport sport, TeamId teamId, IEnumerable<IDomainEvent> events)` — new chart, `Apply` each in order. Stage 2 handlers call this.

- [ ] **Step 1: Write the failing tests** in `ReplayTests.cs`:
  - `Replay_EmittedEvents_RebuildsEqualState`: on chart A run the brief example (4 events), one move (1), one remove (1) and one no-op remove (0), collecting every emitted event; `Replay(NFL, tb, collected)` gives the same position keys in the same order, the same player ids per position, and `Version == 6` on both.
  - `Replay_FilteredByOccurredAt_GivesEarlierChart`: events at `T0`, `T0+1m`, `T0+2m`; replay of `events.Where(e => e.OccurredAt <= T0+1m)` matches the chart after two commands.
  - `Replay_NoEvents_IsEmptyChart`: `GetFullChart()` empty, `Version == 0`.
- [ ] **Step 2: Run** `dotnet test --filter FullyQualifiedName~ReplayTests` — Expected: build FAIL (`Replay` missing).
- [ ] **Step 3: Implement** `DepthChart.Replay`.
- [ ] **Step 4: Run** `dotnet build && dotnet test` — Expected: build succeeds, all tests PASS.
- [ ] **Step 5: Commit**
```bash
git add src/DepthCharts.Domain/DepthChart.cs tests/DepthCharts.Tests/Domain/ReplayTests.cs
git commit -m "feat(domain): rebuild DepthChart by replaying events"
```
