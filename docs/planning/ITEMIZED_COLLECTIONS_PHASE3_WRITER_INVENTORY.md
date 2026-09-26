# Phase 3: PaymentRecord Settlement Writer Inventory

**Status:** Current-code inventory and Phase 5 readiness input. This document does not activate any source or approve a cutover.

**Scope:** Existing monthly `PaymentRecord` sources used by eligible permanent monthly-rental facilities. Daily NPM `DailyCollection`, WCF/ECF `UtilityBill`, and other source domains remain outside the rent adapter.

## Current writers and cutover prerequisites

| Current path | Current behavior affecting monthly rent | Required before a source can become Canonical |
|---|---|---|
| `RecordPaymentCommandHandler` via `PaymentsController.RecordPayment` | Creates or updates `PaymentRecord`; `UpdateStatus` changes paid/partial/unpaid state; supplied legacy OR and `ClientOperationId` are attached. The same handler is called during collector offline replay by `SyncOfflineCollectionsCommandHandler`. | Route converted-source submissions through canonical posting, or preserve late/offline submissions as reconciliation exceptions with physical document identity. Do not write cumulative status as another settlement authority. |
| `OnlinePaymentSettlementService` | Completes monthly online checkout through `MarkPaidOnline` or `MarkPartiallyPaidOnline`; this is a distinct settlement writer from the office payment handler. | Block/quiesce before the boundary, drain initiated checkouts, and route eligible post-cutover payments through the canonical coordinator. |
| `InitiateOnlinePaymentCommandHandler` | May create a missing monthly `PaymentRecord` before checkout starts. It does not itself record receipt, but it can create the source row used by the later online writer. | Revalidate source authority at initiation and settlement; do not create a parallel obligation row or start a legacy settlement after Pending Cutover. |
| `SaveOrNumberCommandHandler` via `PaymentsController` | Attaches or changes the legacy OR string for an existing PaymentRecord. | Treat issued document identity as consumed/audited evidence. Once Canonical, use the accountable-document correction workflow; do not rewrite the legacy number in place. |
| `IssueOnlinePaymentOrNumberCommandHandler` | Assigns a receipt number after online settlement; the same handler also has source-specific paths for UtilityBill and daily collection. | Include pending online settlements and document issuance in reconciliation. Converted monthly rent must use the canonical AccountableDocument bridge. |
| `BulkImportPaymentHistoryCommandHandler` | Creates/imports PaymentRecords and may update existing legacy payment facts or backdate `PaidAt`. | Disable for the exact pending/canonical scope or route through controlled migration/reconciliation. Imports must not manufacture new post-cutover receipts from cumulative history. |
| Administrative payment edits reached through `RecordPaymentCommandHandler` | The existing `Unpaid` status path clears legacy payment/OR state; there is no separate PaymentRecord `MarkUnpaid` handler in the current application search. | Replace reset/edit semantics with attributed void/reversal/replacement history for Canonical sources. |

The current `PaymentRecord` domain guards reject legacy settlement and OR mutation methods after a source is Canonical, and `SettlementVersion` is an optimistic concurrency token. These guards are safety checks, not a substitute for the Q43 pending-cutover gate, draining queues, or coordinating all external writers.

The Phase 3 Composer adapter/projection is readiness work only. Real PaymentRecords remain `Legacy`; canonical posting rejects them. `BulkImportDailyHistoryCommandHandler` and NPM daily settlement handlers mutate `DailyCollection`, not monthly `PaymentRecord`, and require their own source-specific migration plan.

## Required inventory refresh at cutover

Before any scoped Phase 5 activation, repeat a repository search and operational reconciliation for:

- Web and Mobile calls to `RecordPaymentCommandHandler`;
- offline operations and issued physical OR/CT identities;
- online checkout transactions already initiated, callbacks and retries;
- PaymentRecord imports/backdated receipts and administrative resets;
- legacy OR-number entry paths, duplicate-number checks and reconciliation exceptions;
- report/readers that depend on `Status`, `PartialAmount`, `ORNumber`, `PaidAt` or `AmountPaid` projections.

The exact facility/source scope remains Legacy until every writer in that scope has a safe canonical or reconciliation path and its opening settlement has been frozen from reconciled evidence.
