# AGENTS.md — entry point for coding agents

StallTrack — a multi-tenant revenue collection platform for LGU-managed economic enterprises. In production.
Reference tenant: EEMO, Municipality of Cantilan, Surigao del Sur.

## Authority and conflicts

- Explicit current task and business rulings govern the requested behaviour; they take precedence over general skill guidance.
- Accepted business and architecture documentation records intended semantics.
- Current code, migrations, tests, CI/workflows and verified production behaviour are evidence of what the system does now.
- When those disagree, surface the contradiction and determine which source is stale before changing behaviour. Never silently
  resolve a financial or business contradiction merely by choosing the implementation over the documentation.

## Read these before changing code, in this order

1. `.kiro/knowledge/arch-rules.md` — implementation boundaries: what is allowed and what is forbidden.
2. `.kiro/knowledge/patterns.md` — the code shapes to copy.
3. `.kiro/knowledge/ARCHITECTURE_DOCUMENTATION.md` — why the design is what it is.
4. `.kiro/knowledge/EEMO_Complete_Documentation.md` — accepted business semantics.
5. `.kiro/knowledge/EEMO_REVENUE_ARCHITECTURE.md` — approved target revenue architecture, migration roadmap and UI rules; distinguish intended target behavior from current implementation evidence.

Short versions of the same material live in `.kiro/steering/` (`product.md`, `tech.md`, `structure.md`,
`CONTEXT.md`). They are navigation aids, not a substitute for the complete documents.

## High-risk invariants

- **Cantilan is the accuracy baseline.** A change made for another municipality must never move a Cantilan figure.
- **Tenant scoping fails closed.** Tenant-owned reads use the global query filter. `IgnoreQueryFilters()` is limited to
  documented pre-tenant or deliberate cross-tenant paths; every use needs a reason, and cross-tenant reach requires the
  dedicated platform-operator guard.
- **Rates are data and dates matter.** Resolve through `IFeeRateResolver`; `FeeRates` constants are a fallback only.
  Transactions use their business date. A screen quoting a monthly fee uses `RatePeriod.AsOf` rather than inventing a date.
  A stall's daily fee comes from `Stall.ResolveDailyFee(resolvedRate)`, never the stored `MonthlyRate`.
- **An NPM market month follows its explicit `NpmMonthBasis`.** `RentGoal` settles daily collections against the
  tenant's fixed monthly obligation and may require a month-end top-up. `PureDays` has no fixed monthly rent: its
  obligation is the resolved daily fee × chargeable days, with no top-up. Both use the shared month rule/ledger
  (`DailyBilledMonthObligation` / `MonthCredit` / `MonthOutstanding`) so Expected − Collected − Credits = Outstanding;
  never infer the basis from a stored monthly amount or reproduce the arithmetic in a client.
- **Money belongs to the period and occupancy that incurred it.** Use `DomainRules.TermLastDay` for exact terms,
  `StallOccupancy.AnsweringForMonth` for the one monthly owner, and the past occupancy's contract rate. Do not infer
  historical liability from the stall's current holder or current rate.
- **Reconcile reports like for like.** Compare the same tenant, facility, occupancy scope, period, as-of date and money
  basis. Period, lifetime, assessment and receipt views may legitimately differ; when definitions match, they must share
  the same source or rule.
- **Uniqueness is per tenant.** A username, email, stall number or OR number is unique within a municipality,
  not globally, so a cross-tenant lookup must handle multiple matches.
- **Authentication uses the established boundaries.** Reuse the authorization guards. Authenticated endpoints use an
  `AddApiHttpClient` client; the anonymous `IAuthApiClient` contains anonymous endpoints only. Mandatory Head MFA is
  enforced after sign-in.
- **Mobile writes are retry-safe.** Preserve the offline queue and `ClientOperationId` idempotency. The collector business
  date comes from the server-issued session date when available, with the device clock only as fallback.
- **Scoped CSS must stay brace-balanced.** One unbalanced brace in a `.razor.css` corrupts the whole bundle and
  breaks every page; neither `dotnet build` nor `/health` catches it.
- **Prerendering runs `OnInitializedAsync` twice.** Never consume a one-time token there.

## Commands

```bash
dotnet build EEMOCantilanSDS.slnx --configuration Release

# Run the suites SEPARATELY — combining them causes a bUnit timing flake.
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj --configuration Release
dotnet test EEMOCantilanSDS.ComponentTests/EEMOCantilanSDS.ComponentTests.csproj --configuration Release
# Requires Docker/Testcontainers.
dotnet test EEMOCantilanSDS.IntegrationTests/EEMOCantilanSDS.IntegrationTests.csproj --configuration Release
```

Migrations are **additive only** (production applies them at startup).

## Working agreements

- Use file editors, not scripted in-place edits: PowerShell string replacement has corrupted tracked files here
  (stripped a UTF-8 BOM, mangled `₱`/`—`/`…`, produced invalid YAML).
- Stage by explicit path and check `git diff --cached --name-only`. Never stage `.env`, keystores, database
  dumps, APKs or `artifacts/`.
- A push to `master` deploys to production (~10–13 min). Verify afterwards: deployed image tag equals `HEAD`,
  API `/health` 200, portal `/login` 200, scoped CSS bundle brace-balanced.
- Money, reporting, tenancy and auth fixes need a test that fails before the fix — prove it by reintroducing the defect once.
- Collector-app changes need a signed RELEASE APK before collectors see them. `ApplicationVersion` must increase; use the
  manual publish workflow and verify that the release asset and API-advertised version describe the same build.
