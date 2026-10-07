# Product Overview

## StallTrack — MEEDO Revenue Collection System

**Product:** StallTrack, a multi-tenant revenue collection platform for LGU-managed economic enterprises.
**Reference tenant:** Municipal Economic Enterprises Development Office (MEEDO), Municipality of Cantilan, Surigao del Sur.
**Status:** In production — web portal, API and Android collector app are live; further LGUs onboard through the
platform operator console.

**Current business-direction note (2026-10-06):** production may still expose older transport/payor/Fish-Meat behavior while the next compatibility-safe refactor is implemented. The authoritative target is in IA-067, IA-068, ADR-007 and `../planning/OFFICE_CLARIFICATION_REFACTOR_20261006.md`.

## What it does

Digitises fee collection, payment tracking, delinquency monitoring and reporting across a municipality's
facilities, for both office staff and field collectors.

Each municipality is a **tenant**: its own facilities, rates, users, branding and data, isolated inside one
database and one deployment. **Cantilan is the accuracy baseline** — a change made for another LGU must never
move a Cantilan figure.

## Facilities (eight canonical codes, plus per-LGU custom facilities)

| Code | Cantilan name | Billing |
|------|---------------|---------|
| NPM | New Public Market | Daily per stall; utilities remain separate. Historical Fish-kilo coupling is superseded prospectively by IA-068. |
| TCC | Tampak Commercial Center | Monthly rental |
| NCC | New Commercial Center | Monthly rental |
| BBQ | Barbecue Stand | Monthly space rental |
| ICE | Iceplant | Monthly space rental |
| SLH | Slaughterhouse | Per head, by animal type |
| TRM | Legacy Transport Terminal compatibility code | Historical trip/transport evidence; target Terminal is separate from Transportation/Parking |
| TPM | Tabo-an Public Market | Per vendor per market day (weekly; the day is per-LGU) |

## 2026-10-06 clarified source model

- **Income From Terminal** is its own Cash Ticket operation/report family: Comfort Room; Pull Pul Vans, Cargo Vans; Tricycad. Direct aggregate amount is valid; Cash Ticket count is optional.
- Vehicle-class/rate assistance belongs prospectively to Terminal. Jeepney, Multicab, Van, Public Utility Bus and Public Utility Baby Bus map to Pull Pul Vans/Cargo Vans; Tricycle maps to Tricycad.
- **Transportation/Parking** is a separate direct-amount CT source with no required rate/class basis.
- **Fish/Meat Vendor Fee** uses an independent Fish-or-Meat registration context and direct amount received; it is not an NPM charge and does not require a Business Payor.
- **Weight & Measure** is separate and requires a registered Fish/Meat vendor.
- **Business Payors** is retired as a product workflow. New Collection searches source-native identities and shows only eligible items.
- **NPM Daily Collect All** is approved only for today's daily charge, with one Collection/SRC per selected stall.
- Official Monthly Income now includes A. Income From Market, B. Income From Terminal, C. Income from Slaughterhouse, OVERALL TOTAL MARKET COLLECTION, and configurable signatories.

## Rates are data, not constants

`FeeRates` holds Cantilan's ordinance figures as a **fallback only**. Each LGU sets its own amounts with an
effective date in `FacilityRates`.

- Resolve through `IFeeRateResolver.GetSnapshotAsync()` → `snapshot.Resolve(FeeRateKey.X, asOf)`.
- A stall's daily fee comes from `Stall.ResolveDailyFee(resolvedRate)` — custom NPM sections keep their own
  rate, canonical sections use the tenant's.
- A daily-billed facility's "monthly" figure is `ResolveDailyFee(...) * DomainRules.DailyBilledMonthDays`
  (flat 30), never the stored `Stall.MonthlyRate`.

## Roles

- **Platform operator** — onboards LGUs (assess → validate → activate), issues the Head account, can clear a
  Head's second factor. Cross-tenant reach requires the `IsPlatformOperator` flag.
- **Head (SuperAdmin)** — everything within their own LGU. May act on Admins and themselves, never on a peer Head.
- **Admin** — records, reports (OR entry only on legacy-authoritative sources; canonical collections carry an SRC). No account management, no audit trail.
- **Collector** — mobile only, limited to assigned/available facilities and operations under server-owned capability rules.
- **Payor** — public portal for their own stall's dues and online payment.

## Business rules

- Web portal is admin-only; collectors authenticate in the mobile app.
- `CollectorId` comes from the authenticated user, never the request body; admin entries leave it null.
- MEEDO's current Official Receipt is physical Accountable Form No. 51. Record the exact printed identifier rather than generating it; one physical OR may itemize several compatible charges for the same payor while each collection line keeps its own revenue meaning. Adding an OR never rewrites the original collector or timestamp.
- Delinquent begins at one fully elapsed unpaid month. Arrears means qualifying old/lapsed debt; its exact qualification boundary remains unresolved and is not inferred from month count. See `../business/REVENUE_ARCHITECTURE.md`. Contract expiry warns within 3 months.
- A **partial payment counts as unpaid** for the paid-vs-unpaid invariant, and is reported separately as partial.
- NPM does not use the generic monthly-rental writer: `RecordPayment` refuses it. Daily collections and month settlement remain specialized; under `RentGoal` daily payments settle the fixed monthly obligation, while `PureDays` is calendar-day-derived.
- Rosters list current holders only; monetary totals count active stalls only.
- Business-day logic uses `PhilippineTime` (UTC+8); stored timestamps stay UTC. Collector screens prefer the
  server-issued session business date and use the device-local date only as fallback.
- Two-factor is currently optional. A Head receives one dismissible reminder; once enabled, MFA is required at sign-in.
- Account lockout: 5 failed attempts = 15 minutes. Access token 15 min, refresh 7 days, hashed and revoked on logout.
- Every financial mutation is audited with actor, timestamp and before/after values.
- Field writes carry a client operation id, so a retry on a weak connection cannot double-record.
- Source-native search never merges equal names automatically. A typed payer name on a permitted direct/one-off collection is historical snapshot evidence, not a permanent cross-source identity.
- Official Monthly Income report adjustments are Head-only, audited report revisions; they never edit posted Collections or remittance.
