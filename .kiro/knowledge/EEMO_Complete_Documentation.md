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

**That monthly rent is also the ceiling on what a month can owe.** Collection is day by day, but the space is
let for a monthly rent, so a month's obligation is its collectable days at the day's rate **capped at the
month's base rent** (`DomainRules.DailyBilledMonthCharge`):

- a 31-day month owes ₱900, not ₱930 — the office's paper says ₱900 and reconciles against ₱10,800 a year;
- once the base rent is collected the month is **paid**, and its balance is nil, never negative;
- a 31st day actually traded may still be collected — that is real revenue recorded against the day, income
  beyond the rent, and never an arrear;
- the rule **caps and never tops up**: February's 28 days owe ₱840, a mid-month start owes only the days held,
  and excused/absent days and market closures still reduce the month. A full year of daily obligation is
  therefore ₱10,740, while the roster and register continue to STATE the ₱900 / ₱10,800 paper equivalent.

The cap applies wherever an obligation, balance or status is computed (facility reports and their delinquency
table, the payor ledger and balances, the payment-history modal, the inactive-account register). It is applied
per calendar month, so a yearly view caps each month on its own. Marking and settling days is untouched.

---

## 3. Core business rules

- **Delinquency:** 3+ unpaid months inside a rolling 12-month window = delinquent; 1–2 = arrears.
- **Contract expiry:** flagged when within 3 months; an expired contract drops off the current-holder roster
  (it stays in history) and can be renewed, which starts a fresh term at the stall's current rate.
- **A space outlives its lessees.** A stall keeps its number, its section and its whole history when it is
  re-let, so history is a sequence of **occupancies** (`Stall.Occupancies`): one per term, each running from
  its effectivity to the earliest of the day it was terminated, the day before the next lessee began, the day
  the stall was closed, and its own term end. Charges additionally stop at the term end (`BillableEnd`): a
  lessee who stayed on after their term lapsed owes nothing for those days, though money they paid is theirs.
- **Money is named after the lessee who owed it.** A collection is attributed by the period it was raised
  FOR, never by the day it arrived, so an arrear settled after a handover still belongs to the lessee who
  incurred it. A daily collection carries its own business date; a monthly charge belongs to a month.
- **A month belongs to exactly one occupancy.** A month's rent is one indivisible obligation (one payment
  record per stall per month), so a stall handed over mid-month is answered for by the lessee whose occupancy
  began latest within it — `StallOccupancy.AnsweringForMonth` is that rule, and the register, the reports, the
  payor lists and the collection dialog all read it. Nothing may charge or credit the same month twice.
- **A past occupancy is charged the rent it was let at** (`Contract.MonthlyRentalRate`), never the rate the
  space carries now: the stored `Stall.MonthlyRate` is rewritten when the space is re-let or revised, and
  reading it would restate a departed lessee's arrears at a rate they never agreed to.
- **Occupancy without a signed contract.** A barbecue or ice-plant space is let with no contract at all, and
  some commercial-centre spaces run on an extension of a lapsed one (`OccupancyArrangement`). Rent is
  assessed identically; what is absent is the leasee name, the term and the contract rate. Such an occupancy
  is open-ended (`DomainRules.OpenEndedTermYears`) so it never falls due for renewal, and the official sheets
  print "No contract (space only)" / "No contract (extension)" across the contract columns.
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

### Bulk import of a stallholder list

The office keeps its records on the official "List of Stallholders" sheets, so a facility can be populated by
uploading one (CSV / XLSX). The rules that make an as-written sheet import correctly:

- Columns are read by position, tolerating printable-report layouts (title banners, header and TOTAL rows are
  skipped; for NPM, section banners select the rows of the chosen section).
- The **rate** is the contract rental when the sheet states one, otherwise the actual monthly rental — a list
  of spaces let without a contract fills only the latter.
- A row that names nobody on a contract and states no term is recorded as held **without a signed contract**
  (open-ended), not rejected for a missing duration. A row that DOES name a leasee but omits the term is a
  missing figure and is reported.
- Placeholder occupants ("Closed", "Vacant", "N/A", …) are rejected server-side: importing them as active
  stallholders would inflate the active count and raise arrears against nobody.
- A number belonging to an **active** contract is refused. A number belonging to a **vacated** space (closed,
  or its term lapsed) is reused — the space changes hands, the previous occupancy is ended the day before the
  new term begins, and its payor links are revoked, exactly as the Add Vendor takeover does. The preview says
  which numbers those are before anything is saved.
- **Re-importing the same sheet changes nothing.** A row whose stall already carries that same occupant from
  that same effectivity date is reported as already imported, so a second upload cannot add a second term (and
  with it another month of arrears).
- Valid rows are saved in one transaction; invalid rows are reported per row and never block the rest.

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
  This applies to every roster and register that prints one, including the per-facility "List of
  Stallholders" and its CSV export.
- **A period states its own period's money; only the cumulative view states lifetime totals.** The follow-up
  history is filtered by a year, a month, or "Whole time":
  - a year or month view reads the assessment for that window, and the inactive-account register bounded to
    it (`GetClosedStallAccountsForPeriodAsync`), so an occupancy that did not exist in the window is not
    listed at all;
  - a term or occupancy is stated as the PART of it inside the window — under a 2026 filter a term of
    Jun 2023 → Jun 7, 2026 reads "Jan 1, 2026 → Jun 7, 2026" — because the amount beside it is that
    window's. A span that lies wholly outside the window is stated whole (a term that lapsed earlier is a
    fact about the past, and clipping it would say nothing);
  - the window never runs past the snapshot date, so a running occupancy is never stated into the future;
  - "Whole time" states whole spans against whole balances, and is the view that answers "what is owed in
    total".
- Total rows state a per-head or per-unit rate only when every line shares one; otherwise they show a dash,
  because no single figure would be true.
- The stallholder roster carries base rental only — no fish, electricity or water folded in.
- A cached period snapshot is invalidated by money recorded in ANY period, because it embeds the register.

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
