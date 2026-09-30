# ADR-002 — Per-source canonical settlement cutover

**Status:** Accepted

**Date:** 2026-09-26

**Owners:** Clint; MASTER / V2 Planner (sole implementation owner). N/O/P/Q are paused candidate workstreams; their concern boundaries and partial work remain review evidence only.

**Related decisions/issues:** [IA-039 / Q42](DECISION_REGISTRY.md#ia-039--per-source-canonical-settlement-cutover); [IA-040 / Q43](DECISION_REGISTRY.md#ia-040--controlled-reconciliation-gate-before-cutover); [ADR-001](ADR_001_BUSINESS_PAYOR_IDENTITY.md)

## Context

**CURRENT IMPLEMENTATION:** `PaymentRecord` and `UtilityBill` retain cumulative paid/status fields. `UtilityBill.RecordPayment` overwrites cumulative settlement state; existing Mobile sync dispatches the legacy payment commands. The paused N candidate introduces a separately mutable `ReceivableObligation.OutstandingAmount`. These are implementation evidence, not approval of parallel settlement authorities.

**CONFIRMED STALLTRACK DECISIONS:** Clint approved the per-source incremental transition model in Q42 and the controlled reconciliation gate in Q43, including each decision's eight constraints below. This records target engineering architecture. No source is declared converted by this document and no candidate implementation is approved. N/O/P/Q remain permanently paused as implementation sessions; MASTER implements sequentially under the approved baseline.

## Decision

For each source converted to canonical settlement:

1. Preserve a frozen, evidenced opening settlement position at the cutover boundary. Opening legacy settlement remains explicitly identifiable as pre-cutover evidence. Do not manufacture a `Collection` or `CollectionLine` for it to make the new history appear complete.
2. All money received after cutover for that source is represented through canonical `Collection` + `CollectionLine` + allocation posting.
3. Existing cumulative paid/status fields may remain temporarily for compatibility, but after conversion they are projections only. They must not remain independently writable settlement authorities.
4. Retain enough cutover metadata to distinguish the original assessment, opening legacy settlement, canonical post-cutover allocations, reversals/corrections, and resulting outstanding balance.
5. Use an explicit per-source settlement-authority/cutover marker so the system unambiguously determines whether the source remains Legacy or has moved to Canonical settlement.
6. Every settlement writer for a Canonical source follows the canonical posting protocol: Web Composer, specialized operation payment actions, Mobile, online settlement, and correction/void/reversal. The old payment method and canonical allocation path must not independently mutate settlement.
7. Update compatibility projections atomically in the same transaction as canonical posting/correction.
8. Cash reports, RCD, and Collection Activity for the canonical post-cutover scope count actual canonical collection events. Opening legacy settlement is historical balance evidence and must never appear as newly collected revenue. Existing legacy history remains separately attributable; this decision does not erase it.

**APPROVED TARGET DESIGN:** Evidenced opening settlement + canonical post-cutover allocations + legacy paid/status compatibility projections, with one settlement authority after conversion and no second independently editable outstanding balance.

Specialized operations continue to own assessments and their business rules. A source adapter participates in canonical settlement and projection updates; Payor identity introduces no balance authority. Existing writers remain authoritative only for sources explicitly still marked Legacy. Conversion is not permission to invent a generic assessment engine.

## Controlled reconciliation gate — Q43

**Status:** Accepted by Clint on 2026-09-26. This resolves the operational readiness question left open by Q42.

Cutover must not rely only on server-side cumulative payment state. Valid physical collections may still exist in Mobile pending queues, issued CTs awaiting sync, in-flight Web posting, online settlement, or retry/idempotency processing.

1. **Mark the scope Pending Cutover.** The exact conversion scope is explicit and auditable. Do not use a vague global switch when converting only one source, facility, or revenue workflow. This transition state does not itself activate Canonical settlement authority.
2. **Quiesce legacy writers for that scope.** Temporarily prevent new legacy settlement activity in the affected scope. Unrelated sources may continue operating normally.
3. **Drain and reconcile in-flight activity.** Reconcile collector Mobile pending operations, physically issued accountable documents, queued retries, Web transactions already in progress, online settlement already initiated, and server-side idempotent operations that may still complete. Quiescing new activity must not silently discard existing events.
4. **Verify physical document state.** A physically issued CT represents a real collection event even when server sync has not completed. Do not freeze an opening settlement that ignores an issued physical document.
5. **Freeze the evidenced opening position only after reconciliation.** Retain enough boundary metadata to identify the source/obligation, cutover timestamp/version, opening assessment, opening settled amount, opening outstanding amount, and reconciliation evidence/status. Choose the exact persistence shape from the repository model; these are conceptual requirements, not permission to invent another accounting subsystem.
6. **Activate Canonical authority.** After activation, Collection + CollectionLine + Allocation governs new settlement activity for the converted source. Legacy cumulative fields are compatibility projections only, under Q42's atomic protocol.
7. **Preserve unexpected old submissions as reconciliation exceptions.** A late old Mobile/Web/online submission must not silently use the old settlement method, automatically modify the frozen opening snapshot, or reinterpret a cumulative legacy value as a new receipt. Preserve its original ClientOperationId and document identity for controlled review. Any already-issued physical OR/CT remains consumed.
8. **Scopes that cannot pass remain Legacy.** Do not force all EEMO operations to convert together. Require reconciled pending transactions, ready writers, a ready compatibility projection, and safe Mobile/offline behavior before activation.

**APPROVED SEQUENCE:** Reconcile -> freeze evidenced opening position -> activate Canonical authority.

For example, a WCF assessment of PHP 800 with PHP 200 settled on the server and a further PHP 100 collected on an offline issued CT has a reconciled opening settled amount of PHP 300 and opening outstanding of PHP 500. Only then activate Canonical authority. The PHP 100 must not disappear merely because its synchronization lagged.

Reconciliation is required evidence; an empty server queue alone does not establish that device queues and physical forms are reconciled. Late exceptions retain their evidence for controlled review; this decision does not authorize automatic financial resolution or editing a frozen opening position.

## Worked example

| Evidence / result | After new payment | After valid reversal of that payment |
|---|---:|---:|
| Original ECF assessment | PHP 800 | PHP 800 |
| Frozen opening legacy settlement | PHP 200 | PHP 200 |
| Canonical net allocation after cutover | PHP 300 | PHP 0 |
| Cumulative settled projection | PHP 500 | PHP 200 |
| Outstanding | PHP 300 | PHP 600 |

The opening PHP 200 is not a new collection event. The PHP 300 payment and its attributable reversal remain traceable. Neither correction nor projection maintenance silently rewrites opening evidence or issued-document history.

## Consequences

### Positive

- Existing screens can continue reading compatible settlement fields while financial posting becomes canonical incrementally.
- New cash events and pre-cutover balance evidence remain distinguishable.
- A converted source has one coordinated settlement protocol across channels.

### Trade-offs / risks

- Every writer touching a converted source must participate before conversion is enabled; a Composer-only migration is insufficient.
- Source concurrency checks, canonical allocations, document posting, and compatibility updates must succeed or roll back together.
- Existing readers must state whether they show cumulative settlement, outstanding assessment, or cash events. Projected paid amounts must not be counted again as independent post-cutover receipts.

### Required follow-up

- N defines the settlement marker, posting/correction contract and atomic source-adapter participation, with no independent mutable duplicate balance.
- MASTER owns evidenced opening capture, complete writer/in-flight inventory, the Q43 reconciliation gate, scoped transition/authority contracts, writer guards, atomic activation/posting boundaries and exception/read behavior. Source activation remains a sequential MASTER phase, not a candidate-lane handoff.
- O reads authoritative remaining balances and submits payment amounts/allocations; it must not directly update cumulative paid/status fields for Canonical sources.
- Q separates legacy opening evidence from canonical cash events and handles attributable corrections without double counting.

## Compatibility and migration

Use additive persistence and explicit per-source conversion. Preserve Legacy behavior until that source is deliberately converted. Once Canonical, source settlement fields are writable only as projections through the canonical protocol, including from existing endpoints or background writers that remain available.

The Q43 controlled reconciliation gate above is mandatory before freezing and activation. Exact persistence and adapter contracts remain part of the final baseline. The gate does not establish detailed financial resolution rules for an unexpected late exception; that event stays preserved for controlled review.

## Verification

Future acceptance checks must cover frozen opening evidence, an explicit authority marker, one protocol across every settlement writer, atomic rollback of posting/projections, concurrent settlement rejection, and the payment/reversal example above. Reconciliation must prove that opening legacy settlement is never emitted as new canonical revenue and that legacy projections are not counted twice.

Q43 acceptance additionally covers auditable scoped Pending Cutover state, unrelated sources continuing normally, quiesced new legacy activity, drain/reconciliation of every listed channel, physical document checks, activation blocked on unmet readiness, the PHP 300/PHP 500 WCF opening example, and preserved late exceptions with unchanged ClientOperationId/document identity and permanently consumed issued forms.

Current evidence: `EEMOCantilanSDS.Domain/Entities/Payments/PaymentRecord.cs`, `EEMOCantilanSDS.Domain/Entities/Payments/UtilityBill.cs`, and `EEMOCantilanSDS.Application/Command/Sync/SyncOfflineCollections/SyncOfflineCollectionsCommandHandler.cs`. These are static observations; no implementation or runtime verification is claimed here.

## Supersedes / superseded by

Refines IA-001's per-source cutover prerequisite and resolves Q42. Supersedes any target interpretation that preserves independent legacy settlement writes after a source becomes Canonical. Does not change assessment rules or imply that existing sources have already converted.
