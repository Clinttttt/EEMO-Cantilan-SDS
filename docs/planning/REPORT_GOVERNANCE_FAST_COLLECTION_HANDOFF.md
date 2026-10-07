# Report governance, fast collection and Space Rental backend handoff

Local backend branch `codex/report-governance-fast-collection`, based on `b1fd3b1d`. IA-066 governs the new decisions. No frontend styling or page implementation changed; shared HttpClient/Mobile.Core methods expose the typed contracts only. This document does not advertise a production release.

## Space collection readiness and assignments

Existing `GET /api/Collectors/{id}/collection-operations` and `PUT` with `ReplaceCollectorOperationAssignmentsRequest.OperationCodes` now include:

| Code | Source | Policy and intent |
|---|---|---|
| `KANMANGGAY_SPACE_RENTAL` | `ObligationKind.KanmanggaySpaceRental` | OR; account + billing year/month + amount against authoritative remaining monthly balance |
| `FIESTA_ARAW_LOT_RENTAL` | `ObligationKind.FiestaArawLotRental` | OR; account + event's year/month + amount against that single event balance |
| `VEGETABLE_FRUIT_SPACE_RENTAL` | existing governed service | Existing WholePayment OR / DailyTransaction CT policy and configured amount rules; unchanged |

Fiesta and Araw retain `LotRentalEvent` identity, event date, independent numbering and the existing shared official classification. Assignment does not create an obligation, configure a rate, or grant another operation. Existing classic rent facilities retain facility assignments and source cutover requirements. No additional invented space source was registered.

`CollectorOperationAssignmentDto` remains Code/DisplayName/Assigned. New codes are in the existing supported catalog. `CollectorOperationCapabilityDto` returns Ready or NeedsPolicy/NotAssigned according to current collector, assignment and effective OR policy. Neither physical serial stock nor browser-selected classification grants authority.

`GET /api/mobile/collection-session/eligible?payorId=...` returns supported space operations in `Operations[].Choices`. `EligibleChoiceCount` and `CanAutoSelect` retain their existing meaning. Space choices expose `Identity.AccountId`, `Year`, `Month`, `PeriodStart` and optional typed `Event`; `ObligationQuoteDto` also exposes Event. `SelectionKey` includes account and full period date. Fiesta/Araw choices have distinct event/date context, even when both use Lot 1. `AmountRule=PreparedBalance`, `ServerAmount/MaximumAmount=OutstandingAmount`, `RateId` and `RequiredInputs=[AmountReceived]`. No frontend calculation or name matching.

Add a `CollectionSessionItemIntent` with Kind=Obligation, ConfirmedAmount, and `SessionObligationIntent(AccountId, Year, Month)` to the existing quote/record endpoints. Quote checks assignment, tenant, selected Payor ownership, policy, rate/remaining/version; record revalidates inside the existing Serializable session. One item still produces one Collection/SRC. Standalone `POST /api/mobile/fast-collections/space-obligation` accepts `MobileObligationPostRequest(ClientOperationId, AccountId, BillingYear, BillingMonth, ReceivedAmount, BusinessDate)` and calls the SAME composer obligation writer. `IMobileApiClient.PostSpaceObligationAsync` returns `EcfPostOutcomeDto` (CollectionId, ReferenceCode, disposition, amount, line count, replay flag); its name is historical compatibility, not ECF classification.

### Kanmanggay create → list → quote diagnosis

The backend listing already queries tenant-scoped accounts by Kind and returns `SubjectLabel` as the space number; a space-only account legitimately has no StallId/StallNo. Both Manage/List must use `Id`, `SubjectLabel`, `PayerName`, Arrangement and ContractReference, not require a Stall row. `GetWorkspaceAsync` uses the same account DTO. Reads do not assess periods.

A reproducible date bug was found: a mid-month opening stored its rate at actual ActiveFrom but monthly quotes searched only month-start. The first Kanmanggay period now resolves at ActiveFrom; subsequent periods resolve at month-start. Existing assessed amounts stay frozen, future starts have no current collectible period, and no contract or rate backfill is created. A PostgreSQL regression creates through the real workflow, reads Accounts/Workspace immediately, discovers the linked source, posts partial then remaining through session/standalone, and proves replay adds neither Collection nor period. Reintroducing the old date rule makes that test fail.

This proves the contract path; it does not claim inspection or repair of a particular production holder. If a page still appears empty, inspect its selected Kind/filter and use SubjectLabel rather than StallNo.

## Official report governance

SuperAdmin (existing Head configuration role) writes; SuperAdmin/Admin reads. Tenant and actor derive from authentication. Office row keys come from `OfficialMonthlyIncomeStructure`, never mutable labels or group footers.

| Endpoint | Request / response |
|---|---|
| `GET /api/official-reports/monthly-income?year=&month=` | `OfficialMonthlyIncomeDto` with existing groups/cells plus target coverage |
| `GET /api/official-reports/governance?year=` | All `ReportRevisionDto` revisions for that tenant/year |
| `POST /api/official-reports/targets` | `SetAnnualTargetRequest(ClientOperationId, RowKey, Year, Amount, ApprovedSource, Reference?, Note?, ExpectedRevisionId?)` → ReportRevisionDto |
| `POST /api/official-reports/monthly-income/adjustments` | `SetMonthlyIncomeAdjustmentRequest(ClientOperationId, RowKey, Year, Month, ExpectedSystemAmount, OfficialAmount, Reason, Reference?, ExpectedRevisionId?)` → ReportRevisionDto |

`IOfficialReportsApiClient` exposes GetGovernanceAsync, SetTargetAsync and AdjustMonthlyIncomeAsync. Same operation/intent replays (normalized two-decimal amounts); changed intent yields `ReportIntentConflict`. New revisions require the current revision ID (`null` for first). Stale revision or serialization conflict: `ReportRevisionChanged`. Changed system cash basis: `SystemAmountChanged`. Invalid money precision/storage range: `InvalidReportAmount`. These are machine codes, not text to interpret. Validation and authorization failures retain existing ResultStatus/HTTP handling.

`ReportRevisionDto` includes Id, Kind (AnnualTarget=1/MonthlyAdjustment=2), RowKey/year/month, Revision, SupersedesId, Amount, SystemAmountAtRevision, SourceOrReason, Reference/Note, ActorId/Name and RecordedAtUtc. Amount is the annual approved target or signed report delta according to Kind. Revisions append; they do not accumulate deltas. To clear an adjustment, append OfficialAmount equal to current SystemAmount with the latest revision ID and reason.

`MonthlyIncomeCellDto`: Legacy, Canonical, SystemAmount, AdjustmentAmount, OfficialAmount, IsAdjusted and Adjustment audit DTO. Total aliases OfficialAmount for existing print/report consumers. Group/month/year totals are derived, not editable. Later real cash changes change SystemAmount while the approved fixed delta remains explicit. Collection Activity/registers/collector cash/remittance stay based on Collections.

Rows expose AnnualTarget and Attainment = official YTD / annual target × 100; zero/unconfigured targets have null attainment. `TargetCoverageDto` states None/Partial/Complete, TargetedRows/TotalRows, AnnualTarget, CoveredActual and Attainment. Partial coverage has no complete-scope aggregate percentage. `RevenueSourcePerformanceDto` carries the same coverage; its rows expose SystemAmount/AdjustmentAmount plus approved annual target/YTD attainment. Use official actuals for target KPI, canonical cash for cash position. Do not substitute report totals into remittance.

BBQ is in RENT, Slaughterhouse in SLAUGHTERHOUSE. Real unresolved PENDING rows remain. No classification or prior Collection is changed.

## Follow-up

Existing `GET /api/Reports/follow-up?year=&month=&facility=&operationCode=` / `IReportsApiClient.GetScopedFollowUpQueueAsync` preserves classic `Items` and adds `ObligationItems`. A facility filter selects classic facilities; an operation filter selects that space operation. Neither filter supplied includes both feeds.

`ObligationFollowUpItemDto`: AccountId/PayorId, PayerName, SpaceNumber, Scope, PeriodStart, Assessed/Collected/Outstanding, Priority, Event and Link. Stable row key is AccountId + PeriodStart. `FollowUpScopeDto`: Kind (Facility=1, MonthlySpace=2, EventLot=3), OperationCode, DisplayName, optional Facility. Merge feeds by typed scope; do not fabricate a FacilityCode. Kanmanggay shows unpaid/partial closed months; Fiesta/Araw shows unpaid events strictly before as-of date. Paid periods are excluded. Current/future report dates are capped at today's business date. Reads never create periods. No receivable is synthesized from Vegetable/Fruit daily CT transactions. Existing historical follow-up endpoints remain unchanged.

## Remittance multi-collector contracts

`POST /api/remittances/review` with `RemittanceReviewRequest(From, To, CollectorIds?, Instrument?)` returns `RemittanceReviewDto(Collectors, Total, CollectionCount)`. Null/empty IDs means all; selected IDs form a distinct tenant-validated union. Each collector retains a `RemittanceScopeDto` with canonical eligible Collections and ExpectedAmount. `POST /api/remittances/history/filter` accepts the same filter and returns existing history DTOs. `IRemittancesApiClient.GetReviewAsync/GetFilteredHistoryAsync` expose both.

RecordBatch and void behavior are unchanged: separate collector accounting boundaries, existing atomic batch, no replacement Collection/SRC, no income change. No Physical OR/CT input is introduced.

## Fast Transportation and Tabo

Transportation `ConfigureGovernedServiceRequest.QuickAmountEnabled` defaults false and is tenant/effective-version scoped. Only Transportation can enable it. Existing vehicle classes/rate histories are unchanged.

`GovernedServiceMode.QuickAmount=3` accepts ReceivedAmount through the existing governed post/quote writer with no VehicleClassCode/FeeOptionId. It requires CT and freezes mode, amount, policy/config IDs; vehicle/rate fields remain null. Existing class mode uses Mode=null and approved VehicleClassCode. Itemized choices advertise both modes where configured; zero classes is not a blocker when QuickAmount is enabled. Standalone and basket reuse the same authority. Backend does not invent vehicle counts or classify bulk totals as a class.

Tabo needs vendor/day traceability, so `POST /api/mobile/fast-collections/tabo/quote` and `/tabo/record` use `TaboBatchRequest(Items, QuoteFingerprint?)`, 1–200 distinct registered VendorId/day entries and distinct stable child ClientOperationIds. Each item is the existing FeeSchedulePostRequest with OperationCode=TABO. No name-only batch vendor creation. Quote returns typed item ProblemCode/Message, server Amount/Instrument/Version, Total, CanRecord and Fingerprint. Record requires the fingerprint and preflights all children, then runs the existing canonical writer under one Serializable transaction. Full retry returns each original child SRC. Changed child intent conflicts. Quote problems require review; no offline rate authority or aggregate vendor is invented. Tabo remains outside the general itemized adapter in this slice.

`IMobileApiClient.QuoteTaboBatchAsync/RecordTaboBatchAsync` expose the batch; CachingMobileApiClient passes quote through and invalidates collection-entry caches after successful record. UI/offline batch presentation remains Claude's lane.

## Weight & Measure and preserved dependencies

Linked Meat-area discovery with a configured effective rate, 3 kg quote, OR canonical recording and replay passes PostgreSQL. Fixture amount is quantity × fixture rate, not a guessed ordinance fee. Backend validity is proven for this shape; disabled Add Item with a visible selected rate requires frontend selection/binding/re-render inspection. No Pantom Dant production identity was name-linked or mutated.

Collection Composer/Drafts remain required by ECF, operation-specific Web collection, session obligation adapters and tests. Retiring `/collections/current` does not authorize deleting these services. Utility/rent/Vendor Fee/weighing classifications and existing queue/SRC rules stay intact.

Cantilan's current MunicipalitySeeder already uses MEEDO and the full new office name. Runtime branding reads persisted tenant values; this pass does not rename assemblies or update production tenant branding.

## Persistence and validation

Additive migration `20261005232530_ReportGovernanceAndTransportationQuickAmount`: OfficialReportRevisions (unique tenant/client intent and tenant/scope/revision indexes, precision/scope validation), and QuickAmountEnabled default false on existing service setting versions. Report revisions participate in tenant export/restore coverage. Migration is exercised by throwaway PostgreSQL fixtures only. No space schema migration or historical backfill.

EF use for these bounded workflows is consciously registered in the existing architecture boundary test: shared serializable report revisions, read-only obligation feed, and a partial of the existing Tabo writer. No second money/calculation authority.

Validation: 2,511 unit tests passed; the selected PostgreSQL financial/source suite passed 164 tests with two historical snapshot-only tests skipped (no STALLTRACK_SNAPSHOT_DB). The final strengthened space and report-isolation tests also passed individually. Thirteen official-report component tests passed, including rendered adjusted amounts/targets. API, HttpClients and Mobile.Core Release builds passed. EF reported no pending model changes; git diff --check passed. Tabo's injected failure after the first child save rolled back all Collections/lines/posting operations and retry succeeded. Source/report/remittance regressions remain green; no production verification is claimed.

Implementation commit: `939eec0c` (report governance, space readiness, fast collection contracts). Regression-test commit: `1e74e30` (report isolation, space create/query/collection, Tabo rollback, remittance filtering). Exact changed paths are available in these commit manifests (`git show --name-only <commit>`) and the subsequent documentation commit on this branch. No Client or Mobile Razor/CSS file was rewritten. The only Mobile.Core implementation edit is Services/CachingMobileApiClient.cs to expose and invalidate the new backend write contracts.
