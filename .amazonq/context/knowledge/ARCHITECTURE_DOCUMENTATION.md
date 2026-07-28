# Architecture Documentation — why the system is shaped this way

Companion to `arch-rules.md` (what is allowed) and `patterns.md` (what to copy). This file explains the
reasoning, so a change can be judged rather than pattern-matched.

---

## 1. Deployment shape

```
                    ┌──────────────────────────┐
  Head / Admin ───► │ Blazor Server portal     │ ──┐
  Payor        ───► │ (+ payor portal, public) │   │  /api/authproxy/* for cookie-bearing auth
                    └──────────────────────────┘   │
                                                   ▼
  Collector app ─────────────────────────────► ┌─────────────────┐      ┌──────────────┐
  (.NET MAUI, Android)                         │ ASP.NET Core API│ ───► │ PostgreSQL   │
                                               └─────────────────┘      │ (Azure)      │
  Operator console (Angular, separate repo) ──►                         └──────────────┘
```

Two containers — portal and API — on Azure Web Apps (sitecontainers), images tagged with the commit SHA, built
and pushed by GitHub Actions on a push to `master`. One database, tenant-scoped. Migrations apply at startup,
which is why they must be additive.

The image tag equalling `HEAD` is the only reliable proof a deployment actually shipped; the workflow finishing
is not.

---

## 2. Why Clean Architecture here

The office's rules change (rates, sections, market days, new facilities) far more often than the technology
does. Keeping those rules in Domain and Application, with EF Core and HTTP at the edges, means a rate change or
a new facility type touches one folder rather than the whole app — and can be tested without a database.

The layering also protects the accuracy baseline: because reports are computed by handlers and repositories
rather than in the UI, the same figure cannot drift between the screen, the printed roster and the CSV.

---

## 3. CQRS and MediatR

Reads and writes have genuinely different shapes here: a write enforces invariants on one aggregate, while a
read joins several tables and projects a report row. Splitting them lets reads be `AsNoTracking()` DTO
projections without dragging entity graphs around, and keeps write handlers small enough to reason about.

The pipeline gives one place for cross-cutting behaviour: validation, and cache invalidation on mutation.

---

## 4. Result\<T\> instead of exceptions

An expected failure — wrong password, no contract for the period, OR number already used — is not exceptional.
Returning `Result<T>` makes those outcomes part of each method's signature, so a controller maps them to status
codes mechanically and a UI can show the message without a try/catch around every call. Exceptions are reserved
for genuine faults, handled by `ExceptionHandlingMiddleware`.

`Result<T>` also carries the status code, which is what lets the client distinguish "your session expired" from
"that code was wrong" without the server having to spell it out in prose.

---

## 5. Repository + UnitOfWork

Repositories exist for two reasons: handlers stay testable without a database, and read projections stay in one
place so a report's SQL shape can be tuned without touching policy. `IUnitOfWork.SaveChangesAsync` is the only
commit point, which is what makes the audit interceptor reliable — anything that bypasses it is invisible to
the audit trail.

---

## 6. Multi-tenancy by query filter

Every tenant-owned entity carries `MunicipalityId` and a global query filter applies the current tenant. The
alternative — a database per LGU — would multiply migrations, backups and cost for municipalities with a few
dozen stalls each.

The trade-off is that **uniqueness becomes per tenant**, and that has bitten before: an unscoped
"find the user with this email" resolved one row globally and reset the wrong municipality's password. Anything
that must look across tenants uses `IgnoreQueryFilters()` deliberately, with a comment, and handles multiple
matches.

Tenant resolution is per request (portal cookie / API token / mobile bind token). Before sign-in there is no
tenant, which is why the sign-in, refresh, activation and platform-operator paths are the legitimate
`IgnoreQueryFilters()` sites.

---

## 7. Rates as data, resolved as of a date

Fee amounts started as constants because there was one municipality. They are now rows in `FacilityRates` with
an effective date, resolved through `IFeeRateResolver`, with the constants as fallback so Cantilan is
unchanged. Resolving *as of a date* is what makes a mid-month rate change bill correctly and a historical
report stay true after a rate rises.

`Stall.ResolveDailyFee` is deliberately the single rule for a stall's daily fee (custom section keeps its own
rate; canonical sections use the tenant's). Reports call it too, so a roster cannot disagree with the ledger —
that disagreement is exactly the bug that once printed Cantilan's ₱900 for a ₱40 municipality.

---

## 8. Two-factor design decisions

- **Enforced after sign-in, never before.** The Head is already authenticated when the requirement appears, so
  a mandatory second factor can never lock an office out of its own portal.
- **The challenge is the credential** for the second step: the password step issues a hashed, single-use,
  5-minute challenge and no tokens, so a correct password alone yields nothing.
- **Asymmetric on purpose:** enabling asks for no password (already signed in, and it only adds protection);
  disabling requires the password *and* a valid code, because that direction removes protection.
- **Uniform failure message** for a wrong code, an expired challenge or a locked account, so the endpoint
  cannot be used to probe state. Wrong codes feed the existing lockout.
- **A used step is recorded**, so a code cannot be replayed inside its own 30-second window.
- The only rescue for a Head who lost device and codes is the platform operator, because peer Heads are blocked
  from each other's accounts and password recovery restores a password, not a factor.

---

## 9. Offline tolerance in the field

Collectors work where signal is unreliable, so the app reads through an offline cache and writes carry a
**client operation id**. The id is what makes a retry safe: the API discards a duplicate rather than recording
a second payment. This is why the UI never offers to cancel a slow save — the server may already have
committed, and cancelling is how a collection gets recorded twice.

---

## 10. Caching

`IEemoAppCache` caches expensive tenant-scoped reads (rosters, summaries) keyed by tenant, with region-based
invalidation on mutation through `IEemoCacheInvalidator`. Because cached values are shared, anything derived
must be computed **before** the value enters the cache, not mutated afterwards.

Blazor `[PersistentState]` caches a page's loaded data across prerender/interactive so a page does not fetch
twice, and the portal's static assets are fingerprinted (`@Assets[...]`), so a deployment cannot serve stale
CSS.

---

## 11. Verification posture

Money and tenancy are the two areas where a silent error is expensive, so the standard is: reproduce the defect
in a test, fix it, and prove the test fails against the old behaviour. Reports get a Cantilan-unchanged test
beside any new per-LGU behaviour. After deployment, verify the image tag, health, sign-in and the scoped CSS
bundle rather than assuming the pipeline succeeded.
