# StallTrack — EEMO Revenue Collection System

**Product:** StallTrack (multi-tenant LGU revenue collection platform)
**First and reference tenant:** Economic Enterprise & Management Office (EEMO), Municipality of Cantilan, Surigao del Sur
**Status:** In production. Web portal, API and collector app are all live; further LGUs onboard through the platform console.

> This file is the source of truth for BUSINESS behaviour. For layering and coding rules see `arch-rules.md`,
> for the reasons behind the design see `ARCHITECTURE_DOCUMENTATION.md`, and for code shapes to copy see
> `patterns.md`. Where they disagree, `arch-rules.md` wins.

---

## 1. What the system does

Replaces a paper-based revenue collection process for LGU-managed economic enterprises. Staff record
collections, the system tracks who owes what, and reports come out without manual computation.

It is no longer a single-office tool. StallTrack is a **multi-tenant platform**: each municipality is a
tenant with its own facilities, rates, users, branding and data, all in one database and one deployment,
isolated per tenant. Cantilan is the default tenant and the accuracy baseline — its figures must never change
when a feature is added for another LGU.

---

## 2. Facilities and how each one is billed

Eight canonical facility codes, plus per-LGU custom facilities.

| Code | Facility (Cantilan name) | Billing model |
|------|--------------------------|---------------|
| NPM | New Public Market | **Daily** per stall, marked on a calendar; Fish section also charges per kilo; electricity and water billed separately |
| TCC | Tampak Commercial Center | Monthly rental per contract |
| NCC | New Commercial Center | Monthly rental per contract (Extension / Corner classifications) |
| BBQ | Barbecue Stand | Monthly space rental |
| ICE | Iceplant | Monthly space rental |
| SLH | Slaughterhouse | Per head, by animal type |
| TRM | Transport Terminal | Per trip, with queue/dispatch order |
| TPM | Tabo-an Public Market | Per vendor per market day (a weekly market; the market DAY is per-LGU configurable) |

**Custom facilities.** A Head can add facilities beyond the eight (`FacilityCode.Custom1..5`), which bill as
monthly rental. NPM also supports **custom sections** beyond Vegetable / Meat / Fish; a custom section carries
its own daily rate.

### Rates are per-tenant data, not constants

`FeeRates` holds the Cantilan ordinance figures (NPM ₱30/day, fish ₱1/kg, SLH ₱250 hog and ₱365 large animal,
TRM ₱30/trip, TPM ₱100/vendor) and they remain the **fallback**. Each LGU may set its own amounts, effective
from a date, in the `FacilityRates` table.

- Always resolve through `IFeeRateResolver.GetSnapshotAsync()` then `snapshot.Resolve(FeeRateKey.X, asOf)`.
- Never read `FeeRates.*` directly in a handler or repository to bill or report.
- A snapshot is resolved **as of a date**, so a mid-month rate change bills correctly.
- For a stall's daily fee use `Stall.ResolveDailyFee(ordinanceRate)`: a **custom-section** stall uses its own
  stored `DailyRate`; every canonical stall uses the tenant's resolved rate. This one rule keeps billing,
  settlement and the rosters in agreement.

### The daily-billed month convention

A daily-collected facility has no monthly contract rate. Where a document must state one (the stallholder
roster, the closed-accounts register), it is the monthly **equivalent**: resolved daily rate ×
`DomainRules.DailyBilledMonthDays` (a flat 30). Cantilan: ₱30 × 30 = ₱900 → ₱10,800 a year. The stored
`Stall.MonthlyRate` is a hand-entered figure and must NOT be used for a daily-billed facility's reports.

---

## 3. Core business rules

- **Delinquency:** 3+ unpaid months inside a rolling 12-month window = delinquent; 1–2 = arrears.
- **Contract expiry:** flagged when within 3 months; an expired contract drops off the current-holder roster
  (it stays in history) and can be renewed, which starts a fresh term at the stall's current rate.
- **Payment status:** Paid / Partial / Unpaid per stall per period. A **partial counts as unpaid** for the
  paid-vs-unpaid invariant (Paid + Unpaid == Billable) and is additionally surfaced as a partial count.
- **OR numbers** are entered by hand from the physical receipt book, never generated. Adding an OR number
  never rewrites the original collector or timestamp. OR uniqueness is enforced per tenant.
- **NPM is never billed monthly.** `RecordPayment` refuses an NPM stall — daily collections and the
  month-settlement service are the only routes — and online payment routes NPM to a daily-derived path.
- **Collector attribution:** `CollectorId` is taken from the authenticated user, never from the request body.
  An admin-recorded entry leaves it null and is attributed through the audit fields instead.
- **Business-day logic** (today, current month, expiry, streaks, trip day, market day) uses
  `PhilippineTime` (UTC+8). Stored timestamps stay UTC. The mobile app is the exception: it uses device-local
  time for the collector's own day.
- **Excused / absent days** exist for NPM (a vendor who did not trade) and are excluded from arrears.
- **Audit trail:** every financial mutation is written to `AuditLog` with actor, timestamp and before/after
  values, independently of the editable created/updated fields.

---

## 4. Roles and access

| Role | Where | What it can do |
|------|-------|----------------|
| **Platform operator** | Operator console (separate Angular app) + portal | Assess, validate and activate LGUs; issue a Head account; clear a Head's second factor |
| **Head** (SuperAdmin) | Web portal | Everything inside their own LGU: accounts, facilities, rates, records, reports, audit trail, backups |
| **Admin** | Web portal | Records, OR entry, reports. No account management, no audit trail |
| **Collector** | Collector app (Android) | Field collection for their assigned facilities only |
| **Payor** | Payor portal (public) | Their own stall's dues and online payment |

Rules that matter:

- The web portal is **admin-only**; collectors authenticate in the mobile app.
- A Head may act on their own account and on Admins, **never on another Head** (`AdminManagementGuard`,
  fails closed with 403).
- Platform-operator powers come from the `IsPlatformOperator` flag. A **backward-compatible fallback** also
  treats the default municipality's Head as an operator — but only for their own municipality: cross-tenant
  listing and cross-tenant MFA reset require the real flag.
- Account lockout: 5 failed attempts = 15-minute lock. Access tokens 15 min, refresh tokens 7 days, hashed at
  rest, single-source, revoked on logout.

---

## 5. Authentication and account security

- **Password sign-in** issues a 15-minute access token and a 7-day refresh token.
- **Two-factor (TOTP)** is opt-in for everyone and **mandatory for Heads**, enforced after sign-in by
  `MfaEnforcementGate` so a requirement can never lock anyone out. RFC 6238 over `HMACSHA1`, verified against
  the RFC's published vectors; secrets encrypted with `ICredentialProtector` (AES-256-GCM); 8 single-use
  recovery codes; a used step is recorded so a code cannot be replayed.
  The password step issues a hashed, single-use, 5-minute challenge and **no tokens**; `mfa/verify-login`
  exchanges challenge + code for the session. Enabling does not ask for the password (the user is already
  signed in); turning it off requires the password **and** a valid code.
- **Self-service password recovery** by **email only** (never username), enumeration-safe, 30-minute
  single-use hashed link, requires a verified email, cannot revive a deactivated account, revokes sessions.
- **Email verification** with hashed 7-day links; changing an email clears the verified flag.
- **Rescue path** for a Head who lost both device and codes: the platform operator clears the second factor
  after re-entering their own password. Logged as a warning.

---

## 6. Money in and money out

- **Field collection** (collector app): daily NPM marking, monthly stalls, slaughter transactions, terminal
  trips, Tabo-an attendance. Reads fall back to an offline cache; writes carry a **client operation id** so a
  retry after a flaky connection cannot double-record.
- **Office collection** (portal): the same records, plus OR entry, utilities (electricity/water) and
  corrections.
- **Online payment** (payor portal): PayMongo, with each LGU able to hold its own credentials. One unfinished
  checkout is reused rather than duplicated — the double-payment guard. NPM pays a whole month of daily fees,
  fish days and utilities through their own paths.

---

## 7. Reporting

Financial report (per facility and period, with month-over-month movement guarded against a near-zero
baseline), month-end report, collection manager, follow-up queue and its history, collection exceptions,
closed/inactive accounts register, stallholder roster (the official "List of Stallholders" form), per-facility
reports (NPM, SLH, TRM, TPM), transactions feed, audit trail, and Export Data (print / PDF / CSV).

Reporting invariants:

- Rosters list **current holders only** — closed and expired accounts appear in history, not on the roster.
- Monetary totals count **active** stalls only, consistent with the active-stall count.
- A daily-billed facility's monthly figures are derived (see §2), never read from the stored monthly rate.
- Total rows state a per-head or per-unit rate only when every line shares one; otherwise they show a dash,
  because no single figure would be true.
- The stallholder roster carries base rental only — no fish, electricity or water folded in.

---

## 8. Onboarding a new LGU

```
Assessment request (public form)
  → Platform operator reviews          → declined, or approved
  → LGU completes its onboarding       (facilities, sections, rates, accounts, branding)
  → Operator validates the submission
  → Operator ACTIVATES: the tenant is created, its facilities and rates are written,
    accounts are provisioned inactive, and the Head receives a one-time activation link
  → Head sets their password, enrols two-factor, and the portal opens as that LGU
```

Everything the portal shows is then tenant-resolved: office name and acronym, municipality and province,
seal, facility names and short names, market-section labels, fee rates, OR series, market day, and the
collector app's bind link.

---

## 9. Platforms

| Platform | Users | Notes |
|----------|-------|-------|
| Web portal (Blazor Server) | Head, Admin | Full management. Payor portal and public pages share the app |
| Collector app (.NET MAUI, Android) | Collectors | Binds to an LGU by link or QR; offline-tolerant; push notifications |
| Operator console (Angular, separate repo) | Platform operator | Assessment → validation → activation |

---

## 10. Vocabulary

- **Tenant / LGU** — one municipality's isolated data set.
- **Bind link** — a per-LGU one-time link (also a QR) that points a fresh collector-app install at that LGU.
- **Billable** — a stall that owes for the period (active, in term, not excused).
- **Settlement** — converting a month of NPM daily marks into the month's payment record.
- **Head** — the LGU's SuperAdmin, the office's own authority. Not the platform operator.
