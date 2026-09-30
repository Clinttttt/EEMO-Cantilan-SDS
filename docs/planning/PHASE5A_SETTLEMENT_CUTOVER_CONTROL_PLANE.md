# Phase 5A: Settlement Cutover Control Plane

**Status:** Implemented as controlled tooling and test evidence only. PostgreSQL/Testcontainers validation has now run successfully, so Phase 5B is **READY FOR EXPLICIT CANARY REVIEW**. No real UtilityBill or PaymentRecord source was moved to Pending Cutover or Canonical. This document does not authorize activation.

**Scope:** Exact tenant-owned UtilityBill Electricity/Water source parts and monthly-rent PaymentRecord identities. Names, phone numbers, document numbers, and other display text are not source selectors.

## State and command boundary

The required sequence is:

```text
Legacy -> exact source Pending Cutover -> reconcile and quiesce -> freeze opening position -> Canonical
```

- The readiness endpoint is `POST /api/settlement-cutovers/readiness`. It is authenticated for Admin/SuperAdmin and tenant-bound. It only evaluates; it does not mutate the source, opening balance, Collection, or document inventory.
- `SettlementCutoverWorkflow` provides the exact-scope Pending, freeze, and activation commands. Phase 5A exposes no HTTP mutation route for these commands. Freeze and activation are exercised only with isolated integration-test rows.
- Each UtilityBill transition changes only the selected Electricity or Water part. A source cannot jump directly from Legacy to Canonical, and activation requires its unique `CollectionSettlementCutover` record.
- Freeze stores assessment, legacy settled evidence, outstanding, source boundary version, UTC time, tenant/source identity, actor, reconciliation evidence, and reconciliation actor/time in the existing cutover entity. It does not create a Collection or cash event.
- Source optimistic-concurrency tokens and a serializable database transaction protect Pending, freeze, and activation. Activation reloads the immutable evidence, re-evaluates the exact source and blockers, and requires the current readiness fingerprint to equal the fingerprint frozen at review, in addition to matching the source version and all three opening amounts. The fingerprint covers effective policy and the identities/custodians of active assigned documents; source version is checked separately because freeze itself advances that boundary token.

## Readiness evidence and blockers

The evaluator returns exact source identity and authority, source version, assessment, legacy settled evidence, outstanding, required instrument, affected collectors, per-collector capability gaps, unresolved online transactions, source-linked reconciliation operation/document identities and document numbers, active assigned document count, blockers, warnings, and a deterministic fingerprint for the reviewed readiness result. Authorized staff can see the original operation and physical ticket that must be reconciled, not only an aggregate blocker count.

The operator evidence must attest writer quiescence, device queue drain, complete field-device inventory, online-payment drain, physical accountable-document reconciliation, and report-path verification. Evidence is tenant/source scoped, bounded, fingerprinted, and stored with a successful freeze. Server-detected source, policy, allocation, online-payment, document, or posting-operation blockers override operator attestation.

The application has no trusted production device-capability registry. Collector version, payload capability, queue-drain time, and supporting evidence reference are therefore explicit operator-collected evidence; the evaluator does not claim that the backend observed a device. For WCF, every collector currently assigned to the source facility must have a non-empty application version, WCF payload version 1 or later, a drained legacy queue, UTC verification time, and evidence reference. Assigned-collector ECF and monthly-rent scopes remain blocked because their canonical Mobile writer and late-issued-document reconciliation path are not ready. No WCF payload upgrade or APK release is implied.

The currently recorded Mobile project version is display version `1.1.11`, Android version code `13`. These are repository facts, not proof of deployed device versions or WCF payload capability. The production APK remains unchanged.

## Online, document, and late-submission handling

- Initiated, pending, provider-confirmed-but-unsettled, and reconciliation-required online payments block readiness for the matching PaymentRecord or UtilityBill source. Provider-confirmed money is not cancelled or reinterpreted.
- The monthly PaymentRecord online-checkout entry now refuses new checkout after that record leaves Legacy. NPM utility checkout already refuses while either utility part is Pending/Canonical. A request that passed its Legacy check and is still inside the external provider call may not yet have a persisted transaction row; the operator's online-drain evidence must therefore include in-flight server requests/provider work, not just a database query. The current system has no telemetry that proves this drain automatically.
- WCF readiness uses the required Cash Ticket policy. `WaterORNumber` is retained only as legacy evidence; it does not prove a physical Cash Ticket existed.
- Active CT assignments to affected facility collectors require physical inventory reconciliation. Source-linked `ReconciliationRequired` PostingOperations and accountable documents block a clean freeze. A physically issued document remains unavailable even when sync fails.
- Legacy Water payloads arriving after Pending Cutover are preserved under their original operation/document identity as reconciliation exceptions. They cannot alter opening settlement or become a fabricated Collection. Unresolved exceptions must be handled through a later controlled workflow; Phase 5A does not decide their financial resolution.
- Online transactions that could still alter the legacy source must be drained before freezing. No callback is automatically converted into a canonical Collection by the readiness workflow.

## Report readiness and limits

Canonical Collection Activity and classified Collection lines are available as the target source for canonical receipts. Existing legacy readers remain in service and are not all cut over: `GetReportOfCollections`, `FacilityReportsRepository` report queries, `DashboardRepository`, `TransactionFeedRepository`, utility registers/follow-up reads, and related legacy facility summaries still consume `PaymentRecord`, `DailyCollection`, or `UtilityBill` compatibility values. The readiness attestation requires the selected source's Collection Activity, classification totals, and applicable financial report path to be checked before freeze; it does not globally replace these readers or prove every legacy report is migrated.

Compatibility fields are projections after Canonical activation and must not be counted as additional receipts. Opening settlement is balance evidence only. The official cross-period RCD treatment remains unresolved under IA-043; Phase 5A preserves correction dates and relationships but does not choose that Office presentation rule.

## Validation boundary

The required Release-mode PostgreSQL/Testcontainers validation ran successfully on canonical commit `67d5d235379db30b47d9ca079c86f04690f4aec7`:

- readiness-after-freeze regression: 1 passed, 0 failed, 0 skipped;
- `WcfCollectionWorkflowTests`: 17 passed, 0 failed, 0 skipped;
- `EcfCollectionWorkflowTests`: 16 passed, 0 failed, 0 skipped;
- full `EEMOCantilanSDS.IntegrationTests` project: 101 passed, 0 failed, 7 skipped (108 total);
- Release solution build: succeeded, 0 errors, 179 warnings;
- `git diff --check`: passed.

The seven skips are existing opt-in historical snapshot tests that require `STALLTRACK_SNAPSHOT_DB`: five `OccupancyHistorySnapshotTests` and two `RegisterAndFollowUpAgreementTests`. The focused WCF/ECF and readiness suites had no skips. Docker was available; there were no Docker-unavailable skips. The PostgreSQL concurrency/transaction blocker is resolved, and Phase 5B is **READY FOR EXPLICIT CANARY REVIEW**, not activated, complete, or deployed.

### Remaining activation gates

Passing database tests do not clear source-specific rollout gates. Before any named source is activated, complete and record:

- exact tenant and source identity, followed by a fresh source-scoped readiness evaluation;
- reconciliation of legacy settlement, source writers, queued Mobile operations, and all issued physical documents;
- verified capability/version evidence for every collector device assigned to the affected source/facility; backend support alone is not device readiness;
- drain/reconciliation of in-flight online/provider activity, including calls not yet represented by persisted transaction rows;
- accountable-document custody and exception review, with no issued or reconciliation-required CT returned to available inventory;
- report-path verification for canonical Collection events and classification totals, confirming compatibility projections are not counted again;
- separate explicit Clint approval for the exact canary source and activation command.

Existing report readers including `GetReportOfCollections`, `FacilityReportsRepository`, `DashboardRepository`, `TransactionFeedRepository`, and utility registers/follow-up views still consume legacy source rows or compatibility values. The selected source's report path must be checked before freeze; this status does not claim global report migration. IA-043 cross-period RCD treatment remains an unresolved Office policy and must not be inferred here.

The PostgreSQL fixture review also confirmed a separate domain-validation gap: EF maps `Municipality.Code` to at most 30 characters, but `Municipality.Create` currently trims/uppercases without rejecting a longer code. The production onboarding command reaches this factory, and its validator only requires a non-empty code. A small fail-fast domain/onboarding validation change is warranted separately; this change did not widen the schema or alter domain behavior.

No source activation, production deployment, APK release, or evidence-image change occurred in Phase 5A.
