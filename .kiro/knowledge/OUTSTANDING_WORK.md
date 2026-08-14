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
- **THE FILE MOVES — `CollectorRepository` DONE, two to go.** Every contract slice split interfaces and left the
  implementations in place, on purpose. The moves are the mechanical follow-up that actually shrinks the files, and the private
  arithmetic they share has to be moved deliberately rather than duplicated.

  `CollectorRepository` 81KB → four partial files (2026-08-15): entry 10.7KB (the account repository), `.Mobile.cs` 51.6KB (the
  three projections the collector's app reads), `.Reporting.cs` 13.8KB (what the office reads about its collectors), and
  `.Recognition.cs` 9.4KB — the shared arithmetic, deliberately in ONE file, because it decides what a peso is counted as and
  when, and the office reconciles the app against its own reports by hand.

  How it was done safely, worth repeating for the remaining two:
  - **Two verified steps, not one.** First the class became `partial` and the primary-constructor parameters were captured into
    `_context` / `_feeRateResolver` / `_clock` (78 references renamed) — a pure rename, built and fully tested before anything
    moved. Only then were the blocks moved. A primary-constructor parameter is in scope ONLY in the file that declares it, which
    is why the capture is required; `FacilityReportsRepository` already carries the same note for the same reason.
  - **Proved behaviour-neutral by construction, not by hope.** Every code line of the original was compared against the
    concatenation of the four files, ignoring usings, namespaces, braces and comments: 1,145 lines in, 1,145 out, IDENTICAL. A
    move that quietly altered a figure could not survive that check, and the three suites passed unchanged.
  - A PowerShell trap cost one attempt: `@(@(1059,1302))` FLATTENS to `@(1059,1302)`, so the loop read 1059 as a whole range,
    `$range[1]` was null, and `$lines[1058..-1]` wrapped to produce a 2,364-line file. Ranges need `[int[][]]` with a leading
    comma. Restored from a copy taken beforehand and redone.

  `StallRepository` 59KB → six partial files (2026-08-15): entry 12KB (the aggregate and the ordinary stall reads),
  `.Attention.cs` 3.8KB (contracts needing attention), `.Mobile.cs` 13KB (the collector app's two rounds), `.Register.cs`
  13.1KB (the List of Stallholders), `.ClosedAccounts.cs` 21.9KB (the inactive-accounts register), and `.Collectable.cs` 2.3KB
  — the shared arithmetic deciding which days of a month a space is collectable for, which the mobile rounds and the printed
  register must answer identically. Same two-step method; 677 code lines in, 677 out, IDENTICAL.

  STILL TO DO: `PaymentRepository` ~53KB, the same way.

### 3. Move password hashing out of Domain — see above (DONE)

### 4. Move `Result<T>` and paging models out of Domain — MOVED (error categories still to do)

`Result<T>` and `CursorPagedResult<T>` now live in `Application.Common`. Domain never referenced either, so no rule changed;
what changed is that the domain no longer carries a type named after HTTP outcomes (`Unauthorized`, `Conflict`, `NoContent`)
or a paging contract. An architecture test asserts both — that they are absent from Domain AND present in Application, so it
cannot pass by their having been deleted.

497 files use these types. Rather than add a using line to every one, each consumer project declares the namespace globally
via `<Using Include="EEMOCantilanSDS.Application.Common" />`. The move is about where the types BELONG; a diff touching every
handler would have buried that. Two accidental usings went with the move (`System.Xml.XPath` and a JS-interop static import
that had no business in a result type).

STILL TO DO — the half that changes behaviour: `Result<T>` continues to carry HTTP status codes, and the review asks for
error CATEGORIES with the API translating them. That means changing every handler's failure paths and every controller's
translation, and it alters the HTTP responses the portal reads, so it needs its own session with the API contract in view. It
is a redesign, not a move, and bundling it here would have made a mechanical change unreviewable.

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

### 8. Inject time instead of static clocks — STARTED (lockout, tokens, report periods and ALL repositories done)

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

DONE — the reporting periods. The three report handlers (`GetFinancialReport`, `GetFollowUpHistory`, `GetFollowUpQueue`) took
13 static clock reads between them and now take `IClock`. These decide which month an unqualified monthly report means, how
far a whole-year snapshot runs, and where a rolling delinquency span ends.

Four of those reads were in STATIC helpers, where a primary-constructor parameter is not available — the compiler said so
(CS9105). Rather than make the helpers instance methods they now take `DateOnly today`: a static helper that reaches for a
clock cannot be tested, and passing the date in is the pattern the domain already uses.

Eight new cases assert what was previously unassertable — that the report is dated by the clock it was given, and that a
rolling span ends with the last CLOSED month, including a future month asked for (clamped, nothing owed yet) and across a
year boundary. Written first against the wrong field: the span lands on the ROW beside the money, not on the report header,
and the real values were read from the failure rather than guessed. Proven load-bearing — anchoring the span on today
instead of the month asked for fails one case and only that one.

DONE — `PaymentRepository`, 12 reads. It now takes `IClock`; the test-convenience constructor keeps the real clock and says
so, and a test that cares which day it is uses the full constructor. Everything this repository decides about eligibility —
which market days are chargeable, who holds a stall now, which rate applies — is a question about "today".

Four new tests pin the date: the current month is billed only to today, the same month owes more later in the month, a closed
month bills the office's reference month whatever the calendar length, and a month still in the future is not offered at all.
The daily rate is DERIVED from a closed month rather than written down, so no Cantilan figure is asserted for every LGU.

Two things learned while proving them load-bearing, both worth keeping:
- The eligibility bound is enforced TWICE — once on the occupancy window and once per month — so removing either clamp leaves
  the other and the tests still pass. Redundant guards are good for production and misleading for a defect probe.
- What the probe must break is the INJECTION: made to ignore the injected clock and read the static one, three of the four
  fail. That is the assertion that matters, and it is why these tests could not have been written before.

DONE — every repository. `FacilityReportsRepository` (12 reads across four partial files), `TrmRepository` (7),
`CollectorRepository` and `StallRepository` (3 each), and the six single-read repositories: `PayorRepository`,
`FacilityRepository`, `VendorRepository`, `SlaughterRepository`, `TpmRepository`, `TransactionFeedRepository`. There are now
ZERO static clock reads anywhere under `Infrastructure/Repositories`.

Each keeps a test-convenience constructor that supplies the real clock and says so, so none of the ~150 repository test
setups had to change; a test that cares which day it is passes a fixed clock to the full constructor.

Three things worth remembering from doing it:
- `FacilityReportsRepository` is PARTIAL, so a primary-constructor parameter is out of scope in the other files. The clock is
  captured into a field for the same reason `_context` already was.
- Its occupancy/obligation helpers were STATIC. They became instance methods rather than taking a date through every caller;
  the memoising `ConditionalWeakTable` stays static, keyed by stall instance, so caching behaviour is unchanged.
- A blanket text replace turned `PhilippineTime.TodayUtcRange()` into `clock.PhilippineTodayUtcRange()` in `TrmRepository`,
  because "Today" is a prefix of "TodayUtcRange". Caught by the compiler, fixed to `PhilippineTime.DayUtcRange(clock.
  PhilippineToday)` — the pure helper, dated by the clock — and the rest of the codebase checked for the same mangling.

Five new tests pin dates on the delinquency arithmetic: arrears count only months that have ENDED, a future anchor is clamped
to the last closed month (the yearly view offers every month, so this is reachable), and the count rises by exactly one when a
month closes with nothing paid in between. Proven load-bearing — ignoring the injected clock fails the future-anchor case, and
only that one, because the real date is already past the others. That asymmetry is the point: without a stated date, the
clamp is untestable for most of the year.

DONE — the WRITE path. Eleven commands and services that stamp a date into the ledger permanently now take `IClock`:
`CreateStall`, `ToggleStallStatus`, both bulk imports plus `BulkImportDailyHistory`, `RecordPayment`, `SetFacilityRate`,
`SettleNpmDays`, `SettleNpmMonth`, `NpmMonthSettlementService` and `InitiateOnlinePayment` (whose reference number carries the
issue date). A wrong date here is not a display fault — it is written down and later reconciled against paper.

`InitiateOnlinePayment`'s reference generator was static, so it takes the instant as a parameter rather than becoming an
instance method: the same pattern used for the report helpers.

Four new tests pin the rate-effective date, which is the clearest case: a rate takes effect the day it was set and NEVER
before, because re-rating a month whose receipts are already issued would disagree with the paper. Proven load-bearing — all
four fail when the handler ignores the injected clock.

A scripted edit went wrong here and is worth recording. Driving the additions from the compiler's error positions worked for
194 sites, then looped on four: where the constructor sits inside a tuple — `return (new Handler(...), mock);` — the script
found the TUPLE's closing paren, so the argument landed outside the constructor, the error persisted, and it appended again
each pass. One line ended with 90 copies. Caught by scanning for lines with more than one `new FixedClock(`, the four files
restored from git, and those four fixed by hand. `Select-String` counts LINES, not occurrences, so the first check under-
reported the damage — the count has to be per-match.

DONE — the READ path (2026-08-14). Twenty query handlers and four validators now take `IClock`. Nothing here writes to the
ledger, but a report that silently disagrees with the office's own idea of "today" is still a wrong answer, and the year bound
on the report validators ("no later than next year") could not be stated in a test at all while it moved on its own.

Two things worth carrying forward from this slice:

- **A date-pinned test can pass by coincidence.** Pinned first to a fixed date in the CURRENT year, nine of ten year-bound
  cases still passed with the validators ignoring the injected clock — the static bound happened to agree. Pinning to a year
  the server's calendar cannot match (2031) made every case load-bearing; five then failed under the reintroduced defect.
  This is the same family as the earlier vacuous assertions: a green test that cannot fail proves nothing.
- **A DI-resolved dependency has no compile-time guard.** Validators are assembly-scanned and built per request, so the four
  that now need `IClock` would have compiled, passed their unit tests (which construct them directly), and failed only when a
  clerk submitted the form. `Testing/Application/ValidatorResolutionTests.cs` now asserts every constructor parameter of every
  validator is registered by the real `AddApplicationService` + `AddInfrastructureService`, plus a named test for the clock
  registration itself. Proven by deleting the registration: the four validators and the named test failed.
  (Registration is pure — it only reads configuration and hands EF a connection string — so this needs no database.)

STILL TO DO:
- **3 in Domain — `Contract.IsExpired`, `IsExpiringSoon` and `Terminate`'s default.** Deliberately left for its own change:
  an entity cannot be given a constructor dependency, so these want `asOf` parameters, and `IsExpired` alone has ~10 call
  sites including the follow-up queue and the EF configuration. It decides whether a contract still accrues, so it deserves
  its own tests rather than being swept in with two dozen mechanical edits.
- **2 in Infrastructure are `SystemClock` itself** — the implementation of `IClock`. They must stay.
- **2 remaining in Application are not reads to fix**: a doc comment in `IClock.cs`, and `StallContractStatus`'s parameterless
  convenience overload, which already delegates to an `IsCurrentVendor(dto, today)` that IS testable. Its one caller is in the
  Client.
- **~205 in the Client.** Mostly display defaults and date-picker seeds. Lowest value, and worth judging individually rather
  than sweeping: a date picker defaulting to today is not a rule about money.
- **Audit stamps stay put.** ~150 `DateTime.UtcNow` in Domain are `CreatedAt`/`UpdatedAt` assignments; they belong with the
  interceptor.

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

- **No HTTP status codes in Domain** — DONE. `Domain_KnowsNothingAboutHttp` asserts `Result<T>` and `CursorPagedResult<T>` are
  absent from Domain AND present in Application, so it cannot pass by their having been deleted.
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

**Client names identify the client.** Confirmed by the office 2026-08-14: within one LGU there are no namesakes — two
different people carrying the same name is a national-scale problem, not a municipality's. So a slaughterhouse owner's typed
name IS their identity, and matching by name (ignoring case and redundant whitespace, per `PersonName`) is sufficient. No
payor/client entity is required. What a name cannot survive is a genuine misspelling, which is a data-entry correction, not a
modelling gap.

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

- **`MustChangePassword` is now ENFORCED** (was: set but never enforced). Resolved 2026-08-14 at the office's decision:
  - `ChangeMyPasswordCommand` — the signed-in administrator replaces their own password, re-authenticating first (an
    office-issued password may have been handed over on paper) and refusing a new password equal to the old one, which would
    satisfy the requirement while leaving the account on a password the office knows. Returns fresh tokens, because the
    requirement travels as a claim: without new ones the user changes their password and is asked again.
  - `MustChangePasswordMiddleware` (API) refuses every other endpoint meanwhile, with a short explicit allow-list — change
    the password, read current-user, refresh, log out, health — and a machine-readable code so the portal routes on the code
    rather than on English prose. The allow-list is asserted in both directions: blocking too little makes it cosmetic,
    blocking too much locks the office out of the screen that would fix it.
  - `MustChangePasswordGuard` (portal) sends a flagged session to `/change-password`. It is the experience, not the
    boundary: the API is the gate, since a guard living only in the browser is a suggestion.
  - The new page carries NO LGU branding. `AuthBrandPanel` defaults to Cantilan's seal and office name, and the branding
    endpoint that would supply the real ones is itself blocked while the requirement stands — so using it would have shown
    Cantilan's identity to every other municipality.
  - Resetting your OWN password no longer flags the account: choosing a password is not being issued one. An existing test
    asserted the opposite while setting the acting user to the target — it was describing an office-issued reset with a
    self-reset's setup, and is now split into both cases.
  - A source-level test asserts the middleware is REGISTERED, and in the right place. Found necessary the hard way: deleting
    the registration left all sixteen behavioural tests green while nothing enforced anything.
  - Collectors and payors are unaffected — neither type sets the flag, and a missing claim means "not required", so older
    tokens and non-admin accounts are never caught by it.


- **NPM daily history for CUSTOM sections** is reachable through the section chooser but has no end-to-end test.
- **Hide or soft-delete an OR number** so a withdrawn receipt stops being reported as missing.
- **The Backups page** has two different sections both headed "Recent backups" — one for in-app restore points, one for CI
  runs. Confusing to read. `Backups.razor:824-826` also contradicts `BackupController.cs:37-43`.
- **A pre-deploy backup gate now exists** (was: a deployment could migrate before a fresh backup existed). Added
  2026-08-14 as a `backup-gate` job in `deploy-production.yml`, between the test gate and the deployment:
  - It asks one question — does this deployment change the database schema? — by diffing the pushed range against
    `EEMOCantilanSDS.Infrastructure/Migrations`. Migrations are applied on API startup, so a deployment carrying one reshapes
    the office's data before anyone can check it.
  - If it does, `backup.yml` is dispatched and WAITED for. Success is a precondition of deploying; a failed or slow backup
    stops the release rather than migrating without one.
  - Deliberately conditional. Most deployments carry no migration, and demanding a dump for every one would add minutes to
    every release and teach the office to ignore the gate.
  - Deliberately unconditional WITHIN that case: no "only if the last backup is older than N hours". A schema change is
    rare, a dump takes minutes, and threshold arithmetic is one more thing to get wrong in the job whose only job is to be
    trustworthy.
  - It fails SAFE. When the range cannot be determined — a manual run, a force-push, a parent missing from the clone — it
    treats the deployment as a schema change and takes the backup. Being wrong that way costs one dump.
  - Verified before pushing: all six workflow files parse; the deploy job's dependency on the gate is asserted; and the
    detection script was run against real history — a code-only range answers "no", the last real migration commit answers
    "yes, 3 files", and all three unknowable cases fail safe.
  - NOT yet exercised end to end, because `.github/**` is path-ignored for deployments, so this commit does not itself
    deploy. The non-schema path runs on the next ordinary release; the schema path runs on the next migration.

- **`restore.yml` now quiesces the API** (was: writes could land mid-restore). Added 2026-08-14:
  - The API is stopped before anything is touched, because it is the only writer — the portal and the collectors' app both
    go through it. Left running, it would accept collections while the restore replaced the very rows they land in, and
    those receipts would be gone with nobody told. It also fixes what the file already warned about: a pool of open
    connections can block the transactional `--clean`, so a restore could fail on a busy morning for no visible reason.
  - The pre-restore snapshot is now taken AFTER the stop, so the one artifact meant to undo the run is actually the state
    the run replaced.
  - Remaining sessions are closed before restoring (own session excluded), since a forgotten psql or pgAdmin window is
    enough to fail the `--clean` after the office is already committed.
  - The API is restarted with `if: always()` and its health asserted, so a failed restore cannot leave the office with an
    outage on top of the problem they were recovering from. A rehearsal skips the stop, the disconnect and the health
    assertion — it never stopped anything.
  - The portal is deliberately left running: it writes nothing, a Head who just triggered a restore should see a site
    reporting trouble rather than a dead one, and it recovers by itself when the API returns.
  - Verified before pushing: all six workflows parse, and the STEP ORDER is asserted programmatically rather than eyeballed
    (stop before snapshot, disconnect between stop and restore, restart after restore and unconditional).
  - EXERCISED END TO END on 2026-08-14, against production, at the office's instruction (the system is not yet in service).
    The API went `Running` → `Stopped` → `Running`, observed from Azure while the run progressed; the log shows the stop, the
    drain, "Closed 0 other session(s)", a successful restore, 29 tables / 65 migrations, and the API answering `/health`
    afterwards. The pre-restore safety dump is retained as an artifact (29,072 bytes, 90 days). A rehearsal was run first,
    against a scratch database, so a logic error would have surfaced there rather than against the live one.

- **Restore rehearsals can now be cleaned up** — `drill-cleanup.yml`. The rehearsal is what makes the recovery procedure
  practisable, but it leaves the scratch copy behind and its summary only told the operator to drop it; nobody could, because
  the database password lives in this repository's secrets and nowhere else. Found immediately after running the first drill.
  A separate file on purpose: `restore.yml` is what the office reaches for on its worst day, and a DROP branch inside the
  recovery tool would sit one input away from the production name. The production database is refused twice, both times
  against the secret rather than a hardcoded name, and the target must look like a rehearsal copy so a typo cannot destroy
  another database on the same server. Both paths were exercised: asked for the production name it refused and SKIPPED every
  later step, and asked for the drill copy it dropped it and proved production still present.
- **`Profile` "Earlier Terms" card** has no component test (no fixture existed when it was written).
- **"Same payor" matching NARROWED to genuine namesakes** (was: exact free-text comparison). Resolved 2026-08-14. A
  slaughterhouse client is the name a clerk typed — there is no client entity — and that name gated the OR-reuse rule. With
  exact equality, entering the second animal of one receipt as "Juan dela Cruz" when the first was "Juan Dela Cruz" made the
  office's own receipt look like another person's and **the OR the office had already written was refused**; a client's
  history and monthly totals also split across spellings. `Domain/Common/PersonName.cs` now defines the rule (trim, collapse
  internal whitespace, ignore case), stored names are canonical on write, and it is applied at:
  the OR-reuse check (`OrNumberRegistry`), the four owner lookups in `SlaughterRepository`, the receipt groupings in
  `DashboardRepository` and `TransactionFeedRepository`, the month-end report, the follow-up "Missing OR" grouping, and the
  three client report groupings. Capitalisation is preserved in storage and display — only comparison ignores it.
  - `20260814080342_CanonicaliseSlaughterOwnerNames` canonicalises whitespace in existing rows. Data only, no schema change,
    idempotent, `Down` deliberately empty. It was **necessary, not tidying**: the owner picker now offers canonical names, so
    a pre-existing double-spaced row would have become unreachable through it.
  - Proven against real PostgreSQL, because `Trim().ToLower()` in a predicate is SQL the in-memory provider never has to
    translate — it answers in LINQ and would pass whether or not the SQL works.
  - **RESOLVED by the office 2026-08-14: within one LGU there are no namesakes.** Two different people sharing a name is a
    problem of national scope, not of a municipality's own client list, so a name IS the client's identity here and no payor
    entity is needed. That closes what was recorded as the remaining half of this item.
  - What still fragments a client is a genuine MISSPELLING (not a recapitalisation) — "Villanueva" entered once as
    "Villaneuva" is two clients, and no rule derived from the name can tell a typo from a different person. That is a
    data-entry correction concern (find and fix the wrong entry), not an identity one, and it needs no schema: the office can
    correct the name and the transactions rejoin. Worth a duplicate-name warning at entry time if the office ever asks.
- **The follow-up header stated the VISIBLE rows, not the whole debt** — resolved 2026-08-14 (`372b7a62`). The Financial
  Report's Attention & Follow-up header read "N accounts need follow-up · ₱X outstanding in full" while counting and summing
  the two lists beside it, which are capped at 50 accounts each (`AttentionLimit`) to bound the payload. An office with more
  than fifty accounts in a bucket was therefore shown fewer accounts and less money than it was owed, under a heading that
  claims completeness; the column counts had the same fault. `FinancialReportDto` now carries `DelinquentAccountsTotal`,
  `DelinquentOutstandingTotal`, `ArrearsAccountsTotal` and `ArrearsOutstandingTotal`, counted before the cap, and a capped
  list says so on the page.
  - It could only appear at scale: below the cap both ways of counting agree, which is why it survived. A test pins that
    agreement so the two paths cannot quietly diverge again.
  - The new fields default to nought, which is its own hazard — a construction path that forgets them shows an empty header
    above a populated list. Both production paths set them (including the All-time view, which builds its DTO from another),
    and a test covers the All-time path specifically.

- **The bare domain answered 404** — resolved 2026-08-14 (`79b471b5`). There was no route for `/` at all (`Home.razor` held a
  commented-out sample) and the router carries no `NotFound`, so `console.stalltrack.site` told the office the page did not
  exist. The root now forwards to `/menu` when signed in and `/login` when not, renders nothing of its own, replaces itself in
  history, and is standalone so the shell is not drawn around a redirect. `"/"` had to be matched EXACTLY in
  `AppShell.IsStandalonePage`: every path contains it, so a substring entry would strip the sidebar from the whole portal.

- **Known small duplications**: `SettleNpmMonthCommandHandler` repeats logic; `FacilityReportsRepository.Revenue.cs` holds a
  static `ConditionalWeakTable` with a stale `asOf`; mobile `_recordsCache`/`_reportCache` are never cleared on logout;
  `FacilityReportsModal.razor` still hardcodes `#4a9eff`; the dashboard computes compliance twice.
- **Absolute redirects are scheme-downgraded to `http`.** Found 2026-08-14 while verifying the root route: the live
  `Location` header is `http://console.stalltrack.site/login`, not `https://`. TLS terminates at Azure's front end, so the app
  sees `http` and builds absolute redirect URLs with that scheme; there is no `UseForwardedHeaders` in the Client pipeline.
  HSTS is set (`max-age=31536000`), so a returning browser upgrades internally, and the `http` URL 301s to `https` anyway —
  but a first-time visitor makes one plaintext request for the URL.
  - NOT fixed on purpose. The remedy is `UseForwardedHeaders` as the FIRST middleware, and on App Service the usual
    configuration clears `KnownProxies`/`KnownNetworks` because the proxy address is not fixed — which means trusting
    `X-Forwarded-*` from any caller. That is spoofable, and it also changes the client IP that rate limiting and the firewall
    rules see. A trade-off for the office to choose, not one to make quietly.

- **The router has no `NotFound`.** Any mistyped URL renders a blank page rather than saying anything. Wants wording and a
  small design rather than a redirect, so it was left out of the root-route change.

- **Mojibake in `Accounts.razor.css` comments** (~a dozen lines, e.g. "ACCOUNTS PAGE â€" Toolbar"), from `0f8f3f02`. Comments
  only — no selector, value or figure affected. Left alone deliberately: rewriting the file to repair comments would bury the
  diff. Worth fixing if that file is edited substantially for another reason.
  - Cause worth knowing, because it recurs: `powershell -File` on 5.1 reads a BOM-less script as ANSI, so em dashes inside a
    script's own string literals reach the files it writes already corrupted. The same thing damaged three comment lines during
    the `CollectorRepository` split and was repaired in the commit after it. Sweep with
    `Select-String -Pattern "Ã|â€"` before committing, and STOP when it reports something.

- **Orphaned CSS** in `FollowUpQueue.razor.css` (~157 unreachable lines). Left deliberately: that file has mixed line
  endings and a bulk rewrite would normalise them and bury the diff.
