# EEMO Web Admin Caching Architecture Guidance for Opus 4.8

> Reference only. Do not trust this GPT/Codex note blindly.
> Before implementing anything, examine the current EEMO codebase, query handlers, repositories, DTOs, tests, and recent commits yourself. If the implementation has changed, or if a safer design is visible in the code, prefer the verified code context over this document.

## Purpose

Design a clean, maintainable `IMemoryCache` architecture for the EEMO web admin/API side.

The goal is to improve dashboard/report responsiveness and reduce repeated expensive aggregation work without rewriting financial calculations.

This document is not asking for Redis, distributed caching, financial-engine consolidation, or any rewrite of collected/compliance/rate/occupied/breakdown logic.

## Current codebase shape observed

Repository inspected:

`C:\dev\EEMOCantilanSDS`

Relevant current architecture:

- API controllers are thin and call MediatR through `ISender`.
- Application layer contains query/command handlers.
- Infrastructure layer contains EF Core repositories.
- Mobile project already has its own offline cache through `CachingMobileApiClient`, but that is separate from web admin/API caching.
- Web/API side does not currently appear to register `AddMemoryCache()`.
- Heavy web admin reads are mostly under:
  - `DashboardController`
  - `FacilitiesController`
  - `ReportsController`
- Heavy read handlers include:
  - `GetDashboardOverviewQueryHandler`
  - `GetFacilitySummariesQueryHandler`
  - `GetFacilityReportsQueryHandler`
  - `GetFinancialReportQueryHandler`
  - `GetMonthEndReportQueryHandler`
  - `GetCollectionReportQueryHandler`
  - `GetFollowUpQueueQueryHandler`
  - `GetFacilityHistoryQueryHandler`
  - `GetStallHoldersListQueryHandler`
- Many heavy handlers compose canonical repositories such as:
  - `IFacilityReportsRepository`
  - `IDashboardRepository`
  - `ISlaughterRepository`
  - `ITrmRepository`
  - `ITpmRepository`
  - `ITransactionFeedRepository`

Important observation:

The financial/report logic is intentionally composed from existing canonical sources. It includes NPM daily behavior, monthly rentals, absent/excused periods, market closures, closed stalls, paid/partial/unpaid states, and service facilities. Caching is safer than consolidating or rewriting those calculations.

## Recommended high-level architecture

Use `IMemoryCache` first.

Do not introduce Redis yet unless:

- the API runs on multiple app instances,
- multiple servers must share cache state,
- the app needs cache survival across process restarts,
- or production traffic grows beyond the single-instance capstone/demo deployment.

For the current EEMO shape, the cleanest design is:

```text
API composition root
  registers IMemoryCache and cache services

Application query handlers
  cache expensive read models at the handler boundary

Application command handlers
  invalidate affected cache regions after successful SaveChangesAsync

Infrastructure repositories
  remain canonical data access and financial aggregation sources
```

Avoid caching inside Razor components. Avoid starting with deep repository caching. Keep the first version visible at the use-case level: queries cache their final DTOs, writes invalidate the affected read models.

## Recommended folder/abstraction shape

Suggested new Application-level area:

```text
EEMOCantilanSDS.Application
└── Common
    └── Caching
        ├── IEemoAppCache.cs
        ├── IEemoCacheInvalidator.cs
        ├── EemoCacheKeys.cs
        ├── EemoCacheRegions.cs
        └── EemoCacheOptions.cs
```

Suggested tenant/LGU context abstraction:

```text
EEMOCantilanSDS.Application
└── Common
    └── Tenancy
        ├── ITenantContext.cs
        └── TenantConstants.cs
```

Suggested Infrastructure/API implementation area:

```text
EEMOCantilanSDS.Infrastructure
└── Caching
    ├── MemoryEemoAppCache.cs
    └── MemoryEemoCacheInvalidator.cs
└── Tenancy
    └── StaticTenantContext.cs
```

Then register in dependency injection:

```csharp
services.AddMemoryCache();
services.AddSingleton<IEemoAppCache, MemoryEemoAppCache>();
services.AddSingleton<IEemoCacheInvalidator, MemoryEemoCacheInvalidator>();
services.AddScoped<ITenantContext, StaticTenantContext>();
```

If the team prefers keeping memory-cache implementation in the API project instead of Infrastructure, that is acceptable. The important part is that query handlers depend on small app abstractions, not directly on scattered `IMemoryCache` calls everywhere.

## Tenant context design

Do not hardcode this inside every handler:

```csharp
var tenant = "cantilan";
```

That is acceptable only as a short example, not as the real implementation style.

Use a reusable tenant/LGU abstraction instead:

```csharp
public interface ITenantContext
{
    string TenantCode { get; }
}
```

For the current Cantilan-only implementation, keep the behavior simple:

```csharp
public static class TenantConstants
{
    public const string Cantilan = "cantilan";
}

public sealed class StaticTenantContext : ITenantContext
{
    public string TenantCode => TenantConstants.Cantilan;
}
```

Then query handlers receive `ITenantContext`:

```csharp
public class GetDashboardOverviewQueryHandler(
    IDashboardRepository dashboardRepository,
    IEemoAppCache appCache,
    ITenantContext tenantContext)
    : IRequestHandler<GetDashboardOverviewQuery, Result<DashboardOverviewDto>>
{
    public async Task<Result<DashboardOverviewDto>> Handle(GetDashboardOverviewQuery request, CancellationToken ct)
    {
        var tenant = tenantContext.TenantCode;
        var key = EemoCacheKeys.DashboardOverview(tenant, request.Year, request.Month);

        // cache read model here
    }
}
```

Why this is cleaner:

- Current system stays Cantilan-first.
- Cache keys are already LGU-safe.
- Later CARCANMADCARLAN support can resolve the tenant from subdomain, route, login selection, user claim, header, or database configuration.
- Query handlers do not need to be rewritten later.
- The implementation stays aligned with Clean Architecture: Application depends on an abstraction, while Infrastructure/API supplies the concrete tenant source.

For now, do not build the full multi-tenant resolver unless the current task explicitly asks for it. Start with `StaticTenantContext`, then replace it later when CARCANMADCARLAN becomes active implementation work.

## Cache at the query-handler boundary

Prefer this:

```text
GetDashboardOverviewQueryHandler
  -> build key from tenant/year/month
  -> appCache.GetOrCreateAsync(...)
  -> inside factory, call dashboardRepository.GetOverviewAsync(...)
  -> return Result.Success(dto)
```

Avoid this for the first implementation:

```text
Dashboard.razor caches API responses
Controllers contain cache details
Repositories cache random EF results internally
```

Reason:

- Query handlers are already the use-case boundary.
- They know the full request shape.
- They return final DTOs.
- They are easier to test.
- They reduce the chance of accidentally caching partial EF entities or hidden stale repository results.

## Cache only successful read results

Do not cache:

- failed `Result<T>`
- authorization failures
- validation failures
- exceptions
- empty data caused by transient database/API errors

Cache only successful DTO payloads.

This avoids the dangerous scenario where a temporary failure becomes a cached blank dashboard/report.

## Cache key design

Every cache key must include all parameters that change the result.

Minimum key parts:

- tenant/LGU code, even if current value is only `cantilan`
- feature name
- facility code when applicable
- report period
- year
- month
- week number when applicable
- search/filter/scope when applicable

Future-safe pattern:

```text
{tenant}:{feature}:{scope}:{year}:{month}:{filters}
```

Examples:

```text
cantilan:dashboard:overview:2026:06
cantilan:facilities:sidebar:2026:06
cantilan:facility-report:NPM:Monthly:2026:06:week-none
cantilan:financial-report:Monthly:2026:06:facility-all
cantilan:month-end-report:2026:06
cantilan:collection-report:2026:06
cantilan:follow-up:2026:06
cantilan:facility-history:NPM:2026
cantilan:stallholders:facility-all:active-only
```

Do not design keys as if Cantilan will forever be the only LGU. The CARCANMADCARLAN direction means future tenant/LGU separation matters. Even a constant `cantilan` prefix today prevents painful key collisions later.

### Normalize request defaults before building keys

Some handlers use current Philippine date defaults when an optional value is not supplied.

Example pattern currently visible in the codebase:

```csharp
var anchorMonth = request.Month ?? PhilippineTime.Today.Month;
```

Do not build cache keys from the raw nullable request value if the handler later normalizes it.

Risky:

```text
cantilan:financial-report:Monthly:2026:month-null
```

Better:

```text
cantilan:financial-report:Monthly:2026:06
```

Rule:

> Compute the effective period/scope first, then build the cache key from that normalized value.

This prevents incorrect cache reuse across month boundaries and avoids multiple keys for the same logical report.

Also be careful with DTOs that contain generated dates such as `PhilippineTime.Today`. Either:

- include the generated/as-of date in the cache key,
- keep the TTL very short,
- or set generated display timestamps outside the cached payload if the data itself is cacheable.

## Best invalidation design for `IMemoryCache`

`IMemoryCache` does not support native remove-by-prefix.

Do not scatter hundreds of `cache.Remove("literal:key")` calls across command handlers.

Recommended design:

Use cache regions/groups with `CancellationChangeToken`.

Concept:

- Each cached entry belongs to one or more regions.
- A region has a `CancellationTokenSource`.
- When a write changes data, invalidate the relevant region by cancelling its token.
- All cache entries attached to that region expire together.

Example regions:

```text
tenant:cantilan:period:2026:06
tenant:cantilan:facility:NPM:period:2026:06
tenant:cantilan:reports:2026:06
tenant:cantilan:dashboard:2026:06
tenant:cantilan:reference
tenant:cantilan:payor:{payorId}
```

This is cleaner than manual prefix deletion and safer than global cache clearing.

Important implementation detail:

- Replace the cancelled `CancellationTokenSource` with a new one after invalidation.
- Dispose old token sources carefully.
- Keep region names centralized in `EemoCacheRegions`.

## Recommended invalidation groups

Invalidate after successful `SaveChangesAsync`, not before.

### Stall/rental payment writes

Likely handlers:

- `RecordPaymentCommandHandler`
- `SaveOrNumberCommandHandler`
- `IssueOnlinePaymentOrNumberCommandHandler`
- `ConfirmOnlinePaymentCommandHandler`
- `HandlePaymentWebhookCommandHandler`
- `OnlinePaymentSettlementService`
- mobile sync path that dispatches `RecordPaymentCommand`

Invalidate:

- affected period region
- affected facility-period region
- dashboard period region
- reports period region
- follow-up period region
- payor-specific region if payor views are cached later

### NPM daily collection writes

Likely handlers:

- `RecordDailyCollectionCommandHandler`
- `SaveDailyCollectionOrNumberCommandHandler`
- mobile sync path that dispatches `RecordDailyCollectionCommand`

Invalidate:

- affected NPM facility-period region
- affected period region
- dashboard period region
- reports period region
- follow-up period region

### Monthly exception / absence / market closure writes

Likely handlers:

- `SetStallMonthlyExceptionCommandHandler`
- `ClearStallMonthlyExceptionCommandHandler`
- `SetNpmMarketClosureCommandHandler`
- `ClearNpmMarketClosureCommandHandler`

Invalidate:

- affected period region
- affected facility-period region
- dashboard period region
- reports period region
- follow-up period region

Reason:

These change expected bill, unpaid balance, collection rate, report visibility, and follow-up meaning.

### Stall/contract/facility setup writes

Likely handlers:

- `CreateStallCommandHandler`
- `BulkImportStallholdersCommandHandler`
- `UpdateStallCommandHandler`
- `UpdateStallDetailsCommandHandler`
- `ToggleStallStatusCommandHandler`
- `RenewStallContractCommandHandler`
- collector/facility assignment changes if dashboards depend on active collector coverage

Invalidate:

- reference/config region if facility/stall setup is cached
- facility summary regions
- affected facility reports/history
- dashboard period region
- follow-up period region
- stall-holder list region

Bulk import is especially important. It can create many stallholders/contracts in one command, so it should invalidate the same views as manual stall creation:

- affected facility summaries
- dashboard period summaries
- financial/month-end/collection reports for affected periods
- follow-up queue/history for affected periods
- stall-holder list
- closed/inactive account registers if the import flow ever supports inactive rows

For the first implementation, broad invalidation after a successful bulk import is safer than trying to surgically infer every affected month. If the import accepts contract start/effectivity dates, invalidate at least the current period and any affected contract periods represented by the uploaded rows.

### SLH/TRM/TPM service-facility writes

Likely handlers:

- `RecordSlaughterCommandHandler`
- `UpdateSlaughterCommandHandler`
- `SaveSlaughterOrNumberCommandHandler`
- `RecordTripCommandHandler`
- `SaveTripOrNumberCommandHandler`
- `AddVendorToMarketDayCommandHandler`
- `UpdateTpmVendorCommandHandler`
- `MarkVendorPaidCommandHandler`
- `SaveVendorOrNumberCommandHandler`

Invalidate:

- affected period region
- affected service facility-period region
- dashboard period region
- reports period region
- follow-up period region

## First cache targets

Start with small, high-value, low-risk read models.

| Priority | Read model | Suggested TTL | Why |
|---|---:|---:|---|
| 1 | Dashboard overview | 30-60 seconds | Frequently loaded; expensive aggregation; safe to be briefly stale |
| 1 | Facility sidebar summaries | 30-60 seconds | Used during navigation; repeated calls; badge counts can tolerate tiny delay |
| 1 | Financial report | 60-180 seconds | Heavy page; composed from many sources; good visible speed gain |
| 2 | Facility reports | 60-180 seconds | Canonical facility aggregation reused in several places |
| 2 | Month-end report | 60-300 seconds | Print/review payload; expensive but not second-by-second operational |
| 2 | Collection/export report | 60-300 seconds | Heavy report/export view |
| 2 | Follow-up queue | 30-120 seconds | Operational page; useful cache but keep shorter TTL |
| 2 | Follow-up history | 2-10 minutes | Past-period reconstruction; less volatile than live queue, but still affected by backfilled payments/contracts |
| 3 | Facility history | 5-15 minutes | Historical data changes less often |
| 3 | Stall holder list | 5-15 minutes | Mostly setup data; invalidate after stall/contract changes |
| 3 | Static/reference data | 10-30 minutes | Facility names/config/rates if centralized later |

Recommendation for first PR:

1. Register `AddMemoryCache()`.
2. Add cache abstractions and memory implementation.
3. Add key/region helpers.
4. Cache only:
   - dashboard overview
   - facility sidebar summaries
   - financial report
5. Add invalidation after the write flows that obviously affect those three read models.
6. Add tests for key generation, cache hit, cache miss, and invalidation.

Do not cache everything in one PR.

If Follow-up History is cached later, treat it differently from the live Follow-up Queue:

- live queue should stay short TTL because it is operational
- history can have a longer TTL, but must still invalidate after backfilled payments, OR issuance, stall closure/reopen, contract renewal, and monthly exception changes
- never let current-day contract attention leak into historical snapshots; cache keys must include the requested year/month

## What not to cache first

Avoid caching these in the first implementation:

- login/auth commands
- current user identity
- token refresh
- JWT/cookie validation
- PayMongo webhook handling
- PayMongo checkout/session creation
- online payment confirmation
- OR number writes
- command results from writes
- setup/admin password flows
- failed responses

For online payments, correctness and idempotency matter more than speed. If payment read views are cached later, use very short TTL and explicit invalidation after webhook/confirm/OR flows.

## Query handler cache pattern

Suggested style:

```csharp
public async Task<Result<DashboardOverviewDto>> Handle(GetDashboardOverviewQuery request, CancellationToken ct)
{
    var tenant = tenantContext.TenantCode;
    var key = EemoCacheKeys.DashboardOverview(tenant, request.Year, request.Month);
    var regions = EemoCacheRegions.ForPeriod(tenant, request.Year, request.Month)
        .Append(EemoCacheRegions.Dashboard(tenant, request.Year, request.Month));

    var dto = await appCache.GetOrCreateAsync(
        key,
        regions,
        ttl: TimeSpan.FromSeconds(60),
        factory: token => dashboardRepository.GetOverviewAsync(request.Year, request.Month, token),
        ct);

    return Result<DashboardOverviewDto>.Success(dto);
}
```

The final code should match the actual project style, but the idea is:

- central key builder
- central region builder
- short TTL
- success-only caching
- cancellation respected

## Command handler invalidation pattern

Suggested style:

```csharp
await unitOfWork.SaveChangesAsync(ct);

await cacheInvalidator.InvalidatePeriodAsync("cantilan", year, month, ct);
await cacheInvalidator.InvalidateFacilityPeriodAsync("cantilan", facilityCode, year, month, ct);
```

Keep invalidation method names business-friendly:

- `InvalidatePaymentAffectedViewsAsync(...)`
- `InvalidateFacilityPeriodAsync(...)`
- `InvalidatePeriodReportsAsync(...)`
- `InvalidateDashboardPeriodAsync(...)`
- `InvalidateReferenceDataAsync(...)`

Avoid forcing every command handler to know every exact cache key.

## Tenant/LGU readiness

Even before full CARCANMADCARLAN support exists, design cache signatures with tenant/LGU in mind.

Good:

```csharp
DashboardOverview(string tenantCode, int year, int month)
```

Risky:

```csharp
DashboardOverview(int year, int month)
```

Why:

If Cantilan, Carrascal, Madrid, Carmen, and Lanuza eventually share infrastructure, a dashboard cache for `2026-06` must not be reused across LGUs. Cache separation must be part of the design early.

For now, do not repeat `"cantilan"` inside every handler. Centralize it behind `ITenantContext`:

```csharp
public sealed class StaticTenantContext : ITenantContext
{
    public string TenantCode => TenantConstants.Cantilan;
}
```

Later, replace `StaticTenantContext` with a real tenant resolver without changing every query handler or cache key builder.

## Expiration policy

Use absolute expiration first.

Suggested defaults:

- dashboard overview: 30-60 seconds
- facility sidebar summaries: 30-60 seconds
- financial report: 60-180 seconds
- follow-up queue: 30-120 seconds
- facility reports: 60-180 seconds
- month-end/collection report: 60-300 seconds
- facility history: 5-15 minutes
- static/reference config: 10-30 minutes

Do not use long TTLs until invalidation is proven.

Sliding expiration is not necessary for the first version.

## Cache stampede protection

For the first version, `IMemoryCache.GetOrCreateAsync` is probably acceptable.

If heavy reports still recompute too often under concurrent requests, add a small per-key async lock later.

Do not complicate v1 unless profiling shows concurrent stampede is a real problem.

## Testing expectations

Add tests before broad rollout.

Recommended unit tests:

1. Same query twice returns cached DTO and avoids duplicate repository calls.
2. Different year/month/facility/period produce different keys.
3. Success result is cached.
4. Failure result is not cached.
5. Invalidation after a write causes the next read to recompute.
6. Invalidating one facility period does not clear unrelated facility periods.
7. Tenant prefix prevents cross-LGU cache collisions.

Recommended integration/smoke checks:

- Dashboard still updates after recording payment.
- Sidebar badges update after payment/absence changes.
- Financial report updates after payment and after monthly exception/closure changes.
- Online payment webhook/OR flows do not serve stale payment state.

## Do not rewrite financial calculations

Do not consolidate or rewrite:

- collected
- compliance
- collection rate
- occupied count
- unpaid breakdown
- NPM daily/monthly coverage
- absent/excused logic
- closed-stall inclusion/exclusion
- service-facility totals

Caching should wrap current outputs, not change how those outputs are calculated.

If a future consolidation is attempted, require snapshot-equivalence tests first across:

- NPM daily paid/partial/unpaid
- monthly rental paid/partial/unpaid
- absent/excused months
- NPM market closures
- closed stalls
- SLH/TRM/TPM service collections
- current month vs previous month reports
- all facilities vs single facility scopes

## Suggested acceptance criteria

The caching implementation is acceptable only if:

- no financial/report DTO values change unintentionally,
- cache keys include every result-shaping parameter,
- all cached keys include tenant/LGU prefix,
- write handlers invalidate affected cache regions after successful save,
- failures are not cached,
- auth/payment/webhook correctness is not masked,
- current tests pass,
- new tests cover key generation and invalidation,
- the first PR remains small enough to review confidently.

## Principal recommendation

Use a small, explicit Application-level caching abstraction with `IMemoryCache` behind it.

Cache final read-model DTOs at query-handler boundaries.

Invalidate with region/token groups after successful write commands.

Keep repositories as the source of truth.

Keep financial calculations untouched.

Keep tenant/LGU in every cache key from day one.
