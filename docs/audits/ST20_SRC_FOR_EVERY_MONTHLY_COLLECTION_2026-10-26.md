# ST-20 follow-up: a genuine SRC for every new monthly collection (2026-10-26)

Requirement: every successfully recorded collection keeps a genuine StallTrack Reference Code (SRC), whether or not an optional OR number is typed. SRCs come only from committed canonical Collections; none is invented, backfilled or attached to a historical legacy payment.

## Audit: where a monthly collection is recorded today

| Path | Writes | SRC |
|---|---|---|
| Mobile New Collection (`CollectionSessionWorkflow`, offline kind `ItemizedCollectionSession`) | canonical Collection | yes, for rent only on a **Canonical** `PaymentRecord` row |
| Mobile monthly operation page (ICE/BBQ/TCC/NCC/custom), `RentCollection` (offline kind 10), `PostMobileRentAsync` | canonical Collection | yes; the page already routes a row with `RentCanonical=true` here (`MonthlyCollection.razor`) |
| Mobile monthly operation page, legacy row (`monthly/collections/record` -> `RecordPaymentCommand`; offline kind `MonthlyRental`) | legacy `PaymentRecord` status | **no SRC** (legacy authority) |
| Mobile Collect arrears (`ArrearsMonths`) | same legacy `RecordPaymentCommand` | no SRC for Legacy rows; a Canonical row is `CollectVia=Canonical` and goes to New Collection |
| Head Portal facility pages ("record payment") | `RecordPaymentCommand` | no SRC for Legacy rows; a non-Legacy row is refused |
| Office rent receipt (`PostOfficeRentAsync`) | canonical | yes, Canonical row only, behind `MonthlyRentReceiptOptions` (off) |
| Online payment (ST-17 `OnlineRentCanonicalPoster`) | canonical | yes, Canonical row only |

So the only way a monthly collection lacks an SRC is that its row is still **Legacy** settlement authority. A row becomes Canonical only through the governed per-row cutover (`SettlementCutoverWorkflow`: Legacy -> Pending Cutover -> frozen opening position -> Canonical). The cutover itself was blocked for every PaymentRecord with an assigned collector (`MobileWriterStatus`: no durable late-payload reconciliation). That is what this change removes, safely.

## What changed (all additive; nothing activates by itself)

1. **`LegacyRentPayloadReconciliation`** (the Water adapter's counterpart for rent). An older cumulative monthly-rent payload (Mobile offline replay or a stale Web screen) that reaches a row that is no longer Legacy is not applied and not dropped: it is preserved as one `ReconciliationRequired` `PostingOperation` (`OLD_MOBILE_PAYLOAD_AFTER_CUTOVER`, bound to its ClientOperationId, idempotent, same id with a different payload = conflict, no Collection/SRC/balance). Wired into `RecordPaymentCommandHandler`'s existing "not Legacy" refusal; the refusal text and the device `ReconciliationRequired` status are unchanged.
2. **Cutover readiness for PaymentRecord** no longer carries the standing "affected collectors remain blocked" blocker (the ECF treatment): the canonical Mobile writer and the durable late-payload adapter now both exist. The per-collector evidence (device version, drained legacy queue) and every other blocker (legacy cash, online payments in flight, unresolved reconciliation operations, missing policy, evidence manifest) still gate each row.
3. **`RentActivationWorkflow` + `POST api/rent-activations/plan` / `POST api/rent-activations`** (SuperAdmin/Admin). One facility-month at a time, **prospective**: a *fresh* row is one with no row yet or an Unpaid, zero-cash, no-OR, no mixed-component Legacy row. The plan writes nothing and names every row (fresh, already canonical, carries legacy evidence, excused, not active, no approved rate). Activation is bound to the reviewed plan fingerprint and the attested checklist; phase 1 (one transaction) creates the missing Unpaid rows and proves every row ready, rolling back everything on any blocker; phase 2 runs each row through the existing Begin -> Freeze -> Activate commands (own serializable transaction and version check each), resumable. NPM (own month activation), non-monthly facilities and months that have not begun are refused. It creates no Collection, no SRC and no revenue: the first SRC comes from the first real collection afterwards.

Never touched: any row with a payment, partial, OR number or mixed components (it stays Legacy, history and balances untouched); any Canonical row; historical SRCs; corrections; tenant isolation (every query is tenant-scoped; the cutover workflow re-checks the tenant).

## Display of the persisted SRC

Collection Activity, Mobile Records, Remittance, Collections Register and Monthly Income read canonical Collections' `ReferenceCode`; a Legacy row's money is shown from its legacy record and is counted only while its row is Legacy (`CollectionSourceAuthorityMap.LegacyMoneyCounts`), so a row never double-counts across the cutover. Existing ST-17 / office-rent cross-channel tests cover this; this change adds no new display path. A typed OR is shown as the typed number (separate from the SRC).

## Verified

PostgreSQL (`EcfCollectionWorkflowTests.RentActivation`, 4): plan names the fresh row, leaves legacy-cash rows alone and writes nothing; activation of a fresh month makes it Canonical with a frozen opening of 1,000 / 0 / 1,000, creates no Collection, leaves the legacy partial month untouched; the first Mobile collection returns a genuine `SRC-yyyy-nnnnnn` with exactly one Collection; a late legacy payload is preserved once (idempotent, a changed payload conflicts) and applies nothing; a re-run is refused; missing collector evidence refuses and rolls back even the month row; a stale plan is refused; a Collector, the Public Market and a future month are refused; another tenant sees none of it.

## Consequential step left for separate authorization

Nothing has been activated. To get SRCs for a facility's new collections in production, per tenant and facility and per month (or each month going forward): (1) full backup and restore drill; (2) every assigned collector on a current Mobile build with the legacy monthly queue drained, attested in the checklist; (3) review `plan`, check the `Ready` flag and the excluded rows; (4) activate in a maintenance window; (5) confirm one real collection returns one SRC and Collection Activity, Monthly Income and Financial Summary agree; (6) stop on any reconciliation-required item. Months are activated at the start of each month; an un-activated month keeps collecting on the legacy path (no SRC) until it is. A scheduler or "activate automatically for new months" policy is a further decision not taken here.

## Not done / risks

- No Head UI for the plan/activate routes (API only); Mobile shows `RentCanonical` rows through the existing canonical path.
- Legacy rows with cash keep their legacy path permanently unless the office rules otherwise (the cutover blocks them by design).
- Physical OR numbers typed on a late legacy payload are kept as evidence in the intent; no accountable document is quarantined (unlike Water), because rent ORs are optional unverified references.
