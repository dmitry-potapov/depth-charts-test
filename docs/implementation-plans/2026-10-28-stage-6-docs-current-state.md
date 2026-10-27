# Plan: move implementation details from README into docs

## Context
README.md (144 lines) mixes "how to run it" with deep implementation detail (domain rules, handler steps, consumer behaviour, event store, endpoints, tests). The user wants the current state of the code captured in `docs/`, the README slimmed to the essentials, and an explicit note that the solution is deliberately overengineered (Kafka + event sourcing to demonstrate the skills); a real-world version could be a plain CRUD app over a database.

The existing `docs/superpowers/specs/2026-10-27-event-sourced-depth-charts-design.md` is the *original* design and is already stale (e.g. it says positions are not restricted; stage 5 added a per-sport registry). Leave it untouched as history; the new doc is the source of truth for the current state.

## Changes

### 1. New `docs/architecture.md` — current state of the code
Content moved (not rewritten) from README, verbatim where possible, in this order:
1. **Why it is built like this** (new): the solution is overengineered on purpose — Kafka transport, event sourcing, two processes — to demonstrate working with Kafka and event sourcing. In real-world use this could be a regular CRUD app with a database table per chart (brief item 5 also says not to invest in persistence). Link to the original spec for the design history.
2. **Overview**: projects and dependency direction (Domain ← Application ← Infrastructure ← Api/Producer), data flow Producer → Kafka → Api consumer → event store → REST → Producer reads.
3. Sections moved from README unchanged: **Assumptions**, **Limits**, **Domain rules**, **Use cases**, **Kafka consumer**, **Event store**, **Endpoints**, **Configuration**, **Testing**.
4. **Producer CLI behaviour**: the detailed bullets from README's "Producer CLI" (validation, `--as-of`, idempotent publish, demo fresh team/timeout, error handling/exit codes, config keys).

### 2. Slim `README.md` to ~45 lines
Keep: title + one-paragraph intro, a short **"Note: deliberately overengineered"** paragraph (same message, 2–3 sentences, linking to `docs/architecture.md`), Prerequisites and running, demo expected output, Producer CLI usage block + examples, Commands (`dotnet build/test/run`), and a **Documentation** section linking `docs/architecture.md`, the original spec and `docs/implementation-plans/`. Fix the in-README anchor `[Endpoints](#endpoints)` → link into the new doc.

### 3. Implementation plan file
Per CLAUDE.md, save this plan as `docs/implementation-plans/2026-10-28-stage-6-docs-current-state.md` and commit it with the doc changes (single commit, `docs:` prefix, on a branch per git rules — main is the current branch, so branch `docs/current-state` first).

## Skipped
- No code changes, no tests (docs only; "every behaviour change needs a test" does not apply).
- Not updating the stale original spec beyond linking to it.
- No Claude Docs/artifact — CLAUDE.md wants specs in the repo `docs/` folder.

## Verification
- `dotnet build` and `dotnet test` pass (CLAUDE.md requires it before "done"; nothing should change).
- Grep that every README section heading removed now exists in `docs/architecture.md` (nothing lost), and relative links resolve (`ls` the targets).
