# Stage 5: Sport position registry

## Context
The brief asks for depth charts "for NFL supporting positions (QB, WR, RB, TE, K, P, KR, PR)" and "MLB supporting positions (SP, RP, C, 1B, 2B, 3B, SS, LF, RF, CF, DH)". Today sport and position are free text, so `NFL` + `SS` is accepted and the lists from the brief appear nowhere. We add a per-sport registry of allowed positions, stored as a JSON list on a `SportDefinition` entity and seeded with the brief's values.

Decisions (from the user):
- EF InMemory has no migrations → seed with `HasData` and call `EnsureCreatedAsync` at Api startup.
- `AllowedPositions` null or empty → any position. A sport with **no row** also accepts any position, with a code comment that managing sports (adding or rejecting unknown ones) would be a separate feature.

## Changes

### Application
- `src/DepthCharts.Application/Abstractions/ISportRegistry.cs` (new):
  `Task<IReadOnlySet<Position>?> GetAllowedPositionsAsync(Sport sport, CancellationToken ct)`. Returns `null` when any position is allowed.
- `DepthChartCommandHandler` (`src/DepthCharts.Application/DepthChartCommandHandler.cs`): the constructor also takes `ISportRegistry`. For `AddPlayerCommand` only, after building the value types and before `LoadAsync`/`AppendAsync`: if the allowed set is non-null and has no `position`, throw `DomainValidationException($"Position {position} is not allowed for {sport}.")`. The existing flow then records nothing, the consumer logs a warning and skips the command, and the command is not marked as processed. Remove and queries are not checked: removing from a position nobody can be on is already a no-op.
  The player's natural position is not checked, because the brief says players can be on charts for positions that are not their own.

### Infrastructure
- `Persistence/SportDefinition.cs` (new): `[Key] string Sport`, `List<string>? AllowedPositions` (a primitive collection, which EF stores as a JSON column on relational providers). Same style as `ProcessedCommand.cs`.
- `EventStoreDbContext`: add `DbSet<SportDefinition> Sports`, and in `OnModelCreating` use `HasData` with the NFL and MLB rows from the brief (upper-case values).
  If `HasData` rejects the primitive collection on InMemory, fall back to a `string?` property plus a JSON `HasConversion`.
- `Persistence/EfSportRegistry.cs` (new): `AsNoTracking().FirstOrDefaultAsync(s => s.Sport == sport.Value, ct)`. It returns null for a missing row, a null list or an empty list; otherwise it returns a `HashSet<Position>` built with `new Position(v)`, reusing the domain normalisation. This is where the comment about unknown sports goes.
- `DependencyInjection.cs`: `services.AddScoped<ISportRegistry, EfSportRegistry>()`. Add `public static async Task EnsureDatabaseCreatedAsync(this IServiceProvider services, CancellationToken ct = default)`, which creates a scope and calls `EventStoreDbContext.Database.EnsureCreatedAsync(ct)` (InMemory only applies `HasData` here).

### Api
- `Program.cs`: `await app.Services.EnsureDatabaseCreatedAsync();` before `app.Run()` (the consumer starts at `Run`, so the seed is there first).

### Tests (`tests/DepthCharts.Tests`)
- `TestStore.cs`: add `Sports()`, which returns an `EfSportRegistry` over a fresh InMemory context after `EnsureCreated()` (seeded). Update every `new DepthChartCommandHandler(store)` (in `DepthChartCommandHandlerTests`, `DepthChartQueryHandlerTests`) to pass it. Fix any existing test that uses an NFL/MLB position not in the seed.
- `Application/DepthChartCommandHandlerTests`:
  - NFL + `SS` throws `DomainValidationException`, stores no events and leaves `HasProcessedAsync` false.
  - NFL + `qb` is accepted (normalised).
  - `NHL` (no row) accepts any position.
- `Infrastructure/EfSportRegistryTests` (new): the seeded NFL set equals the brief's 8; MLB contains `1B` and has 11; a missing row, a null list and an empty list (rows added in the test) return null.
- `Api/DepthChartEndpointsTests`: `factory.SeedAsync` with NFL + `SS` throws, which proves `Program` seeds the store.

### Docs
- README: Assumptions (allowed positions per sport, with an unknown sport or an empty list meaning any), Domain rules / Use cases (the handler check), Event store (`Sports` table, `HasData` + `EnsureCreated`, no migrations on InMemory), Testing.
- Tech spec: add a "Sport position registry" section to `docs/superpowers/specs/2026-10-27-event-sourced-depth-charts-design.md`.
- Copy this plan to `docs/implementation-plans/2026-10-27-stage-5-sport-position-registry.md` and commit it with the changes.

## Verification
- `dotnet build` (0 warnings/errors) and `dotnet test` all green.
- Manual check (optional, needs Docker): `docker compose up -d`, run the Api, then `producer add NFL tb SS 1 Bob`. The Api log shows a warning and the command is skipped; `producer chart NFL tb` shows no SS. `producer demo` still prints the brief's output.

Skipped: sport CRUD, natural-position validation, Producer-side position checks (the Producer has no store).
