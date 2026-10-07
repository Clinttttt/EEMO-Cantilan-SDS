# Fish / Meat registry management backend handoff — 2026-10-07

Implementation branch: `codex/fish-meat-registry-management-followup`, based on `23e5dd72`.
This extends the [collector-productivity contracts](COLLECTOR_PRODUCTIVITY_FOLLOWUP_HANDOFF_20261007.md), not their UI. Current user instruction and [ADR-007](../decisions/ADR_007_SOURCE_NATIVE_COLLECTION_IDENTITY.md) govern source identity. No dirty primary-checkout documentation was edited.

## Management API and typed client

All routes are authenticated and tenant-scoped. Head is the existing technical role `SuperAdmin`. Admin may read; Collector cannot use management reads or lifecycle mutations.

| Verb / route | Input | Output | Roles |
|---|---|---|---|
| GET `/api/office-sources/fish-meat/manage` | `taxYear`, `month`, optional `year`, optional `status` | `VendorRegistryManagement` | SuperAdmin, Admin |
| POST `/api/office-sources/fish-meat/registrations/{id}/close` | `CloseVendorRegistrationRequest` | `VendorRegistrationMutationResult` | SuperAdmin |
| POST `/api/office-sources/fish-meat/registrations/{id}/renew` | `RenewVendorRegistrationRequest` | `VendorRegistrationMutationResult` | SuperAdmin |

`IOfficeSourcesApiClient` and its authenticated HttpClients implementation expose `ManageRegistrationsAsync(taxYear, month, status=null, year=null)`, `CloseRegistrationAsync(id, request)`, `RenewRegistrationAsync(id, request)`. No UI/interface project changed.

Management query: `taxYear` selects annual registrations; `year` selects collection business-date year and defaults to `taxYear`; `month` is 1–12. Years are 2000–2200. Omitted `status` includes both statuses. Ordering is DisplayName then registration ID. Reads never mutate/reserve records. Prior-year records remain readable; a prior tax year does not automatically mean Closed.

`VendorRegistryManagement(TaxYear, Year, Month, Rows, WalkUpVendorFeeCollected)` contains:

- `Rows[]: VendorRegistrationManagementRow`
- `Registration`: existing `FishMeatVendorRegistrationDto(Id, TaxYear, VendorType, RegistrationKind, DisplayName, BusinessName?, Address?, Reference?)`
- `Status: VendorRegistrationStatus` (`Active=1`, `Closed=2`)
- `CreatedBy`, `RecordedAtUtc`
- nullable `ClosedOn`, `ClosedAtUtc`, `ClosedBy`, `CloseNote`
- nullable `PriorRegistrationId`
- decimal `VendorFeeCollected`, `WeightMeasureCollected`; derived `TotalCollected` is their sum.

The existing registration DTO/request remains compatible. Use `row.Registration.Id` as the management identity, never name text. Display lifecycle from `Status`; New/Renew (`VendorRegistrationKind.New=1`, `Renew=2`) is registration origin, not lifecycle. VendorType is exactly `Fish=1` or `Meat=2`.

## Close

`CloseVendorRegistrationRequest(ClientOperationId, Note=null)`: nonempty stable operation ID; optional note trims to null if blank, max 300. The ID in the URL identifies the exact registration. Actor and close business date come from server authentication/clock; UTC audit time is persisted at PostgreSQL microsecond precision.

Closing retains all registration facts, Collections, SRCs and frozen calculations. It records one irreversible lifecycle close event; there is no delete or reopen endpoint. No statutory approval process is introduced.

Same close ID, same registration and normalized note returns the same closure audit facts with `ExistingOutcome=true`, even on a later day. Changed intent under that ID conflicts. A different close ID against an already closed registration returns `RegistrationClosed`. Client retries must retain the same operation ID and facts.

## Renew

`RenewVendorRegistrationRequest(ClientOperationId, TaxYear, DisplayName=null, BusinessName=null, Address=null, Reference=null)` creates a new Active registration. The source registration ID is required in the URL. Target year must be greater than the source year and within 2000–2200. The source may be Active or Closed. Renewal does not close or mutate it.

Null optional request values copy source facts. Explicit values are validated/trimmed through the existing registration domain factory (name required/max 200; business 200; address 300; reference 200). Blank optional strings clear those optional values; a blank required name is invalid. VendorType is copied exactly and cannot change on renewal. Resulting RegistrationKind is Renew and `PriorRegistrationId` is the exact source ID. A Fish→Meat change requires the explicit New registration path.

Same operation/source/normalized resulting facts returns the existing registration ID. Changed source/year/facts conflict. A database unique index prevents more than one renewal for the exact tenant/source/target year, including concurrent requests; a distinct operation then receives `RenewalAlreadyExists` or retryable `RegistryConcurrencyConflict`. A genuinely separate registration uses the established explicit registration/import path; same-name records are never merged.

`VendorRegistrationMutationResult` contains `Registration`, `Status`, `PriorRegistrationId?`, `ClosedOn?`, `ClosedAtUtc?`, `ClosedBy?`, `CloseNote?`, `ExistingOutcome`. Renewal replay returns the same annual identity and its current lifecycle facts; if it was subsequently closed, replay does not reactivate it.

## Errors and status handling

Use typed `Result.Status` and `Result.Error` codes, not English-message parsing:

| Code / status | Action |
|---|---|
| `InvalidPeriod` / Invalid (400) | Review query year/month/status |
| `InvalidCloseIntent` / Invalid (400) | Review operation ID/note |
| `InvalidRenewalYear`, `InvalidRegistration`, `RequiresPayerSnapshot`, `InvalidActor` / Invalid (400) | Review explicit registration inputs |
| `RegistrationIntentConflict` / Conflict (409) | Do not retry changed intent under the same ID |
| `RegistrationClosed` / Conflict for close; Invalid for new collection quote/post | Refresh selected source; no automatic walk-up fallback |
| `RenewalAlreadyExists` / Conflict (409) | Show/manage the existing renewal |
| `RegistryConcurrencyConflict` / Conflict (409) | Retry unchanged request/ID, then refresh if another renewal won |
| NotFound (404) | Registration unavailable in this tenant; no cross-tenant lookup |
| Forbidden (403) | Role/tenant boundary; no client-side bypass |

## Monthly collection facts

Figures are derived from the existing canonical source-native activity reader, scoped by Collection business date and tenant, including recorded reversal/correction effects (`NetAmount`). No N+1 per-vendor money queries. Only frozen `VendorRegistrationId` attributes a Collection to a row. No NPM or BusinessPayor ownership/name search participates.

Vendor Fee (`FISH_MEAT_VENDOR_FEE`) and Weight & Measure (`WEIGHT_AND_MEASURE`) keep their own classifications and OR policy. `TotalCollected` is an operational convenience sum, not another revenue classification. Same-name walk-up Vendor Fee remains only `WalkUpVendorFeeCollected`, never a registered vendor's total. The photographed handwritten amounts are monthly reference totals, not rates or transaction inputs.

Closing/renewing records does not change any collection amount, monthly report income, source balance, remittance eligibility or collector position. A money correction still uses the established audited Collection correction workflow. Management totals reflect that correction without deleting the original Collection/SRC.

## Collection eligibility / frontend wiring

Source identity remains `SourceIdentityKind.FishMeatVendorRegistration=3` plus the registration ID. Current annual Active sources offer both `FISH_MEAT_VENDOR_FEE` (direct AmountReceived) and `WEIGHT_AND_MEASURE` (Kilograms × effective server rate) when assigned and policy/rate eligible. Existing `Operations[].Choices`, choice `Identity.VendorRegistrationId`, amount rule, rate ID/date and instrument contracts remain unchanged.

Closed registrations disappear from current source search and Collector registration choices. Explicit discovery of a Closed identity yields no registered-source choices. New quote/post rejects it with `RegistrationClosed`, including an old reviewed quote; a retry of already-posted money still returns its original Collection/SRC. No same-name source is substituted. Prior-year registrations cannot collect in another year's business date merely because their status is Active.

Vendor Fee walk-up remains a separate explicit no-registration request with PayerSnapshot and direct amount. Weight & Measure has no walk-up path (`RequiresRegisteredVendor`). Do not apply a frontend `OnlyOperation` restriction that hides Weight & Measure when rendering a selected registered source; consume server-confirmed eligible operations. Do not add a financial writer to fix presentation filtering.

## Import compatibility and migration

Existing `/fish-meat/import/preview` and `/save`, their typed DTOs, explicit duplicate-review confirmation and transactional replay behavior are unchanged. Import creates registry records only, no Collections/SRCs/rates/handwritten revenue totals. Import or single-registration replay of a subsequently closed record does not reactivate it. Lifecycle/renewal operations are separate typed contracts.

Additive migration: `20261007072024_FishMeatRegistryLifecycle`. Adds Status (default Active for existing rows), closure audit/operation fields, PriorRegistrationId, consistency check, unique close operation and renewal source/year indexes, and tenant-safe self-reference with restrictive delete behavior. No data update/backfill or financial-table change. Applied only by test fixtures to throwaway PostgreSQL, never production.

## Validation

Tests cover lifecycle, replay, renewal identity/concurrency, tenant/role guards, classification-separated correction-aware monthly totals, walk-up isolation, retained import behavior, and both Fish/Meat source eligibility.

- Focused domain unit tests: **5 passed, 0 failed, 0 skipped**.
- Full unit suite: **2,519 passed, 0 failed, 0 skipped**.
- Focused PostgreSQL collection-session/report-governance/remittance suite: **90 passed, 0 failed, 0 skipped**.
- Full integration after restoring the defect probe: **357 passed, 0 failed, 7 skipped, 364 total**. The seven skips require `STALLTRACK_SNAPSHOT_DB`; Docker/Testcontainers financial tests ran, including this migration on a fresh throwaway database.
- API, HttpClients and Mobile.Core Release builds: passed.
- EF pending-model check: no changes since the migration; `git diff --check`: passed.
- Defect sensitivity: temporarily summing gross Weight & Measure instead of correction-aware NetAmount made the monthly regression fail (expected 100, actual 166); restoring NetAmount returned the full suite to green. Temporary defect not committed.
- Existing repository compiler/analyzer warnings remain. No UI/component/Android/APK work or production effects.

## Exact changed files

- `EEMOCantilanSDS.Domain/Entities/Revenue/FishMeatVendorRegistration.cs`
- `EEMOCantilanSDS.Application/Dtos/Mobile/VendorRegistryManagementDtos.cs`
- `EEMOCantilanSDS.Application/Common/Revenue/OfficeCollectionWorkflow.Management.cs`
- `EEMOCantilanSDS.Application/Common/Revenue/OfficeCollectionWorkflow.cs`
- `EEMOCantilanSDS.Application/Common/Interface/ApiClients/IOfficeSourcesApiClient.cs`
- `EEMOCantilanSDS.Api/Controllers/Revenue/OfficeSourcesController.cs`
- `EEMOCantilanSDS.HttpClients/ApiClients/OfficeSourcesApiClient.cs`
- `EEMOCantilanSDS.Infrastructure/Repositories/Revenue/CollectionSessionSources.Native.cs`
- `EEMOCantilanSDS.Infrastructure/Persistence/Configuration/FishMeatVendorRegistrationConfiguration.cs`
- `EEMOCantilanSDS.Infrastructure/Migrations/20261007072024_FishMeatRegistryLifecycle.cs`
- `EEMOCantilanSDS.Infrastructure/Migrations/20261007072024_FishMeatRegistryLifecycle.Designer.cs`
- `EEMOCantilanSDS.Infrastructure/Migrations/AppDbContextModelSnapshot.cs`
- `EEMOCantilanSDS.Testing/Architecture/ApplicationEfBoundaryTests.cs`
- `EEMOCantilanSDS.Testing/Domain/FishMeatRegistryLifecycleTests.cs`
- `EEMOCantilanSDS.IntegrationTests/CollectionSessionTests.RegistryManagement.cs`
- This handoff document.

Frontend wiring remains Claude's lane. New management semantics are available through the typed DTO/client contracts above; no frontend styling or financial logic was changed here. NO production deploy, production data mutation, push, master merge or APK publication.
