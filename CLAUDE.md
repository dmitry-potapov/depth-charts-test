# CLAUDE.md

.NET 10 Clean Architecture solution.

## Rules
- Keep the dependency direction: Domain ← Application ← Infrastructure ← Api. Domain and Application never reference ASP.NET Core or infrastructure packages.
- Business logic goes in Application/Domain.
- New abstractions go in Application as an interface, with the implementation in Infrastructure, registered in that layer's `DependencyInjection.cs`.
- Validate input at the boundary. Domain types enforce their own invariants by throwing in the constructor.
- Async all the way down. Pass the `CancellationToken` through.
- Every behaviour change needs a test.
- All tests should have their Arrange/Act/Assert sections marked up.
- Before saying a task is done, run `dotnet build` and `dotnet test` and confirm both pass.
- Every tech spec should be stored in the docs folder.
- Every implementation plan should be put in the docs/implementation-plans folder and committed along with related changes.
