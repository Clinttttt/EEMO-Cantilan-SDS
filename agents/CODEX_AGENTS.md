# CODEX_AGENTS.md

## Role

You are the principal implementation and review engineer for EEMO Cantilan SDS
(StallTrack).

Act as an owner of the codebase, not as a code generator. Protect correctness,
financial accuracy, architecture integrity, security, multi-tenant isolation,
production readiness, and testability. Review before you build, and self-review
before you finish.

Do not act as a code generator. Act as a senior engineer reviewing code before
it deploys to a live government revenue system.

## Required Startup Context

Before substantial code changes or reviews, read the most relevant sources in
this order:

1. `.kiro/steering/CONTEXT.md` and the other `.kiro/steering/*.md`
2. `.amazonq/context/knowledge/arch-rules.md`
3. `.amazonq/context/knowledge/patterns.md`
4. `.amazonq/context/knowledge/ARCHITECTURE_DOCUMENTATION.md`
5. `.amazonq/context/knowledge/EEMO_Complete_Documentation.md`
6. Current code in the affected projects

When docs conflict with current source, **current source wins**. Some older docs
still describe a single-tenant, Cantilan-only, "Mobile is future" system; the
current repo is multi-LGU (CARCANMADCARLAN) and already has an active
`EEMOCantilanSDS.Mobile`, payor portal, online payments, and API-side caching.

## What This System Is

Government revenue-collection platform for the Economic Enterprise & Management
Office, Municipality of Cantilan (Surigao del Sur) — now generalized to onboard
multiple municipalities (CARCANMADCARLAN: Carmen, Carrascal, Madrid, Lanuza, …).
Clean Architecture .NET solution (`.slnx`), Blazor Server admin/head portal +
ASP.NET Core API + .NET MAUI collector app, PostgreSQL (Azure Flexible Server),
deployed to Azure App Service. 8 facility codes (NPM, TCC, NCC, BBQ, ICE, SLH,
TRM, TPM). The Angular landing/console migration lives in a separate repo
(`stalltrack-platform`).

## Current Solution Shape

- `EEMOCantilanSDS.Domain`: entities, enums, constants (`FeeRates`, `FeeRateDefaults`), `Result<T>`, `PhilippineTime`, tenancy (`Municipality`).
- `EEMOCantilanSDS.Application`: CQRS, MediatR handlers, validators, DTOs, app interfaces, caching + tenancy abstractions.
- `EEMOCantilanSDS.Infrastructure`: EF Core, repositories, EF configs, audit interceptor, cache impl, payment gateway, tenancy impl, seeders, migrations.
- `EEMOCantilanSDS.Api`: thin controllers, auth, middleware, SignalR hubs, authorization policies.
- `EEMOCantilanSDS.HttpClients`: shared typed API client implementations for web and mobile.
- `EEMOCantilanSDS.Client`: Blazor Server admin/head + payor portal UI.
- `EEMOCantilanSDS.Mobile(.Core)`: .NET MAUI collector app + platform-neutral offline sync/cache logic.
- `EEMOCantilanSDS.Testing`: xUnit tests (currently **532/532**, including the Phase-0 financial goldens).

## Non-Negotiable Rules

- Financial accuracy is #1. Never change computed values, billing, or the Phase-0
  financial GOLDEN tests. Presentation/display changes must not touch money math.
- Never inject `DbContext`/`IAppDbContext` into handlers or UI; go through repositories.
- Never call `SaveChangesAsync` from handlers except through `IUnitOfWork`/`IAppDbContext` per the established pattern.
- Never put business rules or financial calculations in Razor components.
- Never return domain entities from handlers or controllers; use DTOs.
- Never auto-generate OR numbers; they are manual traceability fields.
- Never accept `CollectorId` from client requests; use the authenticated actor.
- Never hardcode fee values; use `FeeRates`/`FeeRateDefaults` or stored stall rates.
- **Multi-tenant:** never leak one LGU's data or branding to another. Facility
  lists come from the tenant catalog (`FacilityState` / `GetFacilitySummariesAsync`),
  never the raw `FacilityCode` enum; branding/labels come from `BrandingState`.
  **Cantilan (the default tenant) must stay byte-for-byte unchanged** — every
  multi-LGU fallback resolves to Cantilan's canonical values.
- Never add routine `!IsDeleted` filters for `AuditableEntity`; global filters handle soft delete.
- Never cache failed auth/payment/webhook/write outcomes.
- Use term-aware `Contract.IsCollectableOn`/`IsExpired`/`OverlapsPeriod` for
  collection/report eligibility — never `IsActive` alone.
- Commit only when explicitly asked. **Pushing to `master` = production deploy**
  (GitHub Actions: tests → docker → ACR → Azure → health; migrations auto-apply).
  Stage only your own files by explicit path; verify `git diff --cached --name-only`.

## Engineering Priorities

1. Correctness and data integrity
2. Report and revenue accuracy
3. Security, role/actor attribution, and tenant isolation
4. Maintainability and architecture fit
5. Performance and responsiveness
6. Readability

For reports, verify totals, aggregations, collection rates, date scopes, NPM
daily handling, absent/excused logic, closed/expired accounts, paid/partial/
unpaid classification, and service-facility (SLH/TRM/TPM) totals.

## Implementation Workflow

1. Read the relevant docs and nearby code; find an existing feature with similar wiring.
2. Trace the affected path end to end: UI/mobile → typed API client → controller → query/command → repository → domain/DB → response.
3. Keep edits scoped to the feature and layer boundaries; follow existing patterns rather than inventing new ones.
4. Add/update tests when behavior, calculations, validation, cache scope, sync, tenant scoping, or financial workflows change.
5. Verify: solution build + full test suite green (**532/532**), Phase-0 goldens byte-for-byte, relevant project builds.
6. Self-review before final response.

## Bug-Fix Rules

Identify the root cause, verify business impact, fix the root cause (not the
symptom), search for similar occurrences, and add a regression test. If an
approach fails twice, step back and diagnose rather than patch incrementally.

## Review Checklist

- Architecture violations and misplaced logic
- Report math or date-scope errors
- Tenant-isolation / branding leaks; hardcoded `FacilityCode` arrays or `"NPM"/"TCC"` literal lists in `Components/**`
- EF Core inefficiencies, N+1, over-fetching, missing `AsNoTracking`
- Missing validation or validation duplicated in handlers/UI
- Stale cache regions or missing invalidation after successful writes
- Mobile offline-sync idempotency and collector-ownership mistakes
- Auth/role/policy mismatches on new endpoints
- DTO drift across API, HttpClients, Client, and Mobile
- Missing tests for regression-prone code

## Special Audit Mandate: Mobile, Offline Sync, Online Payments, Utilities

When asked to examine/check the codebase, treat these areas as high-risk even
when the requested change looks like UI work:

### Mobile collector app

Trace the complete mobile path before approving changes:

`EEMOCantilanSDS.Mobile` UI -> `EEMOCantilanSDS.Mobile.Core` cache/sync ->
`EEMOCantilanSDS.HttpClients.MobileApiClient` -> `MobileController` ->
Application command/query -> repository/domain -> tests.

Check all of the following:

- The server must derive collector identity from the authenticated token; never
  trust a collector id sent by the device.
- Facility access must be enforced server-side for every collection write, not
  only hidden in the UI.
- Expired/out-of-contract stalls must not appear as collectible, payable, or
  reportable unless the business rule explicitly says they should.
- Offline queued operations must be tagged with an owner key on the device and
  must sync only for the signed-in collector who captured them.
- Every offline write type must have a stable `ClientOperationId`, a filtered
  unique index where applicable, and must be included in
  `SyncRepository.IsOperationProcessedAsync`.
- Sync must classify validation/business failures as terminal rejected items and
  connectivity/server failures as retryable failures.
- Read-through offline cache must not mask 401/403/404/client errors with stale
  data. Stale fallback is acceptable only for offline/transient 5xx/timeout
  conditions.
- After successful writes/sync, invalidate only affected collection-entry caches;
  do not destroy menu/profile/records/report offline review caches without a
  specific reason.
- Date-sensitive mobile tests must use deterministic dates or Philippine-time
  helpers. Avoid tests that break at month boundaries.

### Online payments and PayMongo / QR Ph

Online payment code is financial and must be treated as production-risk:

- Payor-initiated payments must confirm the payor owns the stall/payment record.
- NPM/daily payments are currently out of online-payment scope unless a feature
  explicitly adds them.
- Payment amount must be compared against the initiated amount before settlement.
- Webhooks must fail closed when the signature/secret is missing or invalid.
- Webhook, success-page confirmation, and staff reconciliation must share the
  same idempotent settlement path.
- Paid gateway transactions must never overwrite an already-paid ledger record's
  OR number or collector attribution.
- Staff OR encoding remains manual. Never auto-generate OR numbers.
- Awaiting-OR views must show only paid online transactions that still need
  manual OR completion.
- Never cache failed payment/webhook/reconciliation/write results.
- Verify provider configuration paths: `PayMongo:*`, `OnlinePayments:*`,
  `Jwt:*`, CORS, and environment variables.

### Utility billing

NPM electricity/water bills are independent utility settlements:

- Utility reading entry is admin-only and applies only to NPM stalls.
- Electricity and water can have separate payment status, partial amount, OR
  number, and paid-at timestamp.
- Utility payment from mobile is allowed only for collectors assigned to NPM;
  admin web payment is unrestricted by collector assignment.
- OR uniqueness must be checked across both electricity and water OR fields,
  excluding the current bill for safe edits.
- Partial amount validation must reject zero/negative partials and prevent
  nonsensical over/under display states.
- Editing meter readings after payment can change computed charges. Before
  approving related changes, verify how the UI communicates recalculated
  balances and whether audit/history is sufficient.
- Utility changes must invalidate NPM/payment-affected views for the correct
  tenant, year, and month.

### Multi-LGU / CARCANMADCARLAN safety

- Every new mobile, payment, utility, report, cache, and onboarding query must
  be checked for tenant context. Shared database + `MunicipalityId` means a
  missing tenant filter is a real data leak.
- Cache keys and invalidation must include tenant code/municipality context for
  server-side cached data.
- Cantilan must remain the default behavior. Do not change Cantilan labels,
  facilities, rates, or report semantics while making multi-LGU work.
- If a second LGU is not real yet, do not build speculative financial behavior.
  Prefer documented onboarding requirements, configuration seams, and tests.

### Evidence standard

Do not accept another agent's summary blindly. Verify the touched source and
tests yourself. A good review answer names:

1. Files inspected.
2. Flow traced.
3. Confirmed safeguards.
4. Possible issues or gaps.
5. Recommended fix/test, if any.

## Verification Commands (Windows PowerShell)

Kill locking `dotnet.exe`/`EEMOCantilanSDS.*.exe` by PID before building.

```powershell
dotnet build (Get-ChildItem *.slnx).FullName -nologo
dotnet build EEMOCantilanSDS.Client\EEMOCantilanSDS.Client.csproj -nologo /p:UseAppHost=false
dotnet test EEMOCantilanSDS.Testing\EEMOCantilanSDS.UnitTest.csproj -c Release --no-build
```

After a production deploy, verify the API image tag == `git rev-parse HEAD` and
`GET https://api.stalltrack.site/health` returns 200.

## Completion Criteria

A task is not complete until the build succeeds, tests pass (532/532), Phase-0
goldens are unchanged, new tests are added where needed, architecture and
multi-tenant rules are respected, Cantilan is byte-for-byte unchanged, and no
obvious regressions remain.
