# Implementation Patterns

## Architecture

Dependency flow stays inward:

`Client/Mobile/API -> Application -> Domain`; Infrastructure implements
Application interfaces and references Application + Domain.

Do not collapse this into vertical slices. Feature folders exist inside the
Application layer, but layer boundaries remain strict.

## CQRS

Application command/query folders normally contain:

- `{Name}Command.cs` or `{Name}Query.cs`
- `{Name}CommandHandler.cs` or `{Name}QueryHandler.cs`
- `{Name}CommandValidator.cs` or `{Name}QueryValidator.cs`

Handlers use primary constructors. Query handlers inject repositories/services
needed for reads. Command handlers inject repositories plus `IUnitOfWork`.
Expected failures return `Result<T>`; do not throw for normal not-found,
forbidden, validation, or conflict outcomes.

## Repositories and EF Core

- Repository interfaces live in `Application/Common/Interface/Persistence`.
- Implementations live under `Infrastructure/Repositories`, but namespace is flat: `EEMOCantilanSDS.Infrastructure.Repositories`.
- `FacilityReportsRepository` is split across partial files.
- Repositories own EF access. Do not return `IQueryable`.
- Use `AsNoTracking` for reads.
- Do not add routine `!IsDeleted` filters for `AuditableEntity`; `AppDbContext` applies global filters.
- Recalculate computed properties in LINQ from stored fields; computed C# properties are not database columns.
- PostgreSQL types only: `text`, `character varying(n)`, `boolean`, `uuid`, `timestamp with time zone`, `numeric(18,2)`, `integer`, `jsonb`.

## Controllers and API Clients

- Controllers inherit `ApiBaseController`.
- Controller actions should bind parameters/body, send a command/query through `Sender`, then call `HandleResponse`.
- Use `ActionResult<T>` where `T` matches the `Result<T>` payload.
- API-client interfaces live in Application.
- Implementations live in `EEMOCantilanSDS.HttpClients/ApiClients` and derive from `HandleResponse`.
- Blazor registers clients through `AddApiHttpClient<TClient,TImplementation>`.
- `HandleResponse.UpdateAsync` sends PATCH; use `PutAsync` for PUT routes.

## Blazor Client

- Blazor components call typed API clients only.
- Components do display, state, and event wiring; no business rules or financial math.
- Use component-scoped `.razor.css` plus existing design tokens.
- Do not use `<form>` for project UI patterns unless existing local code requires it.
- For heavy routed pages, set loading state and render a shell/skeleton before slow API work.
- Show first validation error per field; do not inspect raw status codes in components.

## Server Cache

Application abstractions:

- `IEemoAppCache`
- `IEemoCacheInvalidator`
- `EemoCacheKeys`
- `EemoCacheRegions`
- `EemoCacheOptions`
- `ITenantContext`

Infrastructure implementation:

- `MemoryEemoAppCache`
- `MemoryEemoCacheInvalidator`
- `StaticTenantContext`

Cache final successful read-model DTOs at query-handler boundaries. Include all
result-shaping parameters and tenant code in keys. Use region invalidation via
change tokens. Invalidate after successful `SaveChangesAsync`.

Do not cache auth, token refresh, command/write results, webhook handling,
payment gateway mutations, or failures.

## Mobile Offline Sync

Mobile has two separate caching/sync concepts:

- `CachingMobileApiClient`: offline read-through cache for mobile GET views.
- `MobileSyncService`: pending operation queue and sync lifecycle for offline collection captures.

Offline financial writes use a `ClientOperationId` idempotency key stamped on
the financial entity. `SyncOfflineCollectionsCommandHandler` replays existing
validated commands via `ISender` instead of duplicating business logic.

Per-item sync outcomes:

- `Synced`: remove/mark complete locally.
- `Rejected`: terminal business validation issue; keep visible for manual action.
- `Failed`: transient; retry later.

Collectors may only sync their own queued operations. Do not let owner-less or
another collector's rows be silently misattributed.

## Online Payments and Payor Portal

PayMongo hosted checkout confirms payment receipt. Official Receipt entry
remains a staff/admin action. Paid online transactions can be `Paid` while
awaiting OR, then `Completed` after staff encodes OR.

Webhook/confirmation flows must be idempotent and must not be cached.

## Audit

`AuditSaveChangesInterceptor` writes audit rows for financial transaction
entities and account/payor/stall management changes. It redacts credential
fields and skips routine auth housekeeping changes. Do not manually write
audit rows from handlers unless a future task explicitly changes the audit
architecture.
