# StallTrack V3 - Accountability, Remittance and Financial Reporting (2026-09-30)

Status of the program that closes: accountable-form custody, collections, remittance and liquidation, the Report of Collections,
the official Monthly Income and management reporting. Decisions: IA-050, IA-051, IA-052 in
[DECISION_REGISTRY.md](../decisions/DECISION_REGISTRY.md). Nothing here is deployed, migrated in production, backfilled or cut over.

## The four ledgers (never collapsed)

| Ledger | Owns | Never |
|---|---|---|
| Accountable-form | Physical OR/CT stock and custody: In Office, Assigned, Issued/Consumed, Spoiled/Cancelled, Returned, Needs Review | Money |
| Collection | Money received: Collection, CollectionLine, Allocation, PostingOperation | Remittance |
| Remittance | Money already collected and turned over, covering whole Collections exactly once | Revenue |
| Reporting projection | RCD/Collections register, Monthly Income, collector totals, accountability | A writer |

Form-unit states vs financial disposition: `Voided` (document state) means a BLANK form spoiled or cancelled before any financial
use (recorded with reason/actor/time in `AccountableFormSpoilage`); an issued document never voids, it keeps linked correction
history. `Returned` is a custody event (the assignment closes; the document is In Office again), never consumption. No enum was
rewritten and no historical state changed.

## Live matrix

| Capability | Current authority | Writer | Reader | UI | Exactly-once protection | Production readiness | Remaining blocker |
|---|---|---|---|---|---|---|---|
| CT inventory | Accountable-form ledger | `AccountableFormCustodyWorkflow` | Forms register, Accountability report | `/accountable-forms` | Unique document identity, state machine | Ready | None |
| OR inventory | Accountable-form ledger | Same (OR route) | Same | Same | Same | Ready | None |
| Assignment | Assignment rows | Custody workflow | Forms page, position | Assign dialog | Only In Office units assign; one open assignment | Ready | None |
| Consumption | Document + PostingOperation | Canonical coordinator | Position, trace | (Composer/Mobile) | Consume only from Assigned/InOffice; idempotent | Ready | Mobile V3 screens |
| Spoiled/cancelled blank | `AccountableFormSpoilage` + `Voided` | `SpoilAsync` | Position, trace | Record spoiled form | Blank only; unique per document; never stock | Ready | None |
| Return unused | Assignment `ReturnedAtUtc` | `ReturnUnusedAsync` | Position | Return unused | Assigned+unused only; consumed/spoiled refused | Ready | Concurrent return-vs-issue relies on row concurrency (tested by state refusal, not a race test) |
| Reconciliation | `ReconciliationRequired` | Posting rejection paths | Position (Needs review) | Forms page | Quarantine, never stock | Ready | Formal resolution workflow beyond sync relink is out of scope |
| Collections / lines | Canonical | Coordinator | Register, Activity | Reports > Collections | PostingOperation fingerprint | Ready for activated sources | Source activation is per source at go-live |
| Remittance | `CollectionRemittance` + coverage | `RemittanceWorkflow` | Register, detail, position | `/accountable-forms/remittances` | Partial unique index on active coverage; ClientOperationId + fingerprint; concurrent test | Ready | Legacy-authoritative collections cannot be covered (stated apart); Treasury handoff is intentionally absent |
| RCD / Report of Collections | Derived from posted collections | none | `CollectionsReportWorkflow` | Reports > Collections | Same rows for register and summary | Ready | Signed-RCD certification not modeled |
| Collector report | Legacy (until row cutover) + canonical | none | `CollectorReportQueries` | Collector report | Authority map excludes converted rows | Ready | Collector-total algorithm is the single one used by remittance position |
| Collection Activity | Canonical only | none | Composer activity | Current Collection | One entry per Collection | Ready | Legacy events are not listed by design |
| Monthly Income | Legacy pre-cutover + canonical post-cutover | none | `GetOfficialMonthlyIncomeQueryHandler` | Reports > Monthly Income | Legacy excludes converted rows; remittance never read; mutation-checked test | Ready to compare | Slaughterhouse/BBQ placement and Arrears presentation unresolved; legacy Fish weighing uses current rate for old rows |
| Annual targets | None configured | none | Row DTO has target/attainment fields | Shown as not configured | n/a | Gap | Target source/governance not decided; nothing invented |
| Receivables | Legacy follow-up (mature) | none | Financial report | Reports > Receivables | n/a (obligations, not cash) | Ready | Kept apart from unremitted cash |
| Financial Summary document | Legacy facility report | none | `FinancialSummaryDocument` | Export Summary PDF | n/a | Unchanged | Still facility-centric; a Monthly Income document replaces it only when approved |

## Design notes

- **Remittance never creates revenue.** It writes only `CollectionRemittances` and `CollectionRemittanceCoverages`. A test proves the
  Collection and CollectionLine counts and the official Monthly Income are unchanged after a remittance.
- **Coverage is by whole canonical Collection**, derived (never retyped), net of corrections at recording time. A correction recorded
  after a remittance flags it for review; nothing is adjusted silently. A shortfall is a visible difference; an excess is refused.
- **Legacy-authoritative collections** have no immutable per-event identity (a PaymentRecord accumulates payments), so they cannot be
  covered exactly and are reported as "outside remittance coverage" per collector. They become coverable when their source goes
  canonical at go-live. Nothing is manufactured for history.
- **Monthly Income authority:** `CollectionSourceAuthorityMap` decides the representation. The legacy reader excludes converted rows;
  the canonical side is the Collection line net of corrections, stall rent read at allocation level to place it by facility.
- **Print/export:** the Monthly Income, Collections and Accountability tabs print their own report with the tenant letterhead; the
  remittance workspace and detail print a summary. None claims to replace a prescribed government form.

## Known gaps (real)

1. Annual targets: no approved source; target/attainment show as not configured.
2. Official placement of Slaughterhouse, BBQ rent and other rental facilities, and Arrears presentation, stay in a "pending" group.
3. Legacy Fish weighing without frozen evidence is priced at the current Fish rate in the legacy reader.
4. Legacy utility payments have no per-payment date, so the month is the part's paid-at (or last update).
5. The Financial Summary PDF document is still facility-centric.
6. No signed-RCD certification or Treasury deposit step (intentionally not invented).
7. Mobile V3 must consume the stable contracts below; no Mobile screen changed in this pass.

## Contracts stable for Mobile V3

Accountable-form positions per collector and instrument (`FormAccountabilityDto`), the collector position and remittance status
(`CollectorPositionDto`), approved slaughter animals, vehicle classes/rates in governed terms, obligation-free governed operations
and the governed sync payload including `VehicleClassCode`.
