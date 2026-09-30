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
| Ice Plant | `PaymentRecord` (ICE facility) | Monthly | Flexible | **Not stated** | Stall monthly rate | Facility page | Monthly collection | Legacy OR field | Legacy `RecordPayment` | Yes (legacy) | No dedicated classification yet | Legacy | Legacy | DEFERRED | Policy resolved (IA-050): OR, ICE_PLANT classification seeded. Monthly-obligation source not built; legacy PaymentRecord stays authoritative |
| ECF | `UtilityBill` (electricity part) | Direct approved amount | Partial allowed | OR | Approved office amount | Web OR composer, accounts, report | None | Office OR (custody) | `EcfCollectionWorkflowFacade` / Composer | n/a (Web) | Yes | Yes (Web) | Derived | FOUNDATION READY | Source still Legacy; OR custody assignment (this pass) |
| WCF | `UtilityBill` (water part) | Metered / approved | Partial allowed | CT | Effective water rate | Monitoring, reconciliation | Field collection | Assigned CT | `WcfCollectionWorkflow.PostMobileAsync` | Yes | Yes | Yes | Derived | BLOCKED CUTOVER | Rate-unit question resolved: CT + direct approved amount from policy, no meter assumption; water cutover still gated |
| Tabo | TPM attendance | Per market day | Per day | OR (IA-045) | Effective vendor rate | Facility page | Vendor entry | Legacy OR field | Legacy | Yes (legacy) | TPM shadow only | Legacy | Legacy | LEGACY AUTHORITY | OR custody + cutover |
| Fish / Meat Vendor Fee | NPM Fish/Meat vendor context | Monthly goal (configured) | Flexible | OR | Effective configuration | Workspace (unavailable) | None | OR (planned) | **None** | — | None | — | — | DEFERRED | Policy resolved (IA-050): separate obligation, never NPM DailyFee reclassified. Classification exists; prospective Vendor Fee obligation source not built |
| Weight & Measure | NPM weighing evidence | Quantity x rate | Per transaction | OR | Frozen rate x kilos | Read from NPM | Meat kilos (NPM round) | Legacy OR field | NPM daily writer | Yes | Shadow only | NPM | Shadow | FOUNDATION READY | Fish rate freeze (this pass); real adapter needs custody/cutover |
| Market Fees | Governed service | Per transaction | Per transaction | CT | Fixed or direct approved | Configuration, activity | Field collection | Assigned CT | Governed writer (this pass) | Yes | Yes | Yes | Derived | FUNCTIONAL | Head must record an amount rule |
| Landing / Berthing | Governed service | Per transaction | Per transaction | CT | Fixed or direct approved | Configuration, activity | Field collection | Assigned CT | Governed writer (this pass) | Yes | Yes | Yes | Derived | FUNCTIONAL | Head must record an amount rule |
| Transfer Large Cattle | Governed service | Per transaction | Occasional | OR | Direct approved | Configuration, activity | Field collection | Assigned OR | Governed writer (this pass) | Yes | Yes | Yes | Derived | FUNCTIONAL | Classification seed + amount rule |
| Vegetable / Fruit | Governed service (mode-aware) | Per transaction | Whole / daily | OR whole, CT daily | Fixed or direct approved | Configuration, activity | Field collection | Assigned OR or CT by mode | Governed writer (this pass) | Yes | Yes | Yes | Derived | FUNCTIONAL | Head must record an amount rule |
| Transportation / TRM | TRM trips | Per trip | Per trip | Legacy OR → target CT | Vehicle-class rates (IA-030) | Facility page | Trip entry | Legacy OR field | Legacy | Yes (legacy) | No | Legacy | Legacy | BLOCKED CUTOVER | TARGET CONFIRMED (CT); cutover date not authorized (IA-050) |
| Kanmanggay | **None** | Monthly per space | Flexible | OR | Configured per-space rate | Workspace (unavailable) | — | OR | None | — | None | — | — | DEFERRED | Policy resolved (IA-050): Space Rental account, OR. Classification seeded; source not built |
| Fiesta / Araw lot rental | Governed service (event context) | Per lot per event | Per event | OR | Fixed or direct approved | Configuration, activity | Field collection | Assigned OR | Governed writer if enabled | Yes | Yes | Yes | Derived | DEFERRED | Policy resolved (IA-050): EventLotRental, OR. Classification seeded; source not built |
| Fines / Penalties | Penalty definitions | Per penalty | Per transaction | OR | Approved definition | Definitions + register | None | Office OR | None | — | Classification exists | — | — | FUNCTIONAL | Definitions + Web line on an OR |
| Slaughterhouse | SLH transactions | Per head | Per transaction | OR | Effective per-head rates | Facility page | Slaughter entry | Legacy OR field | Legacy | Yes (legacy) | No | Legacy | Legacy | LEGACY AUTHORITY | Target = approved definitions/packages/add-ons (IA-050); CustomRate path retained for history only until definitions are built |

*Statuses are updated at the end of each slice; the final checkpoint response repeats them.*

## Final state (checkpoint)

- FUNCTIONAL: OR custody, governed-service source, collector records/report projection, Market Fees, Landing/Berthing, Transfer Large Cattle, Vegetable/Fruit, Fines/Penalties.
- FOUNDATION READY: ECF direct amount, Weight & Measure (frozen Fish evidence, forward-safe only), Monthly Income reader.
- BLOCKED POLICY: Fish/Meat Vendor Fee, Ice Plant, Transportation CT date, Kanmanggay, WCF rate unit.
- DEFERRED: Fiesta/Araw lot rental. LEGACY AUTHORITY: Slaughterhouse (itemized display only), Tabo.
- BLOCKED CUTOVER: canonical Monthly Income and source cutover (CB-06); nothing in this pass performs a cutover, migration, deployment or backfill.

## Update after IA-050 (grill-me answers)

Remaining gates: **TRM** (target CT confirmed, cutover date not authorized) and **CB-06** (Monthly Income cutover not authorized). No operation is BLOCKED POLICY any more.
Implemented in this slice: IA-050 recorded; `ICE_PLANT`, `KANMANGGAY_SPACE_RENTAL`, `FIESTA_ARAW_LOT_RENTAL` classifications seeded as OR.
Not yet built (each needs domain + additive migration + workflow + UI + tests, and is left as DEFERRED rather than shipped half-finished): Fish/Meat Vendor Fee obligation source, Ice Plant and Kanmanggay monthly-obligation sources, Event Lot Rental, Slaughterhouse approved definitions, ECF direct-amount reconciliation of Web/Mobile capture, and the coverage-based collector total (needs a per-source authority map).
