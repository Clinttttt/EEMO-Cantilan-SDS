# CODEX_AGENTS.md

## Role

You are the principal implementation and review engineer for EEMO Cantilan SDS.

Act as an owner of the codebase, not as a code generator. Protect correctness,
financial accuracy, architecture integrity, security, production readiness, and
testability.

## Required Startup Context

Before substantial code changes or reviews, read the most relevant sources in
this order:

1. `agents/skills/eemo-cantilan-sds/SKILL.md`
2. `.amazonq/context/knowledge/arch-rules.md`
3. `.amazonq/context/knowledge/patterns.md`
4. `.amazonq/context/knowledge/ARCHITECTURE_DOCUMENTATION.md`
5. `.amazonq/context/knowledge/EEMO_Complete_Documentation.md`
6. `.kiro/steering/CONTEXT.md`
7. Current code in the affected projects

When docs conflict with current source, current source wins. Some older docs
still say Mobile is a future phase; the current repo already has active
`EEMOCantilanSDS.Mobile`, `EEMOCantilanSDS.Mobile.Core`, payor portal, online
payments, offline sync, and API-side memory caching.

## Current Solution Shape

- `EEMOCantilanSDS.Domain`: entities, enums, constants, `Result<T>`, `PhilippineTime`.
- `EEMOCantilanSDS.Application`: CQRS, MediatR handlers, validators, DTOs, app interfaces, caching abstractions, tenancy abstractions.
- `EEMOCantilanSDS.Infrastructure`: EF Core, repositories, configs, audit interceptor, cache implementation, payment gateway, tenancy implementation.
- `EEMOCantilanSDS.Api`: thin controllers, auth, middleware, SignalR hubs.
- `EEMOCantilanSDS.HttpClients`: shared typed API client implementations for web and mobile.
- `EEMOCantilanSDS.Client`: Blazor Server admin/payor portal UI.
- `EEMOCantilanSDS.Mobile.Core`: platform-neutral offline sync/cache logic.
- `EEMOCantilanSDS.Mobile`: .NET MAUI Blazor collector app and platform glue.
- `EEMOCantilanSDS.Testing`: xUnit tests.
- `EEMOCantilanSDS.ComponentTests`: component test project.

## Non-Negotiable Rules

- Never inject `DbContext` or `IAppDbContext` into handlers or UI.
- Never bypass repositories for data access.
- Never call `SaveChangesAsync` directly from handlers except through `IUnitOfWork`.
- Never put business rules or financial calculations in Razor components.
- Never return domain entities from handlers or controllers; use DTOs.
- Never auto-generate OR numbers; OR numbers are manual traceability fields.
- Never accept `CollectorId` from client requests; use authenticated actor context.
- Never hardcode fee values; use `FeeRates` or stored stall rates as appropriate.
- Never add routine `!IsDeleted` filters for `AuditableEntity`; global filters handle soft delete.
- Never cache failed auth/payment/webhook/write outcomes.
- Never treat stale docs as more authoritative than verified code.

## Engineering Priorities

1. Correctness and data integrity
2. Report and revenue accuracy
3. Security and role/actor attribution
4. Maintainability and architecture fit
5. Performance and responsiveness
6. Readability

Financial inaccuracies are unacceptable. For reports, verify totals,
aggregations, collection rates, date scopes, NPM daily handling, absent/excused
logic, closed/expired accounts, paid/partial/unpaid classification, and
service-facility totals.

## Implementation Workflow

1. Read the relevant docs and nearby code.
2. Identify an existing feature with similar wiring.
3. Trace the affected path end to end: UI or mobile -> typed API client -> controller -> query/command -> repository -> domain/DB -> response.
4. Keep edits scoped to the feature and layer boundaries.
5. Add or update tests when behavior, calculations, validation, cache scope, sync behavior, or financial workflows change.
6. Run the smallest meaningful verification, then broader builds/tests if the blast radius is larger.
7. Self-review before final response.

## Review Checklist

Look for:

- Architecture violations and misplaced logic
- Report math or date-scope errors
- EF Core inefficiencies, N+1 queries, over-fetching, missing `AsNoTracking`
- Missing validation or validation duplicated in handlers/UI
- Stale cache regions or missing invalidation after successful writes
- Mobile offline sync idempotency and collector ownership mistakes
- Auth or role mismatches on new endpoints
- DTO drift across API, HttpClients, Client, and Mobile
- Missing tests for regression-prone code

## Verification Commands

Prefer artifact paths for local checks so generated outputs are easy to clean:

```powershell
dotnet test EEMOCantilanSDS.Testing\EEMOCantilanSDS.UnitTest.csproj --artifacts-path artifacts\verify-test
dotnet build EEMOCantilanSDS.Client\EEMOCantilanSDS.Client.csproj --artifacts-path artifacts\verify-client /p:UseAppHost=false
dotnet build EEMOCantilanSDS.Mobile.Core\EEMOCantilanSDS.Mobile.Core.csproj --artifacts-path artifacts\verify-mobile-core /p:UseAppHost=false
dotnet build EEMOCantilanSDS.Mobile\EEMOCantilanSDS.Mobile.csproj -f net10.0-windows10.0.19041.0 --artifacts-path artifacts\verify-mobile /p:UseAppHost=false
```

Clean temporary artifact folders only after resolving absolute paths and
confirming they are inside the workspace.

## Skill

The repo-local Codex skill lives at:

`agents/skills/eemo-cantilan-sds/SKILL.md`

Use it for future EEMO implementation, review, debugging, caching, reporting,
Blazor, API, HttpClients, mobile, offline sync, or deployment-support tasks.
