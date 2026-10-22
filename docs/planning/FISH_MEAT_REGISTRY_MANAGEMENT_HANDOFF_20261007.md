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

## 2026-10-19 Collector registration-request backend

### Authority and runtime boundary

Owner-requested prospective revision to IA-068; **Office confirmation is still required before real deployment**. The confirmed 2026-10-06 walk-up Vendor Fee rule is not retrospectively withdrawn. No existing tenant is opted in. `FishMeatRegistrationOptions` defaults to `RequestsInstalled=false` and an empty tenant/effective-business-date map; DI registers those defaults without configuration binding. Source registration remains independent of NPM and a Business Payor master.

The new nonfinancial table is a **separate schema candidate**, explicitly ignored by the active `AppDbContext`. Its configuration, generated [review DDL](../evidence/fish-meat-registration-requests/request-schema.sql) and [promotion/backup patch](../evidence/fish-meat-registration-requests/activation.patch) remain outside active migrations. The schema test verifies that its only changes are one table and seven indexes, with four restrictive tenant-composite FKs. No migration/snapshot was promoted or installed. EF's pending-model-change guard is preserved. `RequestsInstalled=true` alone is insufficient: the workflow also requires the mapped request entity, so accidental flag changes against the old model fail closed before a request-table query.

### Contract for Claude: separate registration queue

Base route: `/api/office-sources/fish-meat/registration-requests`. Existing authenticated transport must be used; do not add these to anonymous authentication clients.

| Method / suffix | Role and operation | Contract |
|---|---|---|
| GET `?page=1&pageSize=25&status=1` | Head/Admin office queue; assigned Collector sees only own requests | `FishMeatRegistrationRequestPage`; bounded 1–100 page size; omit status for all history |
| GET `/{id}` | Same roles; tenant and Collector ownership checked | `FishMeatRegistrationRequestDto` or NotFound |
| POST base | Active Collector assigned Vendor Fee or Weight & Measure | `SubmitFishMeatRegistrationRequest` |
| POST `/{id}/cancel` | Submitting Collector only | `CancelFishMeatRegistrationRequest` |
| POST `/{id}/review` | SuperAdmin/Head only | `ReviewFishMeatRegistrationRequest`; approve existing verified registration, return or reject |
| POST `/{id}/complete` | SuperAdmin/Head only | `CompleteFishMeatRegistrationRequest`; existing official registration fields plus request review, in one transaction |

Submission fields: `ClientOperationId`, `VendorName` (required, max 200), `VendorType` (Fish=1, Meat=2), current `TaxYear`, optional `Contact` (200), `Note` (1000), `ReplacesRequestId`. These are proposed facts, not official registration evidence. The submitter/municipality are derived from authenticated context, never body claims. A current-year check occurs for new submissions after replay lookup; successful old requests can still be retrieved/replayed after a year transition.

States: Submitted=1, Approved=2, Returned=3, Rejected=4, Cancelled=5. Submitted version=1; a terminal decision version=2. Return/reject/cancel never links a registration. Returned submissions remain intact; resubmit with a new operation ID and the returned request ID. A returned request has at most one successor; subsequent returns form an explicit chain. DTOs retain submitter/Collector identity, timestamps, version, registration ID, reviewer user ID/name, reason and predecessor. Decisions are terminal; use a successor rather than overwriting evidence. The existing AuditSaveChangesInterceptor also audits request creation/decisions.

Review body: `ClientOperationId`, `ExpectedVersion`, `Outcome`, required `Reason`, optional `RegistrationId` (required for Approved and prohibited otherwise). Approval looks up the explicit same-tenant active registration and verifies current tax year and proposed type. Equal names never establish identity. Corrected names are permitted in the actual office record; a type/year mismatch must be returned for correction rather than silently reinterpreted.

Completion body: `ClientOperationId`, `ExpectedVersion`, required `Reason`, `Registration` using **the existing `RegisterFishMeatVendorRequest`** (name/business, Fish/Meat, New/Renew, year, address, personal-name fields, sex, nature of business and reference). No NPM association is accepted here. Head reviews those fields; the server never invents them. The nested registration operation ID is replaced server-side with the request ID. Keep the entire completion body frozen on retry: the decision fingerprints the submitted intent, including its nested fields. The existing registration writer and the Approved decision commit in one serializable transaction. Invalid evidence, stale/concurrent review or registration failure leaves no new registration behind. A second click with a new decision ID cannot create a second vendor for a completed request.

Submission replay: same tenant/Collector/operation ID and exact intent returns the stored request; conflicting intent is Conflict. Pending duplicate protection hashes normalized **submission evidence**, including contact/note, within one Collector/year/type; it neither identifies a person nor merges names across Collectors. Same-name proposals with distinct evidence remain separate and require manual office review. Identical proposed evidence must be clarified rather than linked automatically. Review replay binds actor, operation, version and decision intent. Concurrent submissions/decisions use serializable transactions, unique indexes and version tokens; a conflict requires refresh/retry of the original stable operation, not a fresh intent.

An unavailable schema returns `VendorRequestSchemaNotEnabled` before mutation. Invalid evidence maps to Invalid; unauthorized calls to Forbidden; inaccessible IDs to NotFound; stale/conflicting operations to Conflict. Follow the repository's standard Result/HTTP response handling.

### Prospective financial eligibility and offline integration

`OfficeCollectionWorkflow.ResolveAsync` remains the single source-native quote/post rule holder. When a tenant's activation date covers the business date, Vendor Fee requires an explicit active same-tenant registration for both the transaction year and current tax year. Missing/nonexistent/closed/wrong-year IDs cannot be replaced with payer text. The amount is still **actual amount received**, with the existing money validation and OR classification; there is no fixed Vendor Fee rate. Weight & Measure retains its separate registered-vendor/quantity/effective-rate rules.

`CollectionSessionSources` passes the same policy options to the shared Office workflow. Native discovery omits the walk-up capability under the new policy. Session quote/revalidation/post and itemized offline sync therefore use the same authoritative checks. Unsynced old-policy intent is revalidated when posted. Already posted operations and recorded sessions are replayed from their durable evidence before new eligibility is applied: closed/expired registrations or a later policy date cannot rewrite an original SRC or duplicate cash. Business dates before the accepted activation date retain the old policy; do not remove accepted dates as a casual feature rollback. Existing legacy Fish/Meat endpoints already refuse new source-era collection; historical replay paths stay intact. There is no new online/cash-intake writer.

Claude should queue the submission endpoint with one stable operation ID and frozen request body, then poll the own-request list/detail. Do not display a submitted proposal as a collectible identity. After Approved, refresh the existing registered-source discovery and collect against the returned `RegistrationId`. Offline submission is idempotent at this endpoint; a new Mobile queue/UI operation is deliberately left to Claude. Collection-session offline operations continue through the existing sync route; local payer text or amounts are never identity authority.

For unified Collection & Registration Review, compose **three distinct queues**: existing Direct Collection Review (money already posted), this request queue (identity proposal, no money), and existing NPM requests (nonfinancial tenancy review). Keep existing Fish/Meat payer follow-up linking for old walk-up Collections. None of these approvals creates Collections, SRCs, ORs, rent, remittance or NPM associations.

### Installation and acceptance gates

1. Review this policy revision with the Office; record the accepted tenant and effective business date. No supplied tenant/date defaults exist.
2. Separately authorize installation. Apply the reviewed promotion/backup patch in source, generate an additive EF migration/designer/snapshot for exactly the candidate table/indexes, verify no unrelated model drift, and review its SQL against `request-schema.sql`. Do not install the SQL directly as a substitute for controlled EF bookkeeping.
3. Complete and test tenant export/restore coverage with the promotion patch. The current active model/export is unchanged. An unexpectedly installed unmapped table is detected as unmanaged and keeps scoped restore fail-closed. After promotion, restore-v2 requires the new table manifest (`[]` is different from absent); old incomplete snapshots must not delete request/audit evidence. Retained historical table safeguards still apply.
4. Approved schema installation, backup verification and startup controls precede enabling `RequestsInstalled`. The Head/Mobile interfaces, authenticated client registration and Mobile queue wiring are Claude's next integration work.
5. Verify all UI/request/collection paths in a synthetic feature-on host, then separately approve the tenant/effective-date policy map. Runtime defaults stay off; this task changes no real tenant or municipal database.

### Scoped files and verification checkpoint

New source: `Domain/Entities/Revenue/FishMeatRegistrationRequest.cs`; `Application/Dtos/Mobile/FishMeatRegistrationRequestDtos.cs`; `Application/Common/Revenue/FishMeatRegistrationOptions.cs`; `Application/Common/Revenue/OfficeCollectionWorkflow.FishMeatRequests.cs`; `Api/Controllers/Revenue/FishMeatRegistrationRequestsController.cs`; `Infrastructure/Persistence/Configuration/FishMeatRegistrationRequestConfiguration.cs` (project prefixes are `EEMOCantilanSDS.`).

Extended source: `OfficeCollectionWorkflow.cs` (shared eligibility); `CollectionSessionSources.cs` and `.Native.cs` (same policy propagation); `IAppDbContext.cs`/`AppDbContext.cs` (candidate persistence surface, ignored active entity); Application `DependencyInjection.cs` (default-off options); `AuditSaveChangesInterceptor.cs` (nonfinancial audit type). No frontend, existing NPM request record, posted-review workflow or historical linking workflow was changed.

Tests: `CollectionSessionTests.FishMeatRequests.cs`, `Domain/FishMeatRegistrationRequestTests.cs`, `Infrastructure/Persistence/FishMeatRegistrationRequestSchemaTests.cs`; intentional addition to `Architecture/ApplicationEfBoundaryTests.cs` permits the new partial in the existing Office transaction seam. Evidence: candidate DDL plus offline promotion patch. Canonical documentation: prospective IA-068 annotation and Revenue Architecture boundary note; this existing handoff is the frontend integration contract.

Initial checkpoint: branch `codex/local-unified-v8-v10-20261013`, HEAD `17b9d100`; `git status --short` counted 416 entries, while the full `--porcelain -uall` inventory recorded **457 individual pending paths**, none staged. Other frontend changes appeared during work; no other agent is visible through this thread's agent inventory. All existing work remains uncommitted; no reset/stash/worktree, municipal DB access, migration installation, API restart, deployment or push was performed.

Executed verification and final counts are recorded below after the final regression pass. Initial checks: 14 focused unit/domain/schema/architecture/backup checks passed; six focused real PostgreSQL request/policy cases passed; full unit suite passed 2,983/2,983. One initial build failed on a shared intermediate DLL lock; subsequent runs use isolated build outputs. One initial PostgreSQL attempt failed before test execution because the candidate was initially in the active model; the explicit ignored-model boundary corrected that startup compatibility issue without suppressing EF warnings. No failed/skipped run is counted as a successful financial test.

#### Final executed verification

| Check | Actual result |
|---|---|
| Full unit suite, final run | **2,983 passed / 0 failed / 0 skipped** |
| PostgreSQL CollectionSessionTests + FishMeatPayorFollowUpTests + DirectCollectionReviewTests | **129 passed / 0 failed / 0 skipped** |
| Final focused request/registration PostgreSQL run, including latest SRC-sequence and cross-tenant assertions | **11 passed / 0 failed / 0 skipped** |
| Eligibility regression counterfactual | Temporarily bypassing the new resolver gate produced **1 expected failure**: expected `RequiresRegisteredVendor`, actual `null`. Source restored, rebuilt and the focused 11-case run passed. No gate bypass remains. |
| Offline schema artifact | Generated DDL matches the checked-in candidate; one table, seven indexes, four tenant-composite restrictive FKs; active model still excludes the table |
| Promotion patch | `git apply --check --unidiff-zero` passed; patch **not applied** |
| Backend build | Final API Release build **passed, 0 warnings / 0 errors** (incremental; earlier full compilation reported 22 pre-existing warnings) |

The first broad PostgreSQL run had 126 passed / 1 failed: a real Npgsql serialization exception was wrapped in `InvalidOperationException` during concurrent submission. Submission now recognizes the nested `40001` / `40P01` concurrency failures and returns Conflict without automatic mutation retry. The concurrency case is executed with three repetitions, and the subsequent broad/focused runs passed. Terminal completion and review remain serializable and version guarded. The request lifecycle test verifies zero Collections, allocations, posting operations, accountable documents, remittances or NPM associations and identical SRC sequence `last_value`/`is_called` before and after. Completion failure after registration SaveChanges proves the outer transaction removes the attempted new registration.

Reproducible commands (PowerShell; temporary build outputs avoid the running preview's DLL locks):

```powershell
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj -c Release --no-restore -p:OutputPath=bin/FishMeatRequests/net9.0/ -p:IntermediateOutputPath=obj/FishMeatRequests/
dotnet test EEMOCantilanSDS.IntegrationTests/EEMOCantilanSDS.IntegrationTests.csproj -c Release --artifacts-path "$env:TEMP\fishmeat-request-verification" --filter 'FullyQualifiedName~CollectionSessionTests|FullyQualifiedName~FishMeatPayorFollowUpTests|FullyQualifiedName~DirectCollectionReviewTests'
dotnet test EEMOCantilanSDS.IntegrationTests/EEMOCantilanSDS.IntegrationTests.csproj -c Release --no-restore --artifacts-path "$env:TEMP\fishmeat-request-verification" --filter 'FullyQualifiedName~Vendor_request|FullyQualifiedName~Vendor_registration'
dotnet build EEMOCantilanSDS.Api/EEMOCantilanSDS.Api.csproj -c Release --no-restore --artifacts-path "$env:TEMP\fishmeat-request-verification"
git apply --check --unidiff-zero docs/evidence/fish-meat-registration-requests/activation.patch
```

Docker Desktop Linux 29.5.3 was available. PostgreSQL verification used only the existing Testcontainers fixture: disposable `stalltrack_tests`, dynamically mapped **non-5432** port. No municipal connection was inspected or used, and no unrelated container was stopped. The schema installer in the new tests checks that isolated identity before DDL. Source/tenant/role checks, invalid/closed/expired registrations, rejection/return/cancel, decision and submission races, exact direct amounts, offline stale-eligibility rejection, recorded-session replay, same-name distinct evidence, Head-only API authorization and independent legacy follow-up/NPM request behavior are covered. These are backend tests; no Mobile/Head UI, physical device, real LGU activation or full unrelated integration suite is claimed.

At final inventory verification: **474 individually enumerated pending paths**, **0 staged**, all **457** initially recorded pending paths still present in Git status, HEAD/branch unchanged. Concurrent frontend work also changed the checkout during this task; the inventory count is a checkpoint, not ownership of those files. No commit was made. Only the backend/contract/tests/evidence/documentation paths listed above belong to this task. No frontend changes were made by this workstream.

## 2026-10-19 Frontend integration: registration requests (Claude 1.1)

No backend, schema, migration, option or tenant policy was changed. Everything below uses the contracts above exactly; `RequestsInstalled=false` and the empty tenant/date map are untouched.

**Typed clients.** `IMobileApiClient` (+ `MobileApiClient`, `CachingMobileApiClient` pass-through, never cached): `GetFishMeatRequestsAsync`, `SubmitFishMeatRequestAsync`, `CancelFishMeatRequestAsync`. `IOfficeSourcesApiClient`: `FishMeatRequestsAsync(page, pageSize, status)`, `ReviewFishMeatRequestAsync`, `CompleteFishMeatRequestAsync`. All default to "unavailable" in the interface so fakes are unaffected; they ride the existing authenticated clients.

**Collector Mobile.** On `/vendor-fee` and `/weight-measure`, when the vendor search returns nothing, a separate **Request registration** row opens `FishMeatRegistrationRequestForm` (vendor name prefilled from the search, Fish/Meat, optional contact and note; tax year = current PH year). The body is frozen with one operation id for the life of the attempt (retry replays it; "Change details" mints a new identity; a definitive refusal clears it). It is online-only like the NPM request: there is no offline queue entry because the request has no money and the server answers replays by operation id. `/fish-meat-requests` (also "My registration requests" under Other collections) lists own requests with status, office reason, **Cancel** (one stable cancel id per request, `ExpectedVersion` from the row) and **Correct and resubmit** for Returned requests (`ReplacesRequestId`, hidden once a successor exists). An approved request offers **Find vendor**, which returns to the vendor search; the registered vendor appears there from the server, and the proposed name is never a source. "Search again" after submitting re-reads the source list.

**Head Portal.** `/monitoring/review` (**Collection & Registration Review**) separates three areas: Collection verification (hosts the existing `DirectCollectionReview` unchanged: notes, corrections, historical follow-up), Fish / Meat registration (`/monitoring/fish-meat-requests`), NPM registration (existing `/monitoring/npm-requests`, unchanged). `MonitoringTabs` gained Review and Fish / Meat requests. The Fish / Meat queue filters by status, shows request facts, and for the Head (`SuperAdmin`) offers: **Register vendor** (the existing `VendorRegistrationDrawer` with the office's own fields, submitted through `/complete`; one atomic registration + approval), **Use existing** (a registered vendor of the same year and type chosen by id: `/review` Approved with `RegistrationId`), **Return** and **Reject** (reason required). Admin sees the queue and details only. Decision ids follow the Head registration page pattern: unchanged intent keeps its id, any edit gets a new one; Conflict refreshes the row. No decision path approves a typed name, and none creates a Collection, SRC, OR, rent, remittance or NPM link. Financial confirmation in Direct Collection Review stays separate.

**Not installed / not run.** The request table is still not installed, so against the working-copy API the queue answers `VendorRequestSchemaNotEnabled` (shown as "not enabled for this office yet"); the endpoint is reachable (401 unauthenticated). End-to-end acceptance needs the gates in section 2026-10-19 step 2-5: apply `docs/evidence/fish-meat-registration-requests/activation.patch`, generate the additive EF migration for exactly the candidate table and seven indexes, review its SQL against `request-schema.sql`, complete export/restore coverage, then set `RequestsInstalled=true` plus a tenant/effective-date entry **only on an explicitly authorized disposable database** (never the retained municipal database or the working-copy tenant without approval). No component tests were written or run; Head, Mobile Core and Client builds pass.

## 2026-10-19 Separate synthetic acceptance environment prepared

**Status: prepared and API/restore verified; interactive Mobile/Head acceptance is OPEN.** This section supersedes "not installed" only for the new disposable environment below. The primary checkout and its existing accepted previews still have the original default-off policy and ignored request entity.

### Isolation and review

- Source snapshot: `C:\dev\stalltrack-v2-previews\fish-meat-requests-20261019` (3,447 copied source/assets files, including Claude 1.1 integration). This is not a new Git worktree. No existing checkout or preview was reset, merged, restarted or repointed.
- Dedicated Docker container: `stalltrack-fishmeat-requests-20261019`, label `stalltrack.scope=fishmeat-synthetic-acceptance`, PostgreSQL 16 on **127.0.0.1:55495** only; database `stalltrack_fishmeat_requests_acceptance`. Existing containers on 55491/55493 and the retained Cantilan database were untouched. No municipal snapshot/data was copied.
- `activation.patch` reviewed and applied **only in the snapshot**. It promotes the request mapping and adds tenant backup/export coverage. Claude's request-page Razor/CSS and Mobile request form were copied without edits; final hashes match current primary source.
- Generated EF migration: `20261019100000_AddFishMeatRegistrationRequestsAcceptance`, designer and updated model snapshot, all under the isolated Infrastructure/Migrations directory. Review confirms exactly **one table + seven indexes**, four restrictive tenant-composite FKs, no unrelated operations, no model/snapshot drift, and generated DDL equal to the existing `request-schema.sql`.
- EF initially generated `20261019052921`, which sorted before the existing manually dated `20261019090000_AddSlaughterChargeScope20261019`. The **new, unapplied isolated migration metadata/files** were renamed to 20261019100000 before installation. No existing migration/history entry was edited. The final migration sorts last.
- Applied the current 134-migration baseline plus the approved request migration to this **empty disposable database**. Independently verified from a fresh read-only connection: **135 applied migrations**, request table initially empty, Collections initially empty. This is a current-line fresh baseline, not the retained municipal 143/144 historical baseline.
- API startup migrations/seeders remain **disabled**. The isolated bootstrap refuses any database except the exact loopback name/port above, clears shared/user-secret configuration, and removes the scheduled backup worker. Payment-provider URL is an `.invalid` test target. NPM financial runtime was not activated.

### Restore/export acceptance before enabling storage

The promotion patch makes the request table part of the server-authoritative restorable manifest and logical export. An isolated harness exercised the real repositories against PostgreSQL before starting the feature-on API:

- Rejected restore-v1, wrong-tenant, missing request-table manifest, `null` table and malformed object table: **five refusals, zero DELETEs and zero accepted-restore audit events**.
- Explicit request-table `[]` accepted, then a Returned-parent/Submitted-successor chain restored with original IDs and relationship. Existing credential records stayed outside restore.
- Audit history remained append-only; SRC sequence `last_value` and `is_called` remained unchanged. No Collections, accountable financial postings or remittances were created by request preparation/restore.
- A transactional synthetic unmanaged-table probe was detected and blocked scoped restore; its DDL was rolled back. The retained-schema safeguard was not weakened or bypassed. This new database does not contain the thirteen retained historical municipal tables, and it is not evidence that scoped restore is safe for the retained municipal baseline.
- Focused existing `TenantBackupCoverageTests` and `TenantRestoreV2SafetyTests`: **51 passed / 0 failed / 0 skipped** on the promoted isolated model.

### Enabled tenant and real authenticated API checks

Only **Synthetic Fish Meat Acceptance**, code `SYNFM`, tenant code `synthetic-fm`, ID `e0118d81-e954-4a44-9d3f-a0793b428427`, is enabled; effective business date **2026-10-19**. The isolated host replaces the default options with a request-scoped provider that sets `RequestsInstalled=true` and the effective-date map **only when the authenticated tenant matches this ID**. Synthetic control tenant `9914f18d-3c80-48c0-8b14-035be0c2fe65` remains request-storage-off and registration-policy-off. Nothing was enabled in a real LGU or in the primary source defaults.

Actual HTTP authentication and production controllers/workflows verified:

- New typed/unregistered Vendor Fee posting returns `RequiresRegisteredVendor`, creating no SRC.
- Collector request submission/replay uses one identity; Collector and ordinary Admin cannot review.
- Head `/complete` and replay yield one verified registration and **no Collection**.
- Quote/post against that registration receives the entered **35**, with one SRC on replay.
- Closing that synthetic registration blocks a new collection but preserves replay of its existing SRC.
- The synthetic old-policy walk-up receipt also replays without changing its SRC.
- Control tenant unregistered quote still follows old policy, and its request endpoint returns `VendorRequestSchemaNotEnabled`.

The fixture currently starts with **two synthetic collections totaling 60**: old-policy 25 / `SRC-2026-000001`, and verified Vendor Fee 35 / `SRC-2026-000002`. These are disposable acceptance cash evidence, not municipal financial records. Active Fish/Meat vendors, a closed vendor, a prior-year vendor, a Submitted request, a Returned request and a Rejected request are available for UI testing. The proof vendor is intentionally closed after its replay test.

### Exact preview access and accounts

| Surface | URL / launch |
|---|---|
| Head sign-in | `http://127.0.0.2:7587/login?lgu=synthetic-fm` |
| Fish/Meat requests | `http://127.0.0.2:7587/monitoring/fish-meat-requests` |
| Collection & Registration Review | `http://127.0.0.2:7587/monitoring/review` |
| API health / Swagger | `http://127.0.0.1:5137/health` / `http://127.0.0.1:5137/swagger` |
| Separate Windows Collector | Run `C:\dev\stalltrack-v2-previews\fish-meat-requests-20261019\.acceptance\Open-Collector.ps1` |

Synthetic accounts: **fm.head** (SuperAdmin), **fm.collector** (Collector), **fm.admin** (read-only review), all for `synthetic-fm`. Password: **FM!Preview2026-Only** — fixture-only, never reuse outside this environment. Control Collector: `fm.control` / same fixture password, municipality `synthetic-fm-control`.

Windows executable: `EEMOCantilanSDS.Mobile\bin\Debug\net10.0-windows10.0.19041.0\win-x64\StallTrack.FishMeatAcceptance.exe` under the snapshot. Its title, ApplicationId and assembly identity are separate; only its copied Debug bootstrap points to API 5137. Original `PackageId` is retained so the existing scoped-CSS link resolves. The primary MAUI app and its normal endpoint were not modified. No APK was produced or published.

Head uses the separate 127.0.0.2 loopback host, `.StallTrack.FishMeatAcceptance` auth cookie, separate Data Protection application/key directory and API 5137. Existing preview auth cookies are not replaced. Authenticated **HTTP** sign-in/queue rendering both returned 200, including the pending vendor and Register action; the scoped CSS bundle also returned 200. This is not browser-interaction acceptance.

### Manual acceptance sequence and limits

1. Open the separate Windows app and sign in as `fm.collector`; choose **Synthetic Fish Meat Acceptance** if prompted. In Vendor Fee search `Acceptance New UI Vendor` → Request registration → enter synthetic evidence → submit → My requests. Retry must not duplicate it, and no SRC exists yet.
2. In the new Head preview, review that request → Register vendor → complete the existing official fields → submit. Verify one Approved request and one registered identity. `Acceptance Pending Vendor` is also available for review without first submitting.
3. In Collector, Find vendor / search again → choose verified identity → enter synthetic received amount → review and record. Verify one SRC and stable replay; no typed-name fallback may collect.
4. Confirm the receipt appears separately under Collection verification. Use the Returned fixture for correction/resubmission and the pending fixture for cancellation/return/reject tests. `fm.admin` may read only. Closed/prior-year vendors must not gain new eligibility; Weight & Measure remains separate.

**No interactive UI acceptance is claimed.** Computer-use inventory reported no available browser; attempting the in-app browser returned `Browser is not available: iab`. Windows Debug was built but its screens were not driven or visually verified. Only the authenticated HTTP/render and API paths above were exercised. Finish Mobile and Head interaction, drawer, retry and collection checks before signing off end-to-end acceptance.

Builds: API Release **passed, 4 warnings / 0 errors**; Head Release **passed, 85 warnings / 0 errors**; isolated Windows Debug **passed, 15 warnings / 0 errors**. Existing warnings remain. An initial Mobile command-line TargetFrameworks override leaked to referenced project restore targets; corrected by setting Windows-only targeting in the isolated MAUI project instead. No dependency-target changes were made in the primary checkout.

Local evidence and quick-start: `.acceptance/README.md`, `schema-review.json`, `installation.json`, `restore-verification.json`, `api-verification.json`, logs and authenticated Head HTML in the snapshot. Generated EF SQL is `request-migration.sql`; EF migration/designer/snapshot are isolated source only. A full synthetic backup `acceptance-after-api.dump` was created and successfully listed with `pg_restore -l`; SHA256 **92CE06132627C3BFD7C3E08BD9D80ADA6CE161F2A0D138832CA65A1320C9AE2C**. Credentials/runtime secrets and dump remain outside Git. API/Head PIDs are recorded locally in `api.pid` / `head.pid`.

Primary checkout remains branch `codex/local-unified-v8-v10-20261013`, HEAD `17b9d100`. Initial full inventory contained 502 individual pending paths; all remained pending at verification while other concurrent work continued. This task changes only this handoff in the primary checkout; schema promotion, host bootstrap changes, seeding and policy activation occurred in the separate disposable snapshot/database. No commit/push, retained database access, accepted-preview restart or production deployment occurred. Real-LGU Office confirmation and rollout approvals remain separate from synthetic acceptance.

## 2026-10-20 Working-copy registration-request activation (authorized)

The Head authorized installation and activation **only** for the isolated local working-copy database on 127.0.0.1:55491, database stalltrack_workcopy_main, Cantilan tenant 0ea98ba6-f7b1-4928-96f1-6177d45741bd. Production AWS and the original database on port 5432 were not accessed. Earlier candidate-phase sections above are historical; this update supersedes their pending-installation state for the working copy only.

- Additive EF migration **20261020133853_AddFishMeatRegistrationRequestsWorkingCopy20261020** creates only FishMeatRegistrationRequests (23 columns, 4 tenant-scoped restrictive foreign keys, 3 check constraints and 7 secondary indexes), plus its EF migration-history marker. No existing financial tables are changed.
- The mapped model, tenant backup/restore coverage, logical export (including old-schema warning), schema unit test and PostgreSQL test fixture were promoted together. The request table remains protected by the tenant query filter.
- Activation requires Development mode, the exact loopback database/port, a named Cantilan tenant and the installed EF migration. The working-copy launcher sets the activation variables on next local restart. Without the named working-copy configuration the runtime still defaults to off.
- No **registration-required cash-collection policy** was activated. FishMeatRegistrationOptions.RegistrationRequiredFrom stays empty. The request feature is nonfinancial; existing collection workflows/SRCs are unchanged.
- A verified custom-format backup was created at %TEMP%\stalltrack-workcopy-main-20261018\stalltrack-fish-meat-requests-pre-install-20261020-213556.backup. It was restored to temporary scratch PostgreSQL, where the exact one-table migration succeeded before the identical script was installed into the working copy.
- Focused validation: 11 unit/schema/tenant-backup tests, 8 PostgreSQL request/API checks and 3 PostgreSQL policy/replay checks all passed. API Release publish succeeded, isolated canary startup succeeded, health/ready probes returned 200 and anonymous request access correctly returned 401.
- API-only working-copy cutover succeeded on https://localhost:7197 and http://localhost:5117; Portal at https://localhost:7267 kept its original Client PID. Startup confirms the previous approved TEST NPM monthly policy and the new TEST-only Fish/Meat request queue. The previous API binary remains under %TEMP%\stalltrack-workcopy-main-20261018\publish-api-main-backup-fmrequest-20261020-220516.
- Scratch and working-copy counts matched after migration: 42 Collections, 5 vendor registrations and 7 NPM rent months; **zero registration requests** exist yet. The Pending queue should honestly display no requests until a Collector actually submits one.
- Historical posted walk-up collections remain under Review payor, not Registration Review. Existing office vendor registrations are not themselves requests. Live end-to-end Head/Collector browser acceptance with authenticated sessions remains unverified; the PostgreSQL/API regression checks passed. No commit or push was made.
### Final live tenant-isolation cutover (2026-10-20)

A final review found that the isolated working-copy PostgreSQL contains other municipality identities as well as Cantilan. The API was hardened beyond merely verifying the database/tenant settings at startup. FishMeatRegistrationOptions now supplies a named RequestTenants allow-list, and every OfficeCollectionWorkflow.FishMeatRequests request entry point checks RequestsAvailable(Tenant) before it can read or write request records. The approved working-copy activation populates **Cantilan only**. Other municipalities in the same working-copy database remain disabled for this queue, even though the additive table exists globally. The mandatory-registration payment policy remains disabled.

After this correction the API was rebuilt successfully in Release, and **12 focused unit/schema/backup tests passed (0 failed)**, including the newly added tenant allow-list test. The guarded API-only cutover completed successfully and preserved the existing Head Portal process (PID 22312). Live API PID **13332**, health 200, readiness 200, Head login page 200, unauthenticated request endpoint 401. Main API location remains https://localhost:7197; Head remains https://localhost:7267. The previous compiled API was preserved in %TEMP%\stalltrack-workcopy-main-20261018\publish-api-main-backup-fmrequest-20261020-221540. The temporary verification canary is stopped; scratch database was dropped after successful comparison.

A full authenticated Collector-submit -> Head-review -> register/return browser walkthrough has not been executed on the real working-copy Portal. Tests verify the underlying PostgreSQL request workflow and API permissions; a human log-in and real UI submission are still the recommended final demonstration smoke test. No commit/push or production change.
Final tenant-scoped PostgreSQL regression rerun after deployment: 11 passed, 0 failed, 0 skipped (exit code 0); the 12 tenant-guard unit tests passed. The deployed API and Head Portal health checks remain HTTP 200. Authenticated manual UI acceptance has not been performed.

## 2026-10-22 Registry integrity, request availability and logical deletion (isolated source candidate)

**Not deployed or enabled in a user database.** Worktree `C:\dev\stalltrack\eemo-fish-meat-integrity-20261022`, branch `fix/fish-meat-integrity-20261022`, based on fetched `origin/master` **e6bd88ce2643bcbd4b58df058290002d415575c0**. The dirty primary checkout remains on `17b9d100`; none of its files was edited or copied into this worktree. No commit, push, merge, production query, migration, data correction or service restart was performed. Older running-version claims above are historical.

### Findings and implemented boundaries

- **Confirmed import defect:** `OfficeCollectionWorkflow.Import.cs` omitted LastName, FirstName, MiddleName, NameExtension, Sex and NatureOfBusiness in both domain-factory calls. Preview/save now forward all six and return their domain-normalized values. The existing full DTO comparison now detects conflicting retries of those fields. No monetary CSV column is consumed, and no name establishes registration identity.
- Both supplied Downloads files were inspected read-only: 10 rows each, the exact 12-column header. Fish has 10 populated last/first/middle names, sex and business activity; Meat has 10 populated last/first names and activities, with blank middle name/sex. Both have blank address/extension/reference. Blanks stay null. No actual CSV rows were imported into a user database; PostgreSQL tests use synthetic representative records.
- **Confirmed diagnostic defect:** Mobile previously inferred “request registration” solely from an empty LinkedSources list. That list is also empty for missing NPM assignment, withdrawn links and ineligible occupancies. `CollectionSessionDiscovery.NpmLinkStatus` now distinguishes Available, NotLinked, AwaitingVerification, NotEligibleForDate, NotAuthorized and Withdrawn. The request form is offered for an explicitly NotLinked response; other cases show the specific office-review reason. An older API without this field requires refresh/reconciliation, never a guessed registration request.
- Existing exact vendor/contract/stall linkage, answering occupancy/date and Collector NPM assignment remain authoritative. The linked-occupancy check additionally requires an active current-year vendor so a closed vendor checkout cannot submit linked NPM items. The stall's independent NPM session remains available under its own authorization. No name-matching link is introduced.
- **Imelda case remains a data/runtime investigation, not a proven missing-link finding.** No authorized production database/API or installed-APK inspection was performed. Office review still needs exact tenant/annual registration ID, lifecycle/type/year, stall/contract ID, answering term/date, confirmation/correction evidence, Collector assignment and API/APK versions. A recorded rent payment or matching names alone proves none of those associations.
- Holder List and Office Register already read `ManageRegistrationsAsync` and canonical office fields. The main registry now shows vendor/business, type, New/Renew plus year, recorded address, activity, unchanged canonical monthly collection amount, and a Details link into the existing Manage drawer. No new details component or financial calculation. Page scrolling replaces its capped inner vertical scroll; narrow content uses a scoped container-query layout. Printable lists/CSV columns are unchanged.

### Normal nonfinancial request activation

The existing migration **20261020133853_AddFishMeatRegistrationRequestsWorkingCopy20261020** is already part of the normal mapped EF model and migration assembly on this baseline. Its historical name is retained; it is neither renamed nor duplicated. Testcontainers apply the official migration chain.

`FishMeatRequestActivation.cs` provides a normal-environment configuration path, default off:

```json
{
  "StallTrack": {
    "FishMeatRequests": {
      "Enabled": true,
      "MunicipalityIds": ["<explicit-reviewed-municipality-guid>"]
    }
  }
}
```

This is an operator example, not a committed enabled configuration. Empty, duplicate or zero GUID allow-lists are rejected. Simultaneous normal and WorkingCopy activation is rejected. The existing exact working-copy guard is retained. `IFishMeatRequestSchema` / `FishMeatRequestSchema` checks the original migration marker and a tenant-filtered read of the mapped request columns before workflow reads/writes. Absent schema/columns or denied table access returns the existing `VendorRequestSchemaNotEnabled` diagnostic. Unrelated database failures are not hidden as empty queues. Availability never populates `RegistrationRequiredFrom`; direct fee collection policy is unchanged.

Existing submit/cancel, versioned return/reject/approve, durable retry identity and one-registration completion remain the implementation. Authenticated TestServer/controller tests exercise normal allow-list activation, Collector submission, Head completion and Collector status refresh against real PostgreSQL. No payment/document/obligation is generated by requests.

Before any real activation, the operator must review the target migration history/schema, back up that environment, apply only missing reviewed migrations through the established release path, verify exact tenant GUIDs and separately authorize the configuration. This work does not claim production schema inspection or enablement.

### Close versus Delete contract

- Close remains irreversible for an annual registration and retains its NPM association. It does not change the stall/contract or cash.
- New **POST `/api/office-sources/fish-meat/registrations/{id}/delete`**, Head (`SuperAdmin`) only, accepts `{ clientOperationId, reason }`. Registration must already be Closed; nonblank reason up to 500 characters is required. Matching retry returns the existing outcome; conflicting operation intent or concurrent mutation returns conflict.
- `OfficeCollectionWorkflow.Deletion.cs` atomically marks Status=Deleted and corrects every unwithdrawn association using the established `FishMeatNpmAssociation.Correct` evidence. No physical deletion or occupancy/financial mutation. Deletion timestamp, actor, reason and operation identity are persisted. Registry, link and NPM-request mutations are covered by `AuditSaveChangesInterceptor`.
- Pending NPM requests block deletion with **`ReviewPendingNpmRequestsBeforeDelete`** until the Head explicitly returns/rejects/resolves them. A Submitted Fish/Meat request cannot reference a completed registration under its existing check constraint. Already Approved request references and annual renewal chains remain historical evidence, never deleted or rewritten.
- Ordinary registry/Holder List/Office Register/Manage reads exclude Deleted. Collector discovery requires Active. Historical source/activity/export queries retain the underlying row (no global deletion filter), so original collection attribution and retries remain valid. Head-only historical API read: `GET /api/office-sources/fish-meat/manage?taxYear=<year>&month=<month>&status=Deleted`; Admin cannot request this scope. Existing NPM link-history endpoint and Audit Trail retain correction evidence. New renewal/correction of a deleted record is blocked; existing successful intents retain their replay semantics.
- Head Manage shows Details/Renew/Close for Active and Details/Renew/Delete for Closed, with an exact vendor/type/year confirmation and mandatory reason. No Collector Delete control.

Additive migration **20261022124440_FishMeatRegistrationLogicalDeletion20261022** adds four nullable evidence columns, a tenant/operation unique index, a deletion-evidence check and the lifecycle-constraint extension. Existing tenant-composite restrictive foreign keys are unchanged. Status is an EF concurrency token. Reviewed forward SQL: [registry-logical-deletion.sql](../evidence/fish-meat-registration-requests/registry-logical-deletion.sql). No backfill or financial table change. Model/snapshot check passes. Down is tested on unused scratch schema; it explicitly refuses rollback when tombstones exist, before dropping evidence columns. Prefer a reviewed forward repair after use.

Release compatibility gate: install the reviewed schema and compatible API before exposing Delete. Once Status=Deleted (3) has been used, do not roll back to a pre-feature API: the old vendor quote guard recognized Closed (2) only. A compatible forward repair is required; schema rollback refusal alone cannot make an old API safe. No such production release or rollback was attempted here.

### Reconciliation of earlier imports (no backfill performed)

1. Recover original persisted operation identities/import evidence where available, or have the Head review an explicit CSV-row-to-**exact registration ID** mapping. Reuploading this CSV does not recreate the original operation IDs. No matching/merging by name alone.
2. Read the exact tenant/year/type registration through the existing management service. Dry-run each supported office field as Existing / Proposed / No change / Fill blank / Conflict. CSV blanks propose no overwrite. Ambiguous/missing IDs and changed nonblank values require explicit review, not an automatic correction.
3. Retain the full original registration DTO as `Before`. For each approved correction call the existing **PUT `/api/office-sources/fish-meat/registrations/{id}/office-details`** with that snapshot and the reviewed office facts, preserving all unedited values. This uses canonical validation, serializable concurrency and audit evidence. Refresh/re-review on conflict; never blindly retry over a new snapshot. Historical reference is not editable through this correction contract; conflicting references require separate review. Both supplied files leave it blank.
4. Verify the same registration ID/year/type/status, original NPM links/renewals, and unchanged collections/receipts; compare Manage details, Holder List/Office Register and export. No production reconciliation preview or write was performed here because reviewed exact IDs/production evidence are unavailable.

Counts retain their existing definitions: main registry is Active for the selected tax year; Manage/official lists include Active and Closed; Deleted is a separate authorized historical scope. “NPM review” counts unresolved eligible Fish/Meat NPM occupancies lacking an uncorrected annual link, not unregistered people. Monthly money still separates Vendor Fee and Weight & Measure and comes from actual canonical activity.

### Validation and release gates

Commands run from the isolated worktree (suites separately):

```powershell
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj -c Release --filter 'FullyQualifiedName~FishMeat|FullyQualifiedName~TenantBackupCoverage|FullyQualifiedName~TenantRestore'
dotnet test EEMOCantilanSDS.ComponentTests/EEMOCantilanSDS.ComponentTests.csproj -c Release --filter 'FullyQualifiedName~FishMeat|FullyQualifiedName~NpmVendorRegistration'
dotnet test EEMOCantilanSDS.Mobile.ComponentTests/EEMOCantilanSDS.Mobile.ComponentTests.csproj -c Release --filter 'FullyQualifiedName~CollectionSessionEditorTests|FullyQualifiedName~NpmPendingRequestTests|FullyQualifiedName~RegistrationRequestAndRecordTests'
dotnet test EEMOCantilanSDS.IntegrationTests/EEMOCantilanSDS.IntegrationTests.csproj -c Release --filter 'FullyQualifiedName~CollectionSessionTests|FullyQualifiedName~FishMeatPayorFollowUpTests'
dotnet build EEMOCantilanSDS.Api/EEMOCantilanSDS.Api.csproj -c Release --no-restore
dotnet build EEMOCantilanSDS.Client/EEMOCantilanSDS.Client.csproj -c Release --no-restore
dotnet build EEMOCantilanSDS.Mobile/EEMOCantilanSDS.Mobile.csproj -c Release -f net10.0-windows10.0.19041.0
dotnet ef migrations has-pending-model-changes --project EEMOCantilanSDS.Infrastructure --startup-project EEMOCantilanSDS.Infrastructure
git diff --check
```

Results: unit/schema/backup **69 passed**, Head components **55 passed**, Mobile components **73 passed**, PostgreSQL/controller/financial regressions **130 passed**; all **0 failed / 0 skipped**. Additional focused PostgreSQL export/projection checks **3 passed / 0 failed / 0 skipped**. The import regression first failed against unchanged import code (1 failed as expected), then passed with normalized preview/persisted fields, blank preservation and conflicting-retry checks. Migration apply, unused rollback/reapply, used rollback refusal, delete races, request lifecycle, original cash/SRC replay and tenant boundaries are exercised on disposable PostgreSQL, not a retained database.

API and Client Release builds passed (final incremental runs 0 warnings / 0 errors); Windows Mobile Release passed with existing warnings. Initial Client `--no-restore` failed because this fresh worktree had no assets; the component restore supplied them and the subsequent build passed. Existing dependency/compiler warnings were not changed as unrelated work.

The closed-vendor linked-source guard was deliberately removed once in this isolated worktree: the regression failed (`Assert.False`, actual True), proving the old path still quoted linked utilities for a closed vendor. Restoring the guard passed **8 targeted PostgreSQL tests**, including the same linked-source test, independent NPM eligibility, import/projection/export, deletion evidence/retry, normal request activation, pending requests, concurrency, migration rollback and missing-schema behavior.

Final sequential verification against the final source repeated **69 unit/schema/backup, 55 Head component, 73 Mobile component and 130 PostgreSQL tests**, all **0 failed / 0 skipped**. API and Client Release builds each finished with **0 warnings / 0 errors**; Windows Mobile Release finished with **19 existing warnings / 0 errors**. Final no-build EF model/snapshot check found no changes. Scoped source CSS braces **59/59**, generated Client bundle **13,678/13,678**, and `git diff --check` passed. The isolated worktree has 40 changed/new paths, no deleted files, no staged/committed changes. Primary checkout HEAD remains **17b9d100e8ddffdd747f5688a8e49818693d078a** on its original branch; its unrelated pending work is preserved.

Principal source paths for review:

| Area | Paths (relative to repository root) |
|---|---|
| Import and lifecycle | `EEMOCantilanSDS.Application/Common/Revenue/OfficeCollectionWorkflow.Import.cs`, `.Management.cs`, `.Deletion.cs`, `.NpmAssociations.cs`, `OfficeCollectionWorkflow.cs`; `EEMOCantilanSDS.Domain/Entities/Revenue/FishMeatVendorRegistration.cs` |
| Request activation/readiness | `EEMOCantilanSDS.Api/FishMeatRequestActivation.cs`, `Program.cs`; `EEMOCantilanSDS.Application/Common/Interface/Persistence/IFishMeatRequestSchema.cs`; `EEMOCantilanSDS.Infrastructure/Repositories/Revenue/FishMeatRequestSchema.cs`; `OfficeCollectionWorkflow.FishMeatRequests.cs` |
| Contracts and transport | `EEMOCantilanSDS.Application/Dtos/Mobile/CollectionSessionDtos.cs`, `VendorRegistryManagementDtos.cs`; `EEMOCantilanSDS.Api/Controllers/Revenue/OfficeSourcesController.cs`; `IOfficeSourcesApiClient.cs` and `OfficeSourcesApiClient.cs` in their existing API-client folders |
| Persistence and source eligibility | `EEMOCantilanSDS.Infrastructure/Persistence/Configuration/FishMeatVendorRegistrationConfiguration.cs`, `Interceptors/AuditSaveChangesInterceptor.cs`; `Repositories/Revenue/CollectionSessionSources.LinkedSources.cs` and `.Native.cs`; generated migration/designer and `Migrations/AppDbContextModelSnapshot.cs` |
| Head/Mobile | `EEMOCantilanSDS.Client/Components/Pages/Menus/FishMeatVendorFees.razor` / `.razor.css`, `FishMeatVendorManage.razor`; `EEMOCantilanSDS.Mobile/Components/Shared/CollectionSessionEditor.razor` |
| Regression coverage | `EEMOCantilanSDS.IntegrationTests/CollectionSessionTests.RegistryIntegrity.cs`, `.LinkedSources.cs`, `CollectorApiHost.cs`; existing Fish/Meat Head tests, `EEMOCantilanSDS.Mobile.ComponentTests/CollectionSessionEditorTests.cs`, `FishMeatRegistryLifecycleTests.cs` and `FishMeatRequestActivationTests.cs` |

CSS source/generated bundle braces are balanced; whitespace checks pass. No browser or native GUI acceptance: CUA inventory was empty and opening IAB returned `Browser is not available: iab`. No deployed API/APK was inspected or updated. A signed versioned Collector APK and separately authorized API/Client migration/release are required before users see these changes. Manual desktop/tablet/Android checks and the exact-ID Imelda investigation remain pending; build/component success is not visual or production acceptance.
