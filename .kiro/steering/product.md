# Product Overview

## StallTrack — EEMO Revenue Collection System

**Product:** StallTrack, a multi-tenant revenue collection platform for LGU-managed economic enterprises.
**Reference tenant:** Economic Enterprise & Management Office (EEMO), Municipality of Cantilan, Surigao del Sur.
**Status:** In production — web portal, API and Android collector app are live; further LGUs onboard through the
platform operator console.

## What it does

Digitises fee collection, payment tracking, delinquency monitoring and reporting across a municipality's
facilities, for both office staff and field collectors.

Each municipality is a **tenant**: its own facilities, rates, users, branding and data, isolated inside one
database and one deployment. **Cantilan is the accuracy baseline** — a change made for another LGU must never
move a Cantilan figure.

## Facilities (eight canonical codes, plus per-LGU custom facilities)

| Code | Cantilan name | Billing |
|------|---------------|---------|
| NPM | New Public Market | Daily per stall (+ fish per kilo, electricity, water billed separately) |
| TCC | Tampak Commercial Center | Monthly rental |
| NCC | New Commercial Center | Monthly rental |
| BBQ | Barbecue Stand | Monthly space rental |
| ICE | Iceplant | Monthly space rental |
| SLH | Slaughterhouse | Per head, by animal type |
| TRM | Transport Terminal | Per trip, with queue order |
| TPM | Tabo-an Public Market | Per vendor per market day (weekly; the day is per-LGU) |

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
- **Admin** — records, OR entry, reports. No account management, no audit trail.
- **Collector** — mobile only, limited to assigned facilities.
- **Payor** — public portal for their own stall's dues and online payment.

## Business rules

- Web portal is admin-only; collectors authenticate in the mobile app.
- `CollectorId` comes from the authenticated user, never the request body; admin entries leave it null.
- OR numbers are entered by hand, never generated; adding one never rewrites the original collector or timestamp.
- Delinquent = 3+ unpaid months in a rolling 12-month window; 1–2 = arrears. Contract expiry warns within 3 months.
- A **partial payment counts as unpaid** for the paid-vs-unpaid invariant, and is reported separately as partial.
- NPM is never billed monthly: `RecordPayment` refuses it; daily collections and month settlement are the routes.
- Rosters list current holders only; monetary totals count active stalls only.
- Business-day logic uses `PhilippineTime` (UTC+8); stored timestamps stay UTC. Mobile uses device-local time.
- Two-factor is opt-in for all and **mandatory for Heads**, enforced after sign-in so nobody can be locked out.
- Account lockout: 5 failed attempts = 15 minutes. Access token 15 min, refresh 7 days, hashed and revoked on logout.
- Every financial mutation is audited with actor, timestamp and before/after values.
- Field writes carry a client operation id, so a retry on a weak connection cannot double-record.
