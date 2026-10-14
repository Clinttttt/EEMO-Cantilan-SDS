# Contracts Monitoring V12 — binding frontend/backend contract

Baseline: `ea101648`. Backend worktree: `contracts-monitoring-backend-v12`; branch: `codex/contracts-monitoring-backend-v12`.
Published before implementation. No frontend files or Client DI will be edited by backend.

## HTTP and client

`GET /api/monitoring/contracts`, authenticated `SuperAdmin` (Head) or `Admin`, current authenticated municipality only.
No municipality query parameter. No mutations or monitoring table.
Existing authenticated `IReportsApiClient` gains:
`Task<Result<ContractsMonitoringPageDto>> GetContractsMonitoringAsync(ContractsMonitoringRequest request)`.
Existing `AddApiHttpClient<IReportsApiClient, ReportsApiClient>` registration suffices. Backend adds one Infrastructure read-query registration; no frontend DI change is needed.
DTO namespace: `EEMOCantilanSDS.Application.Dtos.Monitoring`.

## Query (`ContractsMonitoringRequest`)

All filters combine with AND. Empty strings mean no filter; names are case-insensitive.

| Parameter | Accepted values/default |
|---|---|
| search | literal substring of occupant, contract name/reference, space number or operation display name; trimmed; max 200 characters |
| operation | any named FacilityCode (`NPM`, `NCC`, `TCC`, `BBQ`, `ICE`, `SLH`, `TRM`, `TPM`, `Custom1`…`Custom5`) or `KM`, `VF`; omitted = all supported sources. A facility is monitored only when its stored archetype is DailyStall/MonthlyRental; this does not admit trip/slaughter/weekly records. |
| facilityId | optional non-empty GUID; matches facility sources only; foreign/nonexistent id returns an empty page, not foreign data |
| status | `Active`, `ExpiringSoon`, `Expired`, `NoContract`, `Ended`, `Unknown`, `NotYetEffective`; omitted = all |
| occupancyBasis | `SignedContract`, `SpaceOnly`, `Extension`, `Unknown`; omitted = all |
| expiryWindow | integer `7` or `30`; omitted = no window; includes only effective, unended signed contracts expiring in 0…N days |
| asOf | optional ISO date; omitted or equal to server Philippine today; any other date returns 400 (no historical/projected register) |
| page | 1-based; default 1; range 1…10000 |
| pageSize | default 25; range 1…100 |
| sort | `expiry` (default), `expiryDesc`, `occupant`, `operation`, `space`; null expiries last; ties always operation, space, source type, source record id |

## Response (`ContractsMonitoringPageDto`)

Filter clarification: `status=Active` means all signed contracts active today, including the `ExpiringSoon` subset, and matches `summary.activeCount`. Other status filters match the row status exactly. This clarification does not add or rename any API field.

JSON is camelCase; enum values are JSON strings. Dates are ISO `yyyy-MM-dd`; nullable fields remain null.

`asOf`, `summary`, `totalCount`, `page`, `pageSize`, `items`.
`totalCount` is after ALL filters, before pagination. Out-of-range pages return empty items and the real total.

`ContractsMonitoringSummaryDto`:
`activeCount`, `expiringIn7DaysCount`, `expiringIn30DaysCount`, `expiredCount`, `noContractCount`, `endedCount`, `unknownCount`, `notYetEffectiveCount`, `totalMonitoredOccupancies`.

Summary applies tenant, search, operation, facilityId and occupancyBasis filters, **not** status, expiryWindow, sort or pagination. Cards remain stable while selecting a status/window.
`activeCount` includes both Active and ExpiringSoon (signed, effective and unended). 7-day count is a subset of 30-day count, which is a subset of activeCount. Other status counts are disjoint from activeCount.

`ContractMonitoringRowDto` fields:

- `sourceType`: `FacilityContract` or `ObligationAccount` (`ContractMonitoringSourceType`).
- `sourceRecordId`: exact Contract.Id or ObligationAccount.Id. Never a name-based identity.
- `facilityId` (nullable GUID), `facilityCode` (nullable FacilityCode), `operation` (stable code), `operationDisplayName` (tenant name).
- `stallId` (nullable GUID), `obligationKind` (nullable ObligationKind), `payorId` (nullable GUID; informational, not a prerequisite).
- `actualOccupant` (nullable string), `contractName` (nullable string), `spaceNumber` (string).
- `occupancyBasis` (nullable OccupancyArrangement), `contractReference` (nullable string).
- `occupancyStart` (date), `effectivityDate` (nullable signed-contract date), `durationYears` (nullable integer).
- `expiryDate` (nullable date), `daysUntilExpiry` (nullable signed integer), `status` (`ContractMonitoringStatus`).
- `closureDate` (nullable recorded closure/termination date).
- `approvedCharge` (nullable decimal), `chargeFrequency` (`Monthly`, `Daily`, `Unknown`, enum `ContractMonitoringChargeFrequency`).
- `contractStatedAmount` (nullable decimal), `areaSqm` (nullable decimal), `areaLocation` (nullable string).

No URL is fabricated. Drill down using the typed sourceRecordId plus stallId/facilityId/facilityCode or obligationKind. Existing detail/read endpoints must retain their own tenant/role checks. This register is not authorization to mutate/renew.
Facility contract references are null: the existing ORNumber is receipt evidence, not a lease document reference.

## Scope and status

Facility sources: stalls with retained contract/occupancy records in DailyStall or MonthlyRental facilities (including tenant custom monthly facilities). Exactly one representative term per physical stall: effective live/date-covering term first, then future live term, then latest retained ended term; latest effectivity/creation/id break ties. Superseded/renewed historical terms are not extra register rows. An entirely vacant stall with no occupancy record is excluded.
Specialized sources: each canonical KM/VF account is a distinct source-owned occupancy, including closed accounts; equal names/numbers do not merge accounts. Hiding an operation in tenant settings does not delete its monitoring evidence.
Fiesta/Araw, Fish/Meat, transient market/trip/slaughter records, fines and utilities are excluded from this initial lease register.

Status precedence:
1. Closed/frozen facility or ended registration/account → Ended. This is administrative lifecycle, not an inference from expiry. Null closure evidence stays null.
2. Occupancy starts in the future → NotYetEffective.
3. Explicit SpaceOnly/Extension → NoContract, with null duration/expiry and no open-ended sentinel exposed.
4. Unspecified basis, absent/invalid signed date or duration → Unknown. No default three-year term and no substitution of ActiveFrom for missing contract evidence.
5. Complete signed evidence: `expiry = DomainRules.TermLastDay(effectivityDate, durationYears)` (day BEFORE anniversary). `asOf > expiry` → Expired; 0…30 days → ExpiringSoon; otherwise Active.

Expiry day is INCLUDED: same-day expiry has daysUntilExpiry=0, ExpiringSoon, and counts active and in both expiry windows. Expired contracts keep their occupancy/account identity; this API never closes them. Future effectivity is NotYetEffective even when occupancyStart is earlier.
An inactive predecessor whose explicitly recorded EndedOn still covers today can represent today's occupancy when renewal was registered ahead of time; the future successor is not counted active today. Conflicting legacy live terms are resolved deterministically, never counted twice; this read does not repair records.

KM's recorded three-year validation is unchanged. VF's optional/recorded durations are retained. Derived expiry on these accounts is **recorded-document monitoring only**, not a new financial/lifecycle rule. The October 9 lifecycle/renewal approval gate remains in force.

Approved charge and contract-stated amount are separate evidence. Monthly facility approved charge is the selected occupancy's recorded actual monthly rental when present, otherwise that occupancy's Contract.MonthlyRentalRate; it never borrows a newer occupant's Stall.MonthlyRate. Contract-stated amount comes from that selected signed Contract.MonthlyRentalRate. DailyStall approvedCharge is null (no invented monthly/daily assessment); frequency is Daily. KM/VF approved charge resolves the latest approved ObligationRate effective on asOf; contractStatedAmount is the separately recorded signed ContractMonthlyRate. No assessment or yearly equivalent is calculated.

## Errors and integration

200 returns the DTO directly, not a Result wrapper. Invalid query: 400 using existing `error`/field-keyed `errors` conventions (malformed binding may use ASP.NET validation ProblemDetails). Anonymous: 401; wrong role/unresolved or mismatched tenant: 403. Unexpected failure uses existing middleware. Empty tenant data is a successful empty page.

Claude 2 should use the existing Reports client, render null evidence explicitly, label summary scope as above, use status/window query filters for drill-down cards, and retain typed source identifiers. No backend merge/push/deployment is implied. Backend will notify the owner if this contract needs changing.
