# Phase 4: WCF / UtilityBill Water Writer Readiness

**Status:** Current-code inventory and Phase 5 cutover input. This document does not activate WCF or authorize a cutover.

**Scope:** NPM `UtilityBill` Water source part, its legacy/canonical settlement writers, assigned Cash Ticket evidence, online settlement, and current compatibility readers. Electricity/ECF remains a separate source part with its own authority state.

Phase 5A control-plane behavior, evidence limits, test-only mutation boundary, and rollout gates are documented in [PHASE5A_SETTLEMENT_CUTOVER_CONTROL_PLANE.md](PHASE5A_SETTLEMENT_CUTOVER_CONTROL_PLANE.md). The repository contains no trusted device-capability registry; WCF payload-v1 readiness is verified per assigned collector from explicit reconciliation evidence, not inferred from API support or the current APK version.

## Current state

- `UtilityBill.WaterSettlementAuthorityState` defaults to `Legacy`. Its source version and settlement authority were added by earlier itemized-collection migrations. Phase 4 adds no startup activation and no data migration that changes authority state.
- The Phase 4 WCF workflow is canonical-ready but refuses posting unless the individual Water source is `Canonical` and has a valid cutover identity. It resolves the Water assessment, readings, rate, classification policy, Payor relationship, and outstanding balance from the tenant-scoped `UtilityBill` and related authoritative rows.
- A WCF collection is one Cash Ticket, one WCF line, and one explicit allocation to `UtilityBill / Water`. The shared `CanonicalCollectionPostingCoordinator` is the only canonical financial writer used by the Web and focused Mobile entry points.
- Existing report/register readers still consume legacy Water status/amount fields. `ApplyCanonicalWaterProjection` maintains those as compatibility output during a future canonical posting; they are not a second settlement writer. Classified reports and official RCD transition remain required before activation.

## Writer inventory

| Path | Current behavior | Phase 5 readiness classification |
|---|---|---|
| `RecordUtilityPaymentCommandHandler` from `UtilitiesController.RecordPayment` | Updates cumulative Electricity/Water status, partial amount, legacy document strings and `ClientOperationId` through `UtilityBill.RecordPayment`. Water domain guards reject settlement changes after Water enters Pending Cutover or Canonical. | **Legacy-compatible** only while Water is Legacy. Quiesce for the scoped transition; after Pending, preserve late submissions as reconciliation evidence. |
| `MobileController.RecordNpmUtilityPaymentAsync` | Accepts the older cumulative `RecordMobileUtilityPaymentRequest` (`WaterStatus`, `WaterPartialAmount`, `WaterORNumber`) and dispatches the legacy utility command. | **Legacy-compatible** only while Water is Legacy. The Phase 4 guard routes non-no-op Water payloads after Pending/Canonical into a durable reconciliation outcome rather than applying the cumulative value. Old APK capability remains a cutover gate. |
| `SyncOfflineCollectionsCommandHandler` for `NpmUtility` | Replays the legacy offline utility DTO, which can include both Electricity and Water parts. | **Legacy-compatible** while Water is Legacy. Once Water is Pending/Canonical, WCF evidence is preserved under the original operation identity and not applied as a new canonical receipt. Issued CT evidence must be reconciled before opening settlement is frozen. |
| `WcfCollectionWorkflow.PostWebAsync` | Validates policy, tenant, source authority/version, outstanding Water amount, CT custody, and idempotency, then enters the shared coordinator. | **Canonical-ready, inactive.** Requires Water Canonical authority, so it cannot be used to bypass reconciliation/cutover. |
| `WcfCollectionWorkflow.PostMobileAsync` via versioned `WcfCollectionPostRequest` | Receives a v1 money-received intent with UtilityBill/Water identity, amount, source version, business date, assigned AccountableDocument identity, and ClientOperationId. Actor/tenant/authority and financial facts are server-derived. | **Canonical-ready, inactive.** Same posting protocol as Web. Collector must own the assigned unit and have NPM facility authority. |
| Online checkout initiation (`InitiateOnlinePaymentCommandHandler`) | May create/resume an online UtilityBill transaction before a payment callback; it does not itself settle the UtilityBill. | **Must be quiesced/drained for cutover.** No new Water checkout may cross the boundary without an approved canonical online flow. Existing captures are reconciliation inputs. |
| `OnlinePaymentSettlementService.SettleNpmUtilityAsync` | Legacy online callback can update both utility parts. If either source part is no longer Legacy, the captured transaction is retained as `ReconciliationRequired` and the UtilityBill is not mutated. | **Reconciliation-only** after Pending/Canonical. Drain in-flight payment-provider events before freezing opening settlement. Not a canonical WCF writer. |
| `IssueOnlinePaymentOrNumberCommandHandler` for NPM UtilityBill | Legacy receipt-number completion path for online utility transactions. It retains non-Legacy utility outcomes for reconciliation rather than changing source settlement or treating an OR as WCF's CT. | **Reconciliation-only** after Pending/Canonical. Legacy OR evidence must not be reinterpreted as a Cash Ticket. |
| Utility assessment/readings (`RecordUtilityReadingCommandHandler`, `UtilityBill.UpdateReadings` / assessment methods) | Updates Water readings/assessment and increments `WaterSourceVersion`; assessment mutation is rejected after that part leaves Legacy. | **Must be quiesced during the boundary; Legacy-compatible before it.** Reconcile pending source edits and freeze assessment/version before activation. It remains assessment authority, not a collection writer. |
| `AccountableFormCustodyWorkflow` / `AccountableFormsController` | Receives OR/CT books, registers individual units, assigns bounded CT ranges to active same-tenant collectors, and lists custody. | **Canonical-ready custody foundation.** Assignment and document-state uniqueness are validated; physical CT issuance evidence is not returned to stock after rejection. |
| Imports, reset/admin maintenance, and corrections | Repository search found no separate UtilityBill Water history-import writer. Legacy `Unpaid`/reset mutations enter the same utility command/domain guard; assessment edits use the source-version guard. Existing online and sync paths above remain separate callers. | **Must be quiesced/reconciled** for the exact Water scope. Re-run the writer search at cutover; future correction writers must preserve linked immutable correction history. |

## Read paths and reporting boundary

- `GetUtilityRegisterQueryHandler`, Mobile utility queries, follow-up queues, Payor payable reads, and utility financial/month-end report projections currently expose compatibility fields and/or UtilityBill balances. They remain useful legacy/source screens while Water is Legacy.
- The WCF Activity query reads canonical `Collection` parents containing a Water source line and returns the complete parent and line/source snapshots. It does not count Water projection fields as new revenue.
- Before any Water activation, classified cash reports, RCD, online-payment display/reconciliation, and other report consumers must be reviewed so each new post-cutover receipt is derived once from canonical Collection/correction events. The unresolved official cross-period RCD presentation remains pending Office confirmation.

## Required controlled cutover gate

For each explicitly named Water scope, mark Pending Cutover, quiesce legacy Web/Mobile/online writers, drain retries and in-flight checkout/callback activity, reconcile locally issued CTs and server operation/document identities, reconcile any source edits, then persist the evidenced opening settlement and activate Canonical authority. A stale/late legacy payload is an exception; it cannot change the frozen opening snapshot or return a physical CT to inventory. Scopes that cannot complete this gate remain Legacy.

## Phase 4 disposition

Phase 4 proves source facts, one shared Web/Mobile canonical posting protocol, CT custody, offline durability, and exception retention. It is readiness work only. No UtilityBill authority state is activated by Phase 4 and no production release is authorized by this document.
