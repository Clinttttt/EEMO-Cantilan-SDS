# OPUS_IMPLEMENTATION.md

> Companion to `OPUS_AGENTS.md`.
> `OPUS_AGENTS.md` defines the strict role and rules.
> This file defines **how you actually build and wire features** end-to-end.
> When the two conflict, `OPUS_AGENTS.md` wins.

## Identity

You are the implementation engineer for EEMO Cantilan SDS.

Your job here is to **write code that ships a working feature**: from the database, through the layers, out to the API, and into the Blazor / MAUI client.

You wire things together. You do not leave half-connected stubs.

A feature is not "done" until the full path works: **UI → HTTP → API → Application → Infrastructure → DB → back to UI.**

---

## Required Reading Order

Before wiring anything, read:

1. `.amazonq/context/knowledge/arch-rules.md`
2. `.amazonq/context/knowledge/patterns.md`
3. `.amazonq/context/knowledge/ARCHITECTURE_DOCUMENTATION.md`
4. `.amazonq/context/knowledge/EEMO_Complete_Documentation.md`
5. `.kiro/steering/CONTEXT.md`

Then find an existing feature that resembles the one you are building and **copy its wiring pattern**.

Do not invent a new flow when an existing feature already shows the way.

> Note on architecture: this project is **layered Clean Architecture + CQRS** (layer-first folders, feature sub-folders inside). It is **not** Vertical Slice Architecture. "End-to-end" below means the path through these layers — not collapsing them into one feature folder.

---

## Solution Layout (where code goes)

| Layer | Project | What lives here |
|-------|---------|-----------------|
| Domain | `EEMOCantilanSDS.Domain` | Entities, enums, constants, `Result`, value objects |
| Application | `EEMOCantilanSDS.Application` | Commands, Queries, DTOs, Requests, Validators, MediatR handlers |
| Infrastructure | `EEMOCantilanSDS.Infrastructure` | Repositories, `AppDbContext`, EF config, migrations, services |
| API | `EEMOCantilanSDS.Api` | Controllers, middleware, DI wiring |
| Web client | `EEMOCantilanSDS.Client` | Blazor pages/components, typed HttpClients, auth handlers |
| Mobile client | `EEMOCantilanSDS.Mobile` | MAUI Blazor pages/components |

Put each new file in the same folder as its siblings (e.g. a new query goes under `Application/Queries/<Feature>`).

---

## End-to-End Wiring Order

Build a feature **bottom-up**, then connect the client last.

1. **Domain** — add/adjust the entity, enum, or constant. Keep business rules in the domain.
2. **EF Configuration + Migration** — add `IEntityTypeConfiguration`, then create a migration. Never hand-edit the model snapshot.
3. **Repository** — add the method to the interface (`Application/Common/Interface`) and implement it in `Infrastructure/Repositories`. All data access goes through repositories.
4. **DTO** — define the shape the API returns. Never return entities.
5. **Command / Query + Handler** — MediatR handler orchestrates: calls repository, maps to DTO, returns `Result<T>`.
6. **Validator** — add a FluentValidation validator; it runs via `ValidationBehavior`.
7. **Controller** — thin endpoint that sends the command/query and translates `Result` to an HTTP response.
8. **DI registration** — register the repository/service in the relevant `DependencyInjection.cs`.
9. **Client HttpClient/service** — add the call in `EEMOCantilanSDS.Client` (typed client) and any DTO mirror.
10. **Blazor page/component** — call the service, bind state, handle loading/error/empty.
11. **Mobile** — mirror the client wiring if the feature is needed on MAUI.

Do not skip a step and "fake" the layer above it.

---

## Layer Rules (keep the wiring clean)

Controllers:

* Stay thin. No business logic, no EF, no `DbContext`.
* Inherit the existing base controller and send via MediatR.
* Map `Result` success/failure to proper status codes.

Handlers:

* Orchestrate only. No `DbContext`. No raw EF queries.
* Depend on repository interfaces, not concrete classes.
* Return `Result` / `Result<T>`, never throw for expected failures.

Repositories:

* Own all EF Core access.
* Return entities or projections to the handler, not to the API.
* Use `AsNoTracking` for reads, projections for lists, pagination for collections.

Client:

* Use the existing typed `HttpClient` setup and auth delegating handlers.
* Mirror DTOs; never reference server entities.
* Never put secrets or tokens in component code; rely on the existing auth/token services.

---

## Auth & HTTP Wiring

This solution already has an auth pipeline. Reuse it; do not rebuild it.

* Client calls flow through the auth proxy / delegating handlers
  (`AuthorizationDelegatingHandler`, `RefreshTokenDelegatingHandler`, `AuthProxyController`).
* Tokens are issued/validated by `TokenService` and surfaced via `CurrentUserService`.
* New protected endpoints must require auth consistently with sibling controllers.
* **Never** add a new network-exposed endpoint without confirming its auth/authorization matches the existing pattern. Flag it if it would be anonymous.

---

## DTO & Mapping Rules

* API in/out types are DTOs, defined in `Application/Dtos/<Feature>`.
* Map entity → DTO inside the handler (or AutoMapper profile if that is the existing pattern).
* The client gets its own DTO mirror; keep field names/shape in sync.
* If you change a DTO, update **every** consumer (controller, client service, component, mobile).

---

## Migrations

* Add `dotnet ef migrations add <Name>` from the Infrastructure project after changing entities/config.
* Review the generated `Up`/`Down` before accepting it.
* Never edit `AppDbContextModelSnapshot.cs` by hand.
* Confirm the migration matches the intended schema change and nothing extra.

---

## Reporting & Financial Wiring

Reports are high-risk. When wiring report features:

* Push aggregation/filtering to the database (server-side), not into the client.
* Verify totals, date filtering, delinquency, collections, and outstanding balances against the business rules.
* Keep money/units typed correctly end-to-end; do not lose precision across DTO boundaries.

Financial inaccuracy is unacceptable.

---

## Verification Before "Done"

After wiring, you must:

1. `dotnet build` the solution — it must succeed.
2. Run affected tests in `EEMOCantilanSDS.Testing` (`dotnet test`).
3. Add tests for the new handler/repository/validator behavior.
4. Manually trace the full path once: client call → controller → handler → repository → DB → response → UI binding.
5. Check the new endpoint's auth matches siblings.
6. Confirm no entity leaks through the API and no `DbContext` leaked into a handler.

Clean up any scratch/temp files you created.

---

## Completion Criteria

A wired feature is complete only when:

* The full path works end-to-end (UI → API → handler → repository → DB → back to UI).
* Build succeeds and tests pass (new tests added).
* Architecture and dependency direction are preserved.
* DTOs are consistent across API, client, and mobile.
* Auth is consistent with existing endpoints.
* No half-connected stubs remain.

Leave the codebase wired correctly and better than you found it.
