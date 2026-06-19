# Offline-First Collection Sync — Handoff / Progress

**Status:** Backend foundation **COMPLETE & verified** (builds green, full test suite **248 pass**,
DB migration applied). **Mobile pending-queue + capture wiring + Records "Pending" UI = COMPLETE &
build-verified** (net10.0-windows10.0.19041.0, 0 warn/0 err). Offline logic was extracted into a testable
**`EEMOCantilanSDS.Mobile.Core`** (net9.0) library with **15 new unit tests** (store, mapping, sync
outcomes). Trip-number per-offline-date refinement (5e) is the only optional item left.
**Last worked:** 2026-06-20. **Nothing committed to git yet** (all changes are uncommitted in the working tree).

---

## 1. The goal (what we're integrating)

EEMO field collectors often have **no internet**. We want them to **collect offline**: the
collection is saved locally on the phone (SQLite) and marked **Pending**; when connectivity
returns it **auto-syncs** to the cloud PostgreSQL. Decisions the owner approved:

- **All facilities** support offline (NPM daily, monthly rentals TCC/NCC/BBQ/ICE, Slaughterhouse,
  Transport Terminal trips, Tabo-an vendors).
- **OR-on-sync**: the collector may enter the Official Receipt number offline; the server validates
  OR uniqueness **on sync**. A duplicate OR → that item is **Rejected** (surfaced for the collector
  to fix), never silently dropped or duplicated.
- Money-safety is paramount → idempotency + clear per-item outcomes.

Architecture rules to follow (see `.kiro/steering/CONTEXT.md`, `.amazonq/...`): Clean Architecture,
CQRS (MediatR), `Result<T>` pattern, repository pattern, FluentValidation, PostgreSQL/Npgsql,
collector identity always from the **token**, never the request.

---

## 2. Design decisions (LOCKED — do not re-litigate)

### 2a. Idempotency = entity-level `ClientOperationId` (DB-unique)
Each of the 5 collection records carries a nullable `Guid? ClientOperationId` (the device-generated
idempotency key) with a **unique filtered index** (`"ClientOperationId" IS NOT NULL`). This is the
gold-standard for idempotent financial writes — the DB itself guarantees a queued op is persisted at
most once, even under retries/races. `null` for normal online records (so online behavior is unchanged).

### 2b. Sync replays the EXISTING validated commands
The sync handler does **not** duplicate business logic. For each queued op it dispatches the existing
command (`RecordDailyCollectionCommand`, `RecordPaymentCommand`, `RecordSlaughterCommand`,
`RecordTripCommand`, `AddVendorToMarketDayCommand`) via `ISender`, passing the **offline business
date**, the **OR**, and the **ClientOperationId**. This reuses all validation (OR uniqueness,
facility-assignment, conflict checks).

### 2c. Per-item outcome classification
- **Synced** — created (or already-synced/idempotent hit).
- **Rejected** — terminal business/validation failure (HTTP 4xx: duplicate OR 409, validation 400,
  forbidden 403, not-found 404). The client must surface these for manual review; do NOT auto-retry.
- **Failed** — transient (5xx / unknown / exception). The client retries on the next sync.

### 2d. Central dedup repository
`ISyncRepository.IsOperationProcessedAsync(Guid)` checks all 5 tables (with `IgnoreQueryFilters`, so a
synced-then-voided record still counts as processed). This avoids touching 5 separate repositories.

---

## 3. BACKEND — what is DONE (files)

### Domain entities (added `Guid? ClientOperationId` + `SetClientOperationId(Guid)`)
- `EEMOCantilanSDS.Domain/Entities/Payments/DailyCollection.cs`
- `EEMOCantilanSDS.Domain/Entities/Payments/PaymentRecord.cs`
- `EEMOCantilanSDS.Domain/Entities/Slaughterhouse/SlaughterTransaction.cs`
- `EEMOCantilanSDS.Domain/Entities/TaboanMarket/TpmAttendance.cs`
- `EEMOCantilanSDS.Domain/Entities/TransportTerminal/TrmTrip.cs`
  - ALSO: `TrmTrip.Create(...)` gained an optional `DateTime? recordedAt = null` param (offline time;
    falls back to `DateTime.UtcNow`).

### EF configs (added unique filtered index on `ClientOperationId`)
- `EEMOCantilanSDS.Infrastructure/Persistence/Configuration/DailyCollectionConfiguration.cs`
- `.../PaymentRecordConfiguration.cs`
- `.../SlaughterTransactionConfiguration.cs`
- `.../TpmAttendanceConfiguration.cs`
- `.../TrmTripConfiguration.cs`

### Migration (generated + APPLIED to the dev DB)
- `EEMOCantilanSDS.Infrastructure/Migrations/20260619132147_AddClientOperationIdForOfflineSync.cs`
- Verified: `ClientOperationId` column exists on `DailyCollections`, `PaymentRecords`,
  `SlaughterTransactions`, `TpmAttendances`, `TrmTrips`.
- Re-apply elsewhere with:
  `dotnet ef database update --project EEMOCantilanSDS.Infrastructure --startup-project EEMOCantilanSDS.Api`

### Commands extended (added optional `Guid? ClientOperationId = null`; Trip also `DateTime? OccurredAt`)
- `Command/DailyCollections/RecordDailyCollection/RecordDailyCollectionCommand.cs` (+ handler stamps new record)
- `Command/Payments/RecordPayment/RecordPaymentCommand.cs` (+ handler stamps new record)
- `Command/Slaughterhouse/RecordSlaughter/RecordSlaughterCommand.cs` (+ handler stamps record)
- `Command/TransportTerminal/RecordTrip/RecordTripCommand.cs` (+ handler passes `recordedAt` + stamps)
- `Command/TaboanMarket/AddVendor/AddVendorToMarketDayCommand.cs` (+ handler stamps attendance)
- NOTE: online callers (the existing MobileController endpoints, tests) pass nothing for the new
  optional params → behavior unchanged.

### Dedup repository
- `EEMOCantilanSDS.Application/Common/Interface/Persistence/ISyncRepository.cs`
- `EEMOCantilanSDS.Infrastructure/Repositories/SyncRepository.cs`
- Registered in `EEMOCantilanSDS.Infrastructure/DependencyInjection.cs`
  (`service.AddScoped<ISyncRepository, SyncRepository>();`).

### Sync command/handler/validator (the orchestrator)
- `EEMOCantilanSDS.Application/Dtos/Mobile/MobileSyncDtos.cs`
  — `OfflineOperationKind`, `SyncResultStatus`, `SyncOfflineOperationDto`,
    `SyncOperationResultDto`, `SyncOfflineCollectionsResultDto`.
- `Command/Sync/SyncOfflineCollections/SyncOfflineCollectionsCommand.cs`
- `Command/Sync/SyncOfflineCollections/SyncOfflineCollectionsCommandHandler.cs`
- `Command/Sync/SyncOfflineCollections/SyncOfflineCollectionsCommandValidator.cs`
  (≤ 200 ops/batch; each op requires a non-empty `ClientOperationId`).

### API + client
- `EEMOCantilanSDS.Api/Controllers/MobileController.cs` → `POST /api/Mobile/sync`
  (`[Authorize(Roles = "Collector")]`).
- `EEMOCantilanSDS.Application/Common/Interface/ApiClients/IMobileApiClient.cs`
  → `SyncOfflineCollectionsAsync(SyncOfflineCollectionsCommand)`.
- `EEMOCantilanSDS.Infrastructure/HttpClients/ApiClients/MobileApiClient.cs` → impl (POST).

### Tests (5 added, all green)
- `EEMOCantilanSDS.Testing/Application/Sync/SyncOfflineCollectionsCommandHandlerTests.cs`
  — non-collector forbidden; already-processed → Synced w/o dispatch; success → Synced;
    duplicate OR (409) → Rejected; 500 → Failed.
- Full suite: **233 passed, 0 failed.**

---

## 4. The Sync API contract (for the mobile phase)

**Endpoint:** `POST /api/Mobile/sync` (collector JWT required).

**Request body** = `SyncOfflineCollectionsCommand`:
```jsonc
{
  "operations": [
    {
      "clientOperationId": "GUID",        // REQUIRED idempotency key (generate once per offline capture)
      "kind": 1,                           // OfflineOperationKind: 1=NpmDaily,2=MonthlyRental,3=Slaughter,4=Trip,5=TpmVendor
      "businessDate": "2026-06-05",        // the offline collection date (PH)
      "orNumber": "0001234",               // optional (OR-on-sync)

      // kind=1 NpmDaily:        stallId, isPaid(bool), fishKilos?(decimal)
      // kind=2 MonthlyRental:   stallId, status(PaymentStatus enum), partialAmount?(decimal)
      // kind=3 Slaughter:       ownerName, animalType(AnimalType enum), customAnimalType?, numberOfHeads(int), customRate?(decimal)
      // kind=4 Trip:            transporterId?, driverName, plateNumber, route, organization?, occurredAt(UTC DateTime)
      // kind=5 TpmVendor:       vendorName, goods
      // common optional:        remarks
      "stallId": "GUID",
      "isPaid": true
    }
  ]
}
```

**Response** = `SyncOfflineCollectionsResultDto`:
```jsonc
{
  "syncedCount": 3,
  "rejectedCount": 1,
  "failedCount": 0,
  "results": [
    { "clientOperationId": "GUID", "status": 1, "message": null },          // 1=Synced
    { "clientOperationId": "GUID", "status": 2, "message": "OR number already exists." }, // 2=Rejected
    { "clientOperationId": "GUID", "status": 3, "message": "Sync error..." }              // 3=Failed
  ]
}
```

**Client rules:**
- On **Synced** → mark the local row Synced (or delete it).
- On **Rejected** → mark Rejected + show `message`; do NOT retry automatically (needs the collector to fix, e.g. change a duplicate OR).
- On **Failed** → keep as Pending; retry on next sync.
- Reuse the SAME `clientOperationId` across retries (that's what makes it idempotent).

---

## 5. MOBILE PHASE — DONE (files + behavior)

App: `EEMOCantilanSDS.Mobile` (MAUI Blazor Hybrid). Build-verified on `net10.0-windows10.0.19041.0`
(0 warnings / 0 errors). The 5 collection screens now capture offline, the queue auto-syncs, and the
Records page shows a **Pending** button (right of the search bar) with a review sheet.

### 5a. Local store (device) — DONE
- `EEMOCantilanSDS.Mobile/Models/PendingOperation.cs` — local row; mirrors `SyncOfflineOperationDto`
  field-for-field (so it replays through the sync endpoint) + local metadata (`PendingLocalStatus`,
  `ResultMessage`, `CreatedAt`) + display fields (`FacilityLabel`, `Title`, `Detail`, `Amount`).
  `ToDto()` projects to the wire DTO. `PendingLocalStatus = Pending | Synced | Rejected | Failed`.
- `EEMOCantilanSDS.Mobile/Services/IPendingOperationStore.cs` + `PendingOperationStore.cs` —
  **JSON file** at `FileSystem.AppDataDirectory/pending-operations.json`, serialized with a single
  `SemaphoreSlim`, in-memory cache, best-effort (corrupt/missing file → empty queue, never throws into
  capture). Chose a flat file over SQLite to stay **dependency-free** (the queue is tiny — a handful of
  un-synced collections). Swappable later via the interface if a device DB is ever needed.
  `ClientOperationId = Guid.NewGuid()` is generated once at capture and reused on every retry.

### 5b. Capture offline — DONE (all 5 screens)
Each screen got `@using EEMOCantilanSDS.Mobile.Models` + `@using EEMOCantilanSDS.Mobile.Services` +
`@inject ...MobileSyncService Sync`. In each Confirm method, **before** the existing online API call:
`if (!MobileSyncService.IsOnline) { build PendingOperation → await Sync.EnqueueAsync(...) → close sheet → return; }`.
- `Market.razor` (NPM) → `NpmDaily` (paid collections only; offline "Not collected" is blocked with a
  sheet error — it's a no-money state change that needs the server).
- `MonthlyCollection.razor` → `MonthlyRental` (Paid/Partial only; offline "Unpaid" blocked).
  `BusinessDate = new DateOnly(SelectedYear, SelectedMonth, 1)`.
- `Slaughter.razor` → one `Slaughter` op **per animal line** (shared owner + OR), rate from Hog/Large/custom.
- `Terminal.razor` → `Trip` for **both** the quick ad-hoc trip (`TransporterId = null`) and the
  registered-transporter trip; `OccurredAt = DateTime.UtcNow`.
- `Taboan.razor` → `TpmVendor` (add vendor). `Amount = Collection.VendorFee`.
- **Kept online-only** (not part of the sync contract): NPM "Not collected" mark, monthly "Unpaid",
  TPM mark-paid / encode-OR, transporter registration. (`MarkTpmVendorPaidAsync`, `IssueOnlinePaymentOrNumberAsync`,
  `AddTransporterAsync` mutate existing rows / create roster entries — not collection captures.)

### 5c. Sync service — DONE
- `EEMOCantilanSDS.Mobile/Services/MobileSyncService.cs` — singleton, `ctor(IPendingOperationStore, IMobileApiClient)`.
  - `EnqueueAsync(op)` → store as Pending, refresh count, raise `Changed`, fire best-effort background sync.
  - `SyncNowAsync()` → gathers Pending + Failed, batches ≤ 200, calls `IMobileApiClient.SyncOfflineCollectionsAsync`,
    applies per-item outcome: **Synced → removed**, **Rejected → kept + message** (no auto-retry),
    **Failed → kept** (retried next time). No-op when offline / already syncing / nothing retryable.
  - Triggers: connectivity-restored (`Connectivity.ConnectivityChanged`), after each capture, manual
    "Sync now", and on entering Records. `static IsOnline` = full internet access. `event Action? Changed`
    (off the UI thread — consumers marshal via `InvokeAsync`). `PendingCount` = non-synced rows.
  - `SyncSummary` (readonly record struct: Synced/Rejected/Failed/Total).
- Registered in `MauiProgram.cs`: `AddSingleton<IPendingOperationStore, PendingOperationStore>()` +
  `AddSingleton<MobileSyncService>()`.

### 5d. Records "Pending" UI — DONE
- `Components/Pages/Menus/Record.razor` (+ new scoped `Record.razor.css`):
  - Search bar wrapped in `.search-row`; **Pending button** (`.pending-btn`, clock icon) sits to its right
    with a count **badge** (`.pending-badge` = `Sync.PendingCount`).
  - Tapping opens a **pending review sheet** (reuses the global `.sheet`/`.sheet-overlay` chrome): a
    connectivity strip, one row per queued item (Title · Facility · Detail · OR · Amount + a status pill
    Pending/Retry/Rejected), **Discard** on Rejected items, and a **Sync now** button (disabled while
    syncing/offline).
  - `OnInitializedAsync` subscribes to `Sync.Changed`, calls `Sync.InitializeAsync()`, and best-effort
    flushes the queue (reloading the server list if anything synced). `@implements IDisposable` unsubscribes.

### 5e. Trip-number refinement (minor, OPTIONAL — still open)
- Offline trips currently get **today's** trip number (`ITrmRepository.GetNextTripNumberForTodayAsync`),
  not the offline date's. `RecordedAt`/`OccurredAt` already carry the real offline time. If per-offline-date
  trip numbering matters, add `GetNextTripNumberForDateAsync(DateOnly)` to `ITrmRepository`/`TrmRepository`
  and have `RecordTripCommandHandler` use the op's business date when `OccurredAt` is in the past.

### 5f. Testability — `EEMOCantilanSDS.Mobile.Core` + unit tests — DONE
The offline logic is **not** in the MAUI app project (which a `net9.0` test project can't reference and
which pins MAUI runtime statics). It lives in a new platform-agnostic library:
- **`EEMOCantilanSDS.Mobile.Core`** (`net9.0`, references Application, in the .sln): `Models/PendingOperation.cs`,
  `Services/IPendingOperationStore.cs`, `Services/PendingOperationStore.cs` (ctor takes a **storage
  directory** — no `FileSystem` static), `Services/IConnectivityMonitor.cs` (abstraction:
  `bool IsOnline` + `event Action? ConnectivityRestored`), `Services/MobileSyncService.cs`
  (ctor `(IPendingOperationStore, IMobileApiClient, IConnectivityMonitor)`; instance `IsOnline`;
  loop hardened to break when no item advances).
- MAUI glue stays in the app: `EEMOCantilanSDS.Mobile/Platform/MauiConnectivityMonitor.cs` (wraps
  `Connectivity.Current`). `MauiProgram` registers `IConnectivityMonitor → MauiConnectivityMonitor`,
  `IPendingOperationStore → new PendingOperationStore(FileSystem.AppDataDirectory)`, and `MobileSyncService`.
  Screens/Records use the injected instance `Sync.IsOnline`.
- **Tests** (`EEMOCantilanSDS.Testing/Mobile/`, 15 added → suite now **248 pass**): `PendingOperationMappingTests`
  (`ToDto` field fidelity), `PendingOperationStoreTests` (temp-dir CRUD + reload-from-disk), and
  `MobileSyncServiceTests` (offline no-op / strict mock, Synced→removed, Rejected→kept+message+no-retry,
  Failed→kept, Failed-then-success retry, enqueue-offline stays pending, idempotency key reused,
  mixed batch). Uses Moq for `IMobileApiClient` + in-memory fakes for the store/connectivity.

> **Verification note:** the Mobile **app** was built only against the **Windows** TFM
> (`net10.0-windows10.0.19041.0`) — the dev target shown in the app screenshots. `Mobile.Core` (the
> tested logic) is plain `net9.0`. Android/iOS/MacCatalyst app TFMs were not built here; the app glue uses
> only cross-platform MAUI Essentials APIs (`Connectivity`, `FileSystem`, `SecureStorage`), so no
> platform-specific issues are expected, but a per-TFM build is still worth doing before shipping there.
> The offline **capture→queue→sync** flow has unit-test coverage but was **not** runtime/manual tested on a
> device or emulator (needs a running API + device); see the manual test recipe used during handoff.

---

## 6. Important notes / gotchas

- **NPM monthly is intentionally blocked**: `RecordPaymentCommandHandler` rejects NPM stalls
  (NPM is daily-only). So `MonthlyRental` sync ops must target TCC/NCC/BBQ/ICE only. NPM uses
  `NpmDaily` ops.
- **Time/dates**: business dates (`CollectionDate`, `MarketDate`, `TransactionDate`, `Year/Month`)
  come from the offline capture. Trip `OccurredAt` must be sent as **UTC**. Server stores timestamps
  in UTC; PH (UTC+8) is used for business-day logic (`PhilippineTime`).
- **Conflict example**: stall+day already collected online by an admin; the offline op for the same
  stall+day → the daily handler marks it paid / the stall+date unique index prevents a dup. The
  per-item result tells the client what happened.
- **Validation pipeline**: command validators run during sync dispatch (good — OR-on-sync etc.).
  A `ValidationException` is caught and mapped to **Rejected** (terminal).
- The base HTTP client's `UpdateAsync<,>` is PATCH; `PutAsync<,>` is PUT; `PostAsync<,>` is POST.
  The sync client uses `PostAsync`. (This bit us earlier on the Profile PUT — match method to route.)

---

## 7. How to verify (commands)

```powershell
# build API (covers Application + Infrastructure)
dotnet build EEMOCantilanSDS.Api/EEMOCantilanSDS.Api.csproj

# run tests
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj   # expect 233 pass

# apply migration to a fresh DB
dotnet ef database update --project EEMOCantilanSDS.Infrastructure --startup-project EEMOCantilanSDS.Api
```
Dev DB (local): `Host=localhost;Port=5432;Database=EEMOCantilanSDS.DB;Username=postgres;Password=12345`
(in `EEMOCantilanSDS.Api/appsettings.Development.json`). `psql` at `C:\Program Files\PostgreSQL\18\bin\psql.exe`.

---

## 8. Session context (other recent work, for continuity)

Earlier in this work-stream (already done, tested, NOT committed): NPM daily-only fixes (history modal,
ledger summary, reports, payment-modal proration), the web NPM "collection receipt" + generic
`CollectionReceiptModal`, mobile Records "Office/Admin" tag for admin-recorded entries, mobile Report
collector-scoping + full-month assessment + advance-paid counting + "last collection = business date",
TPM (Tabo-an) Friday market-day context on mobile Records/Reports, TPM Add-Vendor name picker
(select-or-type + Goods auto-fill + remove/X), and the mobile Profile page made functional
(GetCollectorProfile query + UpdateCollectorProfile command). See git `git status` for the full set of
modified files. Consider committing in logical groups before the mobile sync phase.
