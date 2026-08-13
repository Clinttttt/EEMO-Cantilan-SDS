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

### 3. Move password hashing out of Domain — DONE

Verification went behind `IPasswordHasher` in `19085b3`; the password-change methods followed in `fdc3700`; the three
`Create` factories and the package reference are done now. Domain no longer hashes, verifies, or references an identity
package — its csproj has NO package references at all.

The volume was the risk: six production callers and ~102 test sites, every one able to pass plaintext where a hash was meant,
with both being `string`. Rather than trust careful editing, `HashedPassword` (Domain) makes it a COMPILE error: the
factories and password-change methods accept only that type, and the only way to obtain one is `IPasswordHasher.Hash`. The
compiler then listed all 96 remaining sites, and each was rewritten at the file, line and column the compiler pointed at, so
nothing was guessed and nothing missed.

The stored format is unchanged — the same `PasswordHasher<BaseUser>` with default options — which is what lets existing
accounts sign in. Six tests fail if the format changes, which is the guardrail that matters most here.

`HashedPassword` also refuses an empty value: an empty hash accepts nothing, so it is a bug rather than a state, and failing
where it is constructed beats writing a row whose owner can never sign in.

Verification (six login/restore sites) went through `IPasswordHasher` in `19085b3`. The password-CHANGING half is now done
too: `CompletePasswordReset`, `CompleteActivation` and both `ResetPassword` methods take an already-hashed value, and
`BaseUser.VerifyPassword` — which constructed an Identity hasher inline — is deleted, with its five callers asking the port
that the login handlers already use. `PayorUser.ResetPassword` was dead and went with it.

The compiler could NOT catch the dangerous part of this change: four callers passed plaintext into parameters that now mean
a hash, and both are strings. Storing plaintext as a hash would lock that account out permanently. Each was found and fixed
by hand, and the risk is covered by test: reinstating the plaintext call failed
`Reset_ValidToken_ChangesPassword_ConsumesToken_AndRevokesSessions`.

Tests hash through the real implementation via `TestPasswords.Hash`/`.Accepts`, because a test that hashed differently from
production would prove nothing about whether an account can sign in.

The `Create` factories, the package reference and the "Domain free of Identity" assertion are all done — see the item 3
heading above for how the ~102 call sites were changed safely.

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
`ICollectorMobileQueries` (`13ffe29`), `ICollectorReportingQueries` (`99ae349`), `IClosedStallAccountQueries` +
`IContractAttentionQueries` (`f100980`), `IStallRegisterQueries` (`e11081f`), `IOrNumberRegistry` (this commit).
`IPaymentRepository` is now load-by-id, add, update and the two stall-scoped receipt allowances; `ICollectorRepository` is
the ACCOUNT only; and `IStallRepository` is the stall AGGREGATE only — load with contracts, let, transfer, close, and rule
on stall-number uniqueness — with the register, stallholders list, section summaries, the collector app's projections and
both follow-up reads on their own read contracts. Plain OR availability is no longer a method on five module repositories:
it is one port with one implementation (`DbOrNumberRegistry`) over the one rule (`OrNumberRegistry`), and the composition
test asserts it is absent from all five.

Approach that is working, and worth continuing: split the CONTRACT first, leave the code in place, then move files as a
mechanical follow-up. The reads share private obligation arithmetic, and duplicating money arithmetic is how two screens
start disagreeing. Registrations resolve the EXISTING repository instance rather than registering the type twice — two
instances per request would mean two change trackers, so a read after a write in the same request could miss it.

Remaining, in order:
- **THE FILE MOVES.** Every slice so far split contracts and left the implementations in place, on purpose. Three files are
  still oversized (`CollectorRepository` ~80KB, `StallRepository` ~59KB, `PaymentRepository` ~52KB) and the seams are now
  stated by the compiler. Moving the code into per-capability files is the mechanical follow-up that actually shrinks them —
  and the private arithmetic they share has to be moved deliberately, not duplicated.

### 3. Move password hashing out of Domain — see above (DONE)

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

### 7. One transaction boundary per command — DONE

Audited every handler that calls `SaveChangesAsync` more than once (nine of them) and separated the two shapes: several
saves in ALTERNATIVE branches are fine, several in ONE path are the defect.

Two genuine defects, one more than the review named:

- `CreateStallCommandHandler` saved the stall, then its first contract. A failure between them produced a let space with no
  agreement behind it — on the register, answering for a month's rent, with no lessee, term or start date to bill against.
- `CreateCollectorCommandHandler` saved the account, then its facility assignments. A failure between them produced a
  collector who could sign in but was assigned nowhere, and the office's natural remedy (create them again) would then fail
  on the unique employee ID.

Both are now one commit. No new abstraction was needed: entity ids are generated in memory and neither second step reads
the row written by the first (the assignment lookup reads FACILITIES), so a single `SaveChangesAsync` inserts both in one
transaction. Proven load-bearing — reinstating the interleaved save failed all three new tests and left the handover test,
which was already single-commit, correctly passing.

Not defects, checked and recorded so nobody re-audits them: `IssueOnlinePaymentOrNumberCommandHandler` and
`InitiateOnlinePaymentCommandHandler` save once per module branch (monthly / NPM daily / utility / fish day);
`VerifyMfaLoginCommandHandler`, `LoginCommandHandler` and `ResetPasswordByTokenCommandHandler` save once per outcome, each
followed by its own return.

`IUnitOfWork` deliberately still exposes only `SaveChangesAsync`. An explicit transaction API would only be needed by a
handler that must read its own writes mid-command, and none of these does; adding one now would invite ambient-transaction
bugs for no benefit. If a future command needs it, that is the moment to add it.

### 8. Inject time instead of static clocks — STARTED (lockout and token expiry done; ~270 sites remain)

`IClock` (Application) with `SystemClock` (Infrastructure, singleton) and `FixedClock` (tests). Three members only —
`UtcNow` for instants, `PhilippineNow`/`PhilippineToday` for the office's working day. The rest of `PhilippineTime` stays
static on purpose: converting a stored instant, or bounding a local day or month in UTC, is a pure function of its
arguments and needs no clock.

DONE — the lockout rule. It was written three times, identically, on `AdminUser`, `CollectorUser` and `PayorUser`, while
the state it works on always lived on `BaseUser`; it is now one `RecordFailedLogin(asOf)` and one `IsLockedOut(asOf)` on
the base. The four login/MFA handlers pass `clock.UtcNow`. The three `builder.Ignore(x => x.IsLockedOut)` lines are gone
with it, since a method is not mappable — confirmed by the integration suite, which builds the real model.

Thirteen tests now cover a rule that previously had none worth having: "the account unlocks after fifteen minutes" could
only be verified by waiting, so the part that protects the office — that a lockout ENDS — went unasserted. Each runs
against all three user types, because a lockout policy differing by account type is a security hole rather than an
inconsistency. Proven load-bearing: making the lockout permanent failed six of them.

DONE — token expiry. Five windows, not the three the review named: refresh, activation, password reset, MFA challenge and
email verification all compared against the machine clock inside `BaseUser` and now take the instant. Six handlers and
`TokenService` supply it.

Two faults found while converting, both recorded because they were invisible rather than harmless:

- `BaseUser.IsRefreshTokenValid(token)` had NO callers and could never have returned true — it compared a raw token against
  the stored HASH. The refresh path had grown its own copy of the rule instead. Replaced by
  `CanRefresh(refreshTokenHash, asOf)`, which `TokenService.ValidateRefreshToken` now calls.
- That copy contained a FOURTH transcription of the lockout check, in Infrastructure, missed by the lockout slice above. So
  a locked account could have kept refreshing its session if the two ever drifted. It now asks the user.

Seven more tests, each asserting the half of the rule that was unassertable: that the token STOPS working. Proven
load-bearing — making activation tokens permanent, and dropping the lockout consultation from refresh, each failed exactly
the test that describes it.

STILL TO DO, in the order the review prioritised:
- **Billing eligibility and reporting periods** — the bulk. ~58 `PhilippineTime.Now/Today` reads in Application and ~44 in
  Infrastructure. These decide which market days are chargeable, whether a term has lapsed, and which month a report
  covers, so they are the ones that make a suite pass in one month and fail in another. Convert per feature while already
  changing it, not as a sweep.
- **Audit stamps stay put.** ~150 `DateTime.UtcNow` in Domain are `CreatedAt`/`UpdatedAt` assignments. They are mechanical
  and belong with the interceptor; converting them would be a large diff with nothing to assert.

### 9. Split API / Infrastructure / Client registrations — PARTLY DONE

DONE, and each claim verified against the code first:

- **AutoMapper removed entirely.** The profile was empty and there was not one `IMapper`, `.Map<>` or `CreateMap` call in the
  solution: a package, a registration and a class that did nothing. Package reference gone; an architecture test asserts
  Application does not reference it.
- **`AddApplicationService` no longer takes `IConfiguration`.** It never read it, and the MediatR lambda parameter shadowed
  it with the same name, so the file looked as though it configured MediatR from app settings.
- **The Client's `AddPersistence` is now `AddApiHttpClients`.** It registers outbound HTTP clients; the portal has no
  database and stores nothing.
- **Startup work moved out of `Program.cs` into `DatabaseStartup` (Infrastructure).** 184 lines down to 74. Migration
  locking, seeding and default-tenant resolution are persistence concerns and now sit with persistence, and the
  advisory-lock key is a public constant that `MigrationAdvisoryLockTests` REFERENCES instead of transcribing.
- **Two false comments corrected.** Both claimed that an unresolved default municipality leaves the tenant filter "a no-op"
  and lets "token-less writes go unstamped". Neither has been true since the boundary was made to fail closed: token-less
  reads return nothing and such writes are refused. One of them was an operator-facing WARNING, so it would have pointed
  ops the wrong way during exactly the incident it exists to describe.

One deliberate behaviour difference: startup logs now use the application's default logger category rather than
`ILogger<Program>`. Only affects log filtering by category.

STILL TO DO: the review's larger ask of splitting `AddApi`/`AddInfrastructureService`/`ConfigureServices` into layer-owned
extensions with narrower responsibilities. `AddInfrastructureService` remains large and mixes persistence, caching,
payments, security and HTTP clients. Worth doing, but it is a reshuffle of registrations with no test that can prove it
beyond "the application still starts", so it wants its own session and a careful read of ordering.

### 10. Strengthen the architecture tests — PARTLY DONE

DONE:

- **`TenantFilterCoverageTests`** — every tenant-owned entity is filtered BY MUNICIPALITY, no tenant-owned type hides under
  a non-tenant-owned TPH root (a hole the per-root attachment cannot see), and the model really is largely tenant-owned so
  the checks cannot pass vacuously. Written first as "has a filter" and it PASSED with a tenant-owned entity deliberately
  excluded — a soft-deletable entity always has a filter, so the filter has to be READ, not counted. That mistake is worth
  remembering: it is the difference between a test and the appearance of one.
- **Domain free of MediatR** added to the existing dependency test; Domain has zero MediatR/EF usings and references
  neither.
- **Application free of ASP.NET Identity** and **of AutoMapper** (earlier commits).
- A third stale tenancy comment corrected, on `ApplyQueryFilters`, which still described the filter as "a no-op while
  CurrentMunicipalityId is empty".

STILL TO DO, and each is blocked by an unfinished item rather than by effort:

- **No HTTP status codes in Domain** — needs item 4 (`Result<T>` carries them).
- **Application free of EF** — needs item 5 (`IAppDbContext` exposes `DbSet`).
- **No API-client interfaces in Application** — needs item 6 (the Contracts project).
- **Cross-tenant services explicitly named** — the `IgnoreQueryFilters()` residual from item 1.
- **API policy and Application authorization share one authorizer** — unified behind `PlatformOperatorPolicy` in `8d58fc9`,
  but asserting it structurally means reading an authorization-policy lambda. Judged too brittle to be worth it; the
  behavioural tests in `PlatformOperatorGuardTests` cover the rule itself.

### 11. Reorganize Application into feature folders — NOT STARTED, DO LAST

`Command`/`Queries`/`Dtos`/`Requests` scatter each capability. File moves only, no behaviour. Last, because it churns
every path and would bury a real change in the diff.

### Test reliability — one flake found and fixed, worth watching for more

`ClosedAccountsRenewTests.Proceed_StatesNoFigures_SoTheStallKeepsItsOwnRate` failed once in a full-suite run on 2026-08-12
and passed in isolation and on re-run. Cause: it clicked the renew row and then immediately `Find`-ed a button inside the
dialog, which renders asynchronously, and asserted on the sent request straight after the click. The other tests in the
same file already waited. Fixed by waiting for the footer button and for the request, using the file's existing
`WaitForElement`/`WaitForAssertion`/`RenderTimeout` idiom; the full suite then passed three times running.

Worth knowing because CI runs these on every push, so a flake of this shape shows up as a failed DEPLOY rather than as a
test problem. Any remaining `cut.Find(...)` immediately after an interaction that triggers rendering or an HTTP call is the
same hazard.

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

- **`MustChangePassword` is set but never enforced — needs the office's decision.** Resetting an admin's password sets the
  flag, it is persisted, it travels on the token as `must_change_password`, `CurrentUserService` reads it, and the Accounts
  list shows a "Reset pending" badge for it. Nothing ever asks the user to change their password: there is no
  change-password screen and no route guard, only `/forgot-password` and `/reset-password/{token}`. Found 2026-08-13 because
  the reset dialog promised "they'll be asked to change it on next login" and the Head signed in without being asked.
  The false copy is corrected. Two ways forward, and it is a product choice:
  (a) implement it — a change-password screen plus a guard that redirects while the flag is set, for admins and collectors,
      which is a feature in its own right and touches every authenticated route; or
  (b) drop the flag and treat an office-issued password as simply the account's password.
  Until one is chosen, the flag means only "the office set this password and the holder has not chosen their own", which is
  what the badge tooltip now says.

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
