# Depth Charts

Event-sourced sports depth charts on .NET 10 (Clean Architecture). Only the Domain layer is implemented so far; there are no use cases, endpoints or configuration yet.

## Domain rules

Value types normalise their input and throw `DomainValidationException` when it is invalid:

| Type | Rule |
|------|------|
| `Sport` | Trimmed, upper-cased, letters and digits only (`NFL`). |
| `TeamId` | Trimmed, lower-cased, letters, digits and `-` only (`tampa-bay`). |
| `Position` | Non-empty, trimmed, upper-cased (`qb` → `QB`). |
| `PlayerId` | Positive integer. |
| `Player` | Has an id, a non-empty trimmed name and an optional natural position. |

`DepthChart` is the aggregate for one sport and team. Its stream id is `"{sport}:{team}"` in lower case, e.g. `nfl:tampa-bay`.

- Commands do not change state. `AddPlayer` and `RemovePlayer` return the events to record; `Apply` changes the state, and `Replay` rebuilds a chart from its events. `Version` counts the events applied.
- `AddPlayer` with no depth, or a depth past the end, puts the player last. A negative depth is rejected. Adding a player who is already at that position moves them.
- When a player is added, their name is updated at every position they hold (the latest event wins).
- `RemovePlayer` for a player who is not at the position returns no events.
- `Apply` rejects events for another sport or team and event types it does not know. A depth out of range in a stored event is clamped.
- `GetFullChart` lists the positions in the order they were first added and leaves out empty ones.
- `GetPlayersUnder` returns the players below the given player at a position, or an empty list if the player is not there.

## Commands

```sh
dotnet build
dotnet test
```

## Testing

xUnit tests are in `tests/DepthCharts.Tests`. `Domain/` covers the value types, the `DepthChart` commands and queries, and replay.
