# Operational Functionalization V3 — Live Implementation Matrix (2026-09-30)

**Branch:** `interface-v3/claude-ui` (frontend + bounded backend pass; local commits only).
**Authority:** Clint / Core Brain task brief of 2026-09-30, recorded as [IA-049](../decisions/DECISION_REGISTRY.md).
**Not authorized here:** production deployment or migration, master merge, APK publication, historical backfill, source cutover.

An operation is **FUNCTIONAL** only when all of these exist: authoritative source, approved policy/rate, correct
instrument resolution, authorization, valid accountable-document custody, a writer, `ClientOperationId` idempotency,
offline behaviour (Mobile field work), canonical Collection/CollectionLine, Records visibility, report derivation, safe
corrections/reconciliation, and tests for the dangerous paths. Rendering a page is not functional.

States: FUNCTIONAL · FOUNDATION READY · IMPLEMENTING · BLOCKED POLICY · BLOCKED DOCUMENT · BLOCKED CUTOVER · LEGACY AUTHORITY · DEFERRED

## Shared foundations

| Foundation | State | Notes |
|---|---|---|
| Canonical Collection / CollectionLine / Allocation / PostingOperation | FOUNDATION READY (exists) | Used by ECF (Web OR) and WCF (Mobile CT). |
| Cash Ticket custody (receive, assign, consume, reconcile) | FUNCTIONAL | `AccountableFormCustodyWorkflow`; assignment was CT-only. |
| **OR custody** (assign to collector, collector-owned availability) | FUNCTIONAL | Assignment, collector-owned availability and Web custody screen are live. |
| **Governed-service / direct-amount source** | FUNCTIONAL | New tenant-owned definition + effective-dated rate + focused Mobile writer. |
| Canonical Monthly Income reader | FOUNDATION READY | Derives by classification, so new lines are recognized; production report cutover is **not** performed (BLOCKED CUTOVER, CB-06). |
| Collector Records / Reports include canonical operation collections | FUNCTIONAL | Server projection, not reconstructed from the device queue. |

## Operations matrix

| Operation | Source owner | Obligation basis | Payment cadence | Instrument | Amount source | Web role | Mobile role | Accountable document | Writer | Offline | Canonical line | Records | Reports | Status | Remaining blocker |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| NPM | NPM stall / month ledger | Monthly goal | Flexible (daily installments) | OR (legacy field; canonical pending) | Stall rate / effective rate | Accounts, monitoring | Daily round | Legacy OR field | Legacy specialized writers | Yes (legacy) | Not yet (Legacy authority) | Legacy | Legacy | LEGACY AUTHORITY | Scoped Q43 cutover |
| TCC / NCC / BBQ | `PaymentRecord` | Monthly | Flexible | OR | Stall monthly rate | Accounts, monitoring | Monthly collection | Legacy OR field | Legacy `RecordPayment` | Yes (legacy) | Adapter exists, source Legacy | Legacy | Legacy | LEGACY AUTHORITY | Scoped Q43 cutover |
| Ice Plant | `PaymentRecord` (ICE facility) | Monthly | Flexible | **Not stated** | Stall monthly rate | Facility page | Monthly collection | Legacy OR field | Legacy `RecordPayment` | Yes (legacy) | No dedicated classification yet | Legacy | Legacy | FUNCTIONAL (classification) | Own ICE_PLANT classification (OR); the existing monthly PaymentRecord rent source collects and cuts over under it, never Permanent Stall Rent. Effective amount stays the stall monthly rate |
| ECF | `UtilityBill` (electricity part) | Direct approved amount | Partial allowed | OR | Approved office amount | Web OR composer, accounts, report | None | Office OR (custody) | `EcfCollectionWorkflowFacade` / Composer | n/a (Web) | Yes | Yes (Web) | Derived | FUNCTIONAL | DirectApproved basis on the existing UtilityBill source (additive column, amount stored as one unit, no meter evidence); one assessment per stall-month, Web entry offers "Approved amount, no meter reading" |
| WCF | `UtilityBill` (water part) | Metered / approved | Partial allowed | CT | Effective water rate | Monitoring, reconciliation | Field collection | Assigned CT | `WcfCollectionWorkflow.PostMobileAsync` | Yes | Yes | Yes | Derived | FUNCTIONAL (per source) | CT + direct approved amount on the same UtilityBill source; the server quote carries the outstanding, Mobile never holds the amount. Each water row goes canonical through its own cutover (source-by-source, IA-051) |
| Tabo | TPM attendance | Per market day | Per day | OR (IA-045) | Effective vendor rate | Facility page | Vendor entry | Legacy OR field | Legacy | Yes (legacy) | TPM shadow only | Legacy | Legacy | LEGACY AUTHORITY | OR custody + cutover |
| Fish / Meat Vendor Fee | NPM Fish/Meat vendor context | Monthly goal (configured) | Flexible | OR | Effective configuration | Workspace (unavailable) | None | OR (planned) | **None** | — | None | — | — | FUNCTIONAL | ObligationAccount anchored to an NPM Fish/Meat stall (Payor from the linked occupancy), effective-configured monthly amount, installments of any size, own classification; NPM DailyFee history untouched |
| Weight & Measure | NPM weighing evidence | Quantity x rate | Per transaction | OR | Frozen rate x kilos | Read from NPM | Meat kilos (NPM round) | Legacy OR field | NPM daily writer | Yes | Shadow only | NPM | Shadow | FOUNDATION READY | Fish rate freeze (this pass); real adapter needs custody/cutover |
| Market Fees | Governed service | Per transaction | Per transaction | CT | Fixed or direct approved | Configuration, activity | Field collection | Assigned CT | Governed writer (this pass) | Yes | Yes | Yes | Derived | FUNCTIONAL | Head must record an amount rule |
| Landing / Berthing | Governed service | Per transaction | Per transaction | CT | Fixed or direct approved | Configuration, activity | Field collection | Assigned CT | Governed writer (this pass) | Yes | Yes | Yes | Derived | FUNCTIONAL | Head must record an amount rule |
| Transfer Large Cattle | Governed service | Per transaction | Occasional | OR | Direct approved | Configuration, activity | Field collection | Assigned OR | Governed writer (this pass) | Yes | Yes | Yes | Derived | FUNCTIONAL | Classification seed + amount rule |
| Vegetable / Fruit | Governed service (mode-aware) | Per transaction | Whole / daily | OR whole, CT daily | Fixed or direct approved | Configuration, activity | Field collection | Assigned OR or CT by mode | Governed writer (this pass) | Yes | Yes | Yes | Derived | FUNCTIONAL | Head must record an amount rule |
| Transportation / TRM | TRM trips | Per trip | Per trip | Legacy OR → target CT | Vehicle-class rates (IA-030) | Facility page | Trip entry | Legacy OR field | Legacy | Yes (legacy) | No | Legacy | Legacy | TARGET READY / BLOCKED ONLY UNTIL PRODUCTION ACTIVATION | Governed CT service with Head-defined vehicle classes and effective-dated rates; enabling it is the go-live boundary and closes the legacy trip writer; Mobile stays disabled until Mobile V3 (IA-051) |
| Kanmanggay | **None** | Monthly per space | Flexible | OR | Configured per-space rate | Workspace (unavailable) | — | OR | None | — | None | — | — | FUNCTIONAL | ObligationAccount (Space Rental account: Payor, space, active period, approved monthly amount, periods, installments) settled on an OR |
| Fiesta / Araw lot rental | Governed service (event context) | Per lot per event | Per event | OR | Fixed or direct approved | Configuration, activity | Field collection | Assigned OR | Governed writer if enabled | Yes | Yes | Yes | Derived | FUNCTIONAL | ObligationAccount lot rental (event, lot, Payor, approved amount, one event period) settled on an OR; event dates are not billing dates |
| Fines / Penalties | Penalty definitions | Per penalty | Per transaction | OR | Approved definition | Definitions + register | None | Office OR | None | — | Classification exists | — | — | FUNCTIONAL | Definitions + Web line on an OR |
| Slaughterhouse | SLH transactions | Per head | Per transaction | OR | Effective per-head rates | Facility page | Slaughter entry | Legacy OR field | Legacy | Yes (legacy) | No | Legacy | Legacy | LEGACY AUTHORITY | Custom animals now only from Head/Admin-approved definitions at their approved rate; itemized components shown; historical CustomRate untouched. Packages and add-ons beyond per-animal definitions wait for the slaughterhouse canonical source |

*Statuses are updated at the end of each slice; the final checkpoint response repeats them.*

## Final state (checkpoint)

- FUNCTIONAL: OR custody, governed-service source, collector records/report projection, Market Fees, Landing/Berthing, Transfer Large Cattle, Vegetable/Fruit, Fines/Penalties.
- FOUNDATION READY: ECF direct amount, Weight & Measure (frozen Fish evidence, forward-safe only), Monthly Income reader.
- (Superseded by IA-050: no operation is BLOCKED POLICY. Gates left: TRM cutover date, CB-06.)
- DEFERRED: Fiesta/Araw lot rental. LEGACY AUTHORITY: Slaughterhouse (itemized display only), Tabo.
- BLOCKED CUTOVER: canonical Monthly Income and source cutover (CB-06); nothing in this pass performs a cutover, migration, deployment or backfill.

## Update after IA-050 (grill-me answers)

Remaining gates: **TRM** (target CT confirmed, cutover date not authorized) and **CB-06** (Monthly Income cutover not authorized). No operation is BLOCKED POLICY any more.
Implemented in this slice: IA-050 recorded; `ICE_PLANT`, `KANMANGGAY_SPACE_RENTAL`, `FIESTA_ARAW_LOT_RENTAL` classifications seeded as OR.
Not yet built (each needs domain + additive migration + workflow + UI + tests, and is left as DEFERRED rather than shipped half-finished): Fish/Meat Vendor Fee obligation source, Ice Plant and Kanmanggay monthly-obligation sources, Event Lot Rental, Slaughterhouse approved definitions, ECF direct-amount reconciliation of Web/Mobile capture, and the coverage-based collector total (needs a per-source authority map).

## Final operational completion (IA-050 / IA-051)

- **Gates:** none remain as policy gates. CB-06 is authorized prospectively at approved production go-live, source by source with exactly-once coverage; Transportation is TARGET READY, blocked only until production activation. Nothing here deploys, migrates production or backfills.
- **Source authority:** `CollectionSourceAuthorityMap` decides, per source kind, whether legacy or canonical money is authoritative (legacy until a row's cutover; canonical always for governed, penalty and obligation sources; legacy only where no canonical writer exists). The collector report, canonical Monthly Income sources and an exhaustive test use it.
- **Known limits, stated plainly:** (1) the collector report's new canonical rent/ECF/obligation/penalty lines are exercised for SQL translation but not asserted with collector-owned canonical data; (2) the official mixed-period Monthly Income that sums legacy and canonical by classification is not built - the canonical reader stays canonical-only and reports per-source authority so a mixed report can be assembled without double counting; (3) Slaughterhouse packages and add-ons; (4) Mobile screens for obligations-free operations, vehicle classes and approved slaughter animals belong to the Mobile V3 pass; (5) WCF/ECF direct amounts are entered per bill by the Head with the previous approved amount carried forward; no separate configured default rate key exists.

## Update after the accountability and reporting pass (IA-052)

- The mixed-period official Monthly Income now exists (`GetOfficialMonthlyIncomeQueryHandler`); the earlier "not built" limit is closed. Remaining reporting limits are listed in [ACCOUNTABILITY_REMITTANCE_FINANCIAL_REPORTING_V3_20260930.md](ACCOUNTABILITY_REMITTANCE_FINANCIAL_REPORTING_V3_20260930.md).
- Remittance no longer waits for CT exhaustion; form custody and cash remittance are separate ledgers.
