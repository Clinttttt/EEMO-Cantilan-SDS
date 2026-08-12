# Outstanding work

Everything known to be unfinished, with what was VERIFIED against the code rather than assumed. Ordered by
risk-adjusted value. Update this file in the same commit that changes an item's status — a backlog that lags the code is
worse than none.

Last reviewed: 2026-08-12.

---

## Architecture review backlog

Source: `StallTrack_Architecture_Review.md` (external, 2026-08-11). Its claims were checked one at a time; where the
review was wrong or overstated, that is recorded here rather than silently dropped.

### 1. Tenant isolation — DONE, with three residuals recorded below

Shipped in `4a6ea50` (characterization tests), `eee55d8` (writes fail closed), `70075c0` (reads fail closed, authenticated
fallback removed).

What it does now: an authenticated caller resolves to their OWN municipality or to nothing — never to the default. A
token-less caller (login, activation, webhook, background work, startup) still resolves to the default, which is correct
for paths with no user. An unresolved tenant reads NOTHING and cannot write at all. A context built with no accessor
(design-time tooling, migrations, much of the test suite) still works across tenants; `AppDbContext.HasTenantAccessor`
separates "system" from "unresolved", which `Guid.Empty` used to conflate.

RESIDUALS — not defects today, but the ways this can quietly regress:

- **The no-accessor escape hatch is ungated.** `new AppDbContext(options)` sees every tenant, by design, for tooling and
  tests. Verified 2026-08-12 that NO production code uses that constructor — the only 235 call sites are in the two test
  projects. Nothing stops one being added. Making the constructor `internal` with `InternalsVisibleTo` would let the
  compiler enforce it; NOT done because `dotnet ef` design-time behaviour was not verified and a broken migration workflow
  is a poor trade for a guardrail. A source-scanning test was considered and rejected as brittle across CI paths.
- **`IgnoreQueryFilters()` is still free to call anywhere.** The review wanted cross-tenant reads expressed through named
  cross-tenant ports instead. Today roughly a dozen call sites use it legitimately (login, seeders, backup, the OR
  registry, platform-operator paths), and each is commented — but a new one can be added silently, and it bypasses the
  boundary completely. An architecture test could pin the allowed list.
- **Tenant isolation IS now proven against real Postgres** (`TenantIsolationTests`, four cases: each tenant sees only its
  own; an unresolved tenant sees nothing while the rows demonstrably exist; another tenant's row is unreachable by primary
  key; a write is stamped with the writer's tenant). Proven load-bearing on 2026-08-12: reinstating the old fail-open
  filter failed `AnUnresolvedTenantReadsNothing` and ONLY that one — the three resolved-tenant cases correctly still
  passed, since that defect opens only the unresolved path. The filter was then restored byte-identical.

### 3. Move password hashing out of Domain — HALF DONE

Shipped in the commit that added `IPasswordHasher` (Application port), `IdentityPasswordHasher` (Infrastructure), and
migrated the SIX verification call sites: admin, collector and payor login, and the three restore handlers that
re-authenticate. Those files no longer import ASP.NET Identity at all.

The format is deliberately unchanged — `PasswordHasher<BaseUser>` with default options, exactly what the call sites used —
because every stored hash was written that way. Tests assert that a hash produced by `AdminUser.Create` and by
`CollectorUser.Create` verifies through the port; if that ever fails, the office is locked out of its own system.

Also fixed while there: a malformed or empty stored hash used to throw `FormatException` out of a login attempt, giving a
500 where a 401 belongs. It reads as "wrong password" now.

REMAINING — the harder half. Domain still hashes in eight places: `BaseUser` (4), `AdminUser` (2), `CollectorUser` (2),
`PayorUser` (2 — includes its own `Create`). Those are `Create` factories and `ChangePassword`/`ResetPassword` methods that
take PLAINTEXT. Fixing it properly means the factories accept an already-computed hash, which changes their signatures and
therefore every caller: seeders, onboarding activation, first-console-admin, MFA reset, password reset, and a good number
of tests. That is a wide, mechanical change and should be its own commit — with the same compatibility test as its
guardrail, because it is the change that could lock everyone out.

Corrections to the review worth keeping:
- It said seeding depends on the filter being a no-op. It does not — `MunicipalitySeeder` reads a table that is not
  tenant-owned, and the facility and rate seeders already use `IgnoreQueryFilters()` and stamp explicitly.
- It implied authenticated requests could fail OPEN. They could not: they fell back to the DEFAULT municipality, so the
  real hazard was reading Cantilan's data, not everyone's.
- Production carried ZERO unstamped rows in every tenant-owned table (checked 2026-08-11), so no backfill was needed.

### 2. Split the oversized repositories — IN PROGRESS

`CollectorRepository` ~80KB, `StallRepository` ~59KB, `PaymentRepository` ~52KB. They mix aggregate writes, auth lookup,
mobile projections, reports and uniqueness checks.

Done: `IStallLedgerQueries` (`466fa11`), `IMissingReceiptQueries` (`2f9bffc`), `IStallMobileQueries` (`0d1ebad`) and
`ICollectorMobileQueries` (`13ffe29`), `ICollectorReportingQueries` (this commit). `IPaymentRepository` is now load-by-id,
add, update and the three receipt-availability rules; `IStallRepository` has shed its mobile projections, and
`ICollectorRepository` is now the ACCOUNT only — load to modify, find for login, rule on uniqueness — with the office's
roster and one collector's activity on the reporting contract and the app's three projections on the mobile one.

Approach that is working, and worth continuing: split the CONTRACT first, leave the code in place, then move files as a
mechanical follow-up. The reads share private obligation arithmetic, and duplicating money arithmetic is how two screens
start disagreeing. Registrations resolve the EXISTING repository instance rather than registering the type twice — two
instances per request would mean two change trackers, so a read after a write in the same request could miss it.

Remaining, in order:
- **`StallRepository` closed-accounts and contract-attention seams.** `GetClosedStallAccountsAsync` alone has ~35 refs and
  `GetClosedStallAccountsForPeriodAsync` ~14, mostly test setups. Needs its own session; the volume is the risk, not the
  design.
- **`StallRepository` register seam** — `GetStallsByFacilityAsync`, `...Paginated`, `GetStallHoldersListAsync`,
  `GetSectionSummariesAsync`.
- **A receipt-registry CONTRACT.** Lower value than the review implies: the RULE is already single-sourced in
  `OrNumberRegistry`, verified 2026-08-12, so this is about interface placement (five repository interfaces expose one
  rule) rather than about unifying logic. Tidying, not correctness.
- **THE FILE MOVES.** Every slice so far split contracts and left the implementations in place, on purpose. Three files are
  still oversized (`CollectorRepository` ~80KB, `StallRepository` ~59KB, `PaymentRepository` ~52KB) and the seams are now
  stated by the compiler. Moving the code into per-capability files is the mechanical follow-up that actually shrinks them —
  and the private arithmetic they share has to be moved deliberately, not duplicated.

### 3. Move password hashing out of Domain — see above (HALF DONE)

### 4. Move `Result<T>` and paging models out of Domain — NOT STARTED

`Domain/Common/Result.cs` carries HTTP status codes and `Unauthorized`/`Forbidden`/`NoContent`; Domain does not otherwise
use the type. `CursorPagedResult<T>` is an application/API contract too. Move to Application with error categories and let
API translate to HTTP.

### 5. Replace `IAppDbContext` feature by feature — NOT STARTED

37 call sites, 38 EF imports in Application. Do NOT run as a campaign: convert a feature only while already changing it.
Each one risks a behaviour change in authentication or onboarding. Remove the EF package reference from Application only
after the last caller goes.

### 6. Extract a Contracts project — NOT STARTED

~107 DTOs, 29 typed API-client interfaces and 19 request files sit in Application, so HttpClients, Blazor, MAUI and the
tests depend on the whole assembly. Correct in principle and the largest single change on the list; do it after the
boundaries are right.

### 7. One transaction boundary per command — NOT STARTED

`CreateStallCommandHandler` saves the stall, then saves its contract in a second commit; a failure between them leaves an
incomplete stall. Audit for other multi-save handlers. No blanket transaction behaviour without auditing external HTTP
calls first.

### 8. Inject time instead of static clocks — NOT STARTED

Domain calls `DateTime.UtcNow`; Application reads static `PhilippineTime.Now`/`Today`. Prioritise lockout, token expiry,
billing eligibility and reporting periods, and pass dates INTO domain decisions. Mechanical audit stamps can stay in the
interceptor.

### 9. Split API / Infrastructure / Client registrations — NOT STARTED

Also found while reading: `Application.DependencyInjection` takes configuration it never uses; AutoMapper is registered
with an empty profile and no `IMapper` usage was found (verify, then remove); the Client's HTTP registration is misnamed
`AddPersistence`; migration, seeding and default-tenant initialisation should leave `Program.cs` for an explicit startup
initializer.

### 10. Strengthen the architecture tests — NOT STARTED

Assert: Domain free of EF/Identity/MediatR; Application free of EF and ASP.NET; no API-client interfaces in Application; no
HTTP status codes in Domain; cross-tenant services explicitly named; API policy and Application authorization share one
authorizer. Keep the existing financial, Cantilan-unchanged and tenancy regression tests.

### 11. Reorganize Application into feature folders — NOT STARTED, DO LAST

`Command`/`Queries`/`Dtos`/`Requests` scatter each capability. File moves only, no behaviour. Last, because it churns
every path and would bury a real change in the diff.

---

## Confirmed office rules

Answered by the office (interview, 2026-08-12). Recorded here because they are policy, not code, and the next person
should not have to re-derive them.

**OR numbers.** Unique per TRANSACTION within the LGU. The same number must never appear against another vendor, or in
another module. But one transaction may produce several RECORDS, and those share the one number, because it was one
payment: two kinds of animal on one slaughterhouse receipt; several market days paid at once; several months settled
together. Verified 2026-08-12 that the code implements exactly this — all five repositories route to one
`OrNumberRegistry` (Infrastructure/Repositories/OrNumberRegistry.cs), which allows the repeat within one stall's months,
one stall's days, and one slaughter receipt (same owner AND same date), and refuses it everywhere else. It also checks
soft-deleted rows, so deleting a record never frees its receipt number, and it scopes per municipality so a second LGU
may reuse a number that exists only in another.

**The three billing rules** stand as implemented, per the same interview: a term of N years owes exactly N × 12 months'
rent; an expired contract stops accruing rent but keeps its balance collectable; a current or yearly market report counts
only the market days elapsed as of the report date.

---

## Open questions for the office

These cannot be answered by reading code.

1. **Two Postgres firewall rules** (`ClientIPAddress_2026-7-6...`, `ClientIPAddress_2026-7-17...`) open specific IPs
   indefinitely. Flagged; keeping or removing them is the office's call.

---

## Deferred product work

- **NPM daily history for CUSTOM sections** is reachable through the section chooser but has no end-to-end test.
- **Hide or soft-delete an OR number** so a withdrawn receipt stops being reported as missing.
- **The Backups page** has two different sections both headed "Recent backups" — one for in-app restore points, one for CI
  runs. Confusing to read. `Backups.razor:824-826` also contradicts `BackupController.cs:37-43`.
- **No pre-deploy backup gate**: a deployment can migrate before a fresh backup exists.
- **`restore.yml` does not quiesce the API** during a restore, so writes can land mid-restore.
- **`Profile` "Earlier Terms" card** has no component test (no fixture existed when it was written).
- **"Same payor" matching is free-text name comparison** — there is no payor entity behind it.
- **Known small duplications**: `SettleNpmMonthCommandHandler` repeats logic; `FacilityReportsRepository.Revenue.cs` holds a
  static `ConditionalWeakTable` with a stale `asOf`; mobile `_recordsCache`/`_reportCache` are never cleared on logout;
  `FacilityReportsModal.razor` still hardcodes `#4a9eff`; the dashboard computes compliance twice; `Take(AttentionLimit)`
  is applied before the header count and sum.
- **Orphaned CSS** in `FollowUpQueue.razor.css` (~157 unreachable lines). Left deliberately: that file has mixed line
  endings and a bulk rewrite would normalise them and bury the diff.
