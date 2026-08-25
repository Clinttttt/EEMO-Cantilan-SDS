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
- **`IgnoreQueryFilters()` is now PINNED — `CrossTenantReadsAreNamedTests`, 2026-08-16.** It was free to call anywhere: nothing
  failed and nothing warned, and the mistake would not look like one, because a query returning MORE rows than it should reads
  exactly like a query that works. The allowed set is now stated as an allow-list of FILES, each grouped under the pattern it
  belongs to. A new file reaching across tenants fails the build until somebody adds it deliberately, with a reason — the decision
  becomes visible in a diff instead of arriving inside a repository nobody re-reads.
  - **This note said "roughly a dozen" call sites. There are 86, across 37 files.** Every one was read before being pinned.
  - Six legitimate patterns: PRE-AUTH IDENTITY (login, activation, reset, verification, refresh and device tokens — no tenant is
    resolved yet, so the LGU is derived FROM the record found, each looked up by a globally unique secret and pinned by the handler
    afterwards); PLATFORM OPERATOR (gated by `PlatformOperatorPolicy` before reading); GLOBAL REFERENCE DATA (`Municipalities` is
    not tenant-owned — a municipality cannot be scoped to itself — read by the caller's own id or code); SEEDING AND STARTUP (no
    tenant exists yet); SOFT-DELETE ONLY (`OrNumberRegistry`, `AdminRepository`, `CollectorRepository` re-apply
    `MunicipalityId == mid` BY HAND — a cancelled receipt's OR number is still spent, a deleted account's username still taken);
    and WHOLE-DATABASE WORK (export/restore, operator-only by definition).
  - Granularity is per FILE, not per line or count. Line numbers churn on every edit and counts invite blind bumping; a file is
    either an established cross-tenant boundary or it is not.
  - Three assertions, each proven load-bearing by reintroducing the defect: an unnamed file is caught (with file and line named in
    the message); a DEAD allow-list entry fails, because a name outliving its reason silently re-permits the next read added to
    that file; and the scan itself must find the audited volume, because a test that finds nothing passes as quietly as one that
    finds nothing wrong — the lesson `TenantFilterCoverageTests` taught when it passed with an entity deliberately excluded.
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

### 2. Split the oversized repositories — DONE

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

  `PaymentRepository` 53KB → three partial files (2026-08-15): entry 9.9KB (the payment aggregate, per-facility record reads
  and the receipt-number availability checks), `.Ledger.cs` 36.2KB (one account's history, summary, outstanding months and
  collection history, carrying the obligation arithmetic they share) and `.MissingReceipts.cs` 10.6KB (money taken whose OR is
  still blank). Same two-step method; 615 code lines in, 615 out, IDENTICAL.

  **ITEM 2 IS NOW COMPLETE.** Contracts and implementations are both split. No file among the three exceeds 52KB, and the
  largest remaining single file is `CollectorRepository.Mobile.cs` — one 620-line method (`GetCollectorReportAsync`) accounts
  for most of it, and breaking THAT up is a redesign of one query rather than a file move, so it is deliberately not attempted
  here.

  A note for whoever does the next one: `IsORNumberUniqueAsync` on `PaymentRepository` is public but on no Application
  contract. It is genuinely used — by `UtilityBillRepository` and the composition tests — so it was left alone, but it is a
  seam nobody has named.

**Item 3 (password hashing out of Domain) is recorded above**, in the position it was actually done in. It appeared here a second
time as a duplicate `### 3` heading, which made the list read as though there were two item 3s.

### 4. Move `Result<T>` and paging models out of Domain — DONE

`Result<T>` and `CursorPagedResult<T>` now live in `Application.Common`. Domain never referenced either, so no rule changed;
what changed is that the domain no longer carries a type named after HTTP outcomes (`Unauthorized`, `Conflict`, `NoContent`)
or a paging contract. An architecture test asserts both — that they are absent from Domain AND present in Application, so it
cannot pass by their having been deleted.

497 files use these types. Rather than add a using line to every one, each consumer project declares the namespace globally
via `<Using Include="EEMOCantilanSDS.Application.Common" />`. The move is about where the types BELONG; a diff touching every
handler would have buried that. Two accidental usings went with the move (`System.Xml.XPath` and a JS-interop static import
that had no business in a result type).

DONE for the layer that mattered (2026-08-15): Application and Infrastructure no longer name HTTP status codes.

`ResultStatus` states the KIND of outcome in the office's terms — `Conflict` for "it already exists", `NotFound`, `Forbidden`,
`Locked`, `UpstreamFailed` for "something we depend on failed" — and `ApiBaseController.HandleResponse` switches on THAT and
owns the response shape. 133 sites across 48 files lost their bare 400/401/403/404/409/423/500/502, and none remain.

The HTTP responses are unchanged, and that is the point: every one of those statuses is read by something. The portal branches
on Conflict to say a username is taken, on NotFound to say the account is gone, and treats Unauthorized and Forbidden as "your
session ended" rather than an error to display; the mobile app and the sync path read them too.

Sequenced so that could be proved rather than hoped:

1. **The characterisation test came first.** `Testing/Api/HandleResponseContractTests.cs` pins every status the API returns,
   including which failures carry their message in the body and which deliberately say nothing (401 must not hint whether an
   account exists; 404 has nothing to add), and the per-field shape of validation errors. Written and passing BEFORE the
   translation moved, so it describes the old behaviour, not the new code's opinion of it.
2. **`Result<T>` carries the status; `StatusCode` is DERIVED from it.** Nothing that reads the number had to change — and the
   portal legitimately speaks in numbers, because `HttpClients/HandleResponse.cs` rebuilds a `Result` FROM a real HTTP
   response. `Result<T>` was never the wire contract, which is what made this safe; had it been serialised, the portal would
   have needed changing in lockstep.
3. The numeric `Failure(message, int)` overload remains for exactly those callers, and maps to the same category by the same
   table, so the two can never disagree.

Proven load-bearing by mis-mapping one category (Conflict to BadRequest): only the 409 case failed.

DONE (2026-08-16 finished the tail). Nothing outside the API's own boundary speaks in HTTP numbers any more.

The PORTAL was converted 2026-08-15: all 14 of its comparisons now read the category — `Status == ResultStatus.Conflict` rather
than `StatusCode == 409` — across Accounts, Menu, Report, Settings, Transactions, ExportData, FollowUpQueue, MonthEndReport,
PastFollowUpQueue, StallHolderList, TwoFactorPanel and AuthProxyController.

The TESTS followed: 100 assertions across 44 files now assert the category a handler STATED rather than the number it translates
to. For a handler-produced result the category is the source of truth and the number is derived, so this asserts the thing itself.

Three places keep speaking in numbers, deliberately:
- `HandleResponseContractTests` — it IS the HTTP contract, so it must assert statuses.
- The middleware tests — those read `HttpContext.Response.StatusCode`, a real HTTP response.
- `ResultStatusMappingTests` — it pins BOTH directions, including the 429 that has no category.

Verified equivalent before converting, not after: every site compared against a code that maps one-to-one. **The one lossy
direction was checked and avoided** — an HTTP status nobody maps (429, which the rate-limited sign-in and password-reset
endpoints really do return) falls to `Invalid`, so rewriting a `StatusCode == 400` check as `Status == Invalid` WOULD silently
treat a throttled request as a bad one. `ResultStatusMappingTests` pins that trap explicitly.

`Result<T>.StatusCode` stays: `HttpClients/HandleResponse.cs` builds a Result FROM a real HTTP response, where the number is the
input.

### 5. Replace `IAppDbContext` feature by feature — BOUNDARY PINNED 2026-08-17; the sweep is deliberately NOT done

**What was done: `ApplicationEfBoundaryTests`.** EF sits in **38 of Application's 790 files**, and it can no longer spread to a 39th
without somebody adding the name deliberately. That is the part of "Application free of EF" that carries the architectural value, and it
carries none of the risk.

**Why the conversion itself was measured and then declined as a sweep.** The obvious move — point each of the 35 handlers at the
repository interface that already exists — is NOT a swap. The clearest case proves it:

| | Query |
|---|---|
| `GetMyOfficeProfileQueryHandler` | `context.Municipalities.IgnoreQueryFilters().FirstOrDefaultAsync(...)` |
| `IMunicipalityRepository.GetByIdAsync` | `context.Municipalities.AsNoTracking().FirstOrDefaultAsync(...)` |

They differ on the query filter, and **`Municipality` IS soft-deletable**, so the filter is real: the handler finds a soft-deleted
municipality and the repository does not. Swapping them would quietly turn a loaded office profile into a 404. Verified, not assumed —
`ApplyQueryFilters` applies `!IsDeleted` to every `AuditableEntity`.

Every one of the 35 needs that comparison made, and they cluster in **auth, account recovery and onboarding** — where a silent behaviour
change is worst and least visible. Against a benefit that is architectural rather than behavioural, a sweep is the wrong trade.

**It is still worth doing per feature**, when a feature is being changed anyway and its queries are being read properly. The allow-list
makes that progress visible: convert a handler, and `TheAllowedSetHasNoDEADEntries` tells you to remove its name. When the list empties,
the EF package reference can come out of Application and the review's original test becomes free.

The clusters, for whoever picks one up: onboarding/assessment (12), auth/recovery/MFA (11), rates and OR series (6), municipality profile
and payment settings (4), platform operator (2), online payments (2), plus the seam itself and the paging helper.

37 call sites, 38 EF imports in Application. Do NOT run as a campaign: convert a feature only while already changing it.
Each one risks a behaviour change in authentication or onboarding. Remove the EF package reference from Application only
after the last caller goes.

### 6. Extract a Contracts project — CLOSED 2026-08-17, the office decided the current setup stands

**Decision: leave it as it is. The portal keeps posting command types, and the compile-time wire check is the reason.**

The measurement below is what the decision rests on, so it is kept rather than deleted. `[FromBody]` binds the COMMAND TYPE itself on
70 endpoints, which is what makes the portal's wire contract checked by the compiler end to end: add a parameter to a command and the
portal stops building, before anything is deployed. Accidental, but real protection on a money path.

Giving the portal its own request models while the API went on binding commands would have removed that check and replaced it with
nothing — drift appearing at runtime as a field the API silently ignores, a required record parameter arriving absent as a 400, or a
decimal quietly defaulting to zero. Doing it safely meant the API binding the same models across all 70 endpoints: a large change
whose main benefit was tidiness, weighed against a guarantee that already works.

**If this is ever reopened**, it is only worth doing in that safe form — shared request models bound by BOTH sides, staged one
controller at a time — and never as "the portal gets its own models" alone. The architecture test that wanted this (no API-client
interfaces in Application) stays unbuilt for the same reason; that is now a deliberate gap, not an oversight.

~107 DTOs, 29 typed API-client interfaces and 19 request files sit in Application, so HttpClients, Blazor, MAUI and the
tests depend on the whole assembly. Correct in principle and the largest single change on the list; do it after the
boundaries are right.

**Measured 2026-08-16 before starting, and it changed the plan.** Moving the DTOs alone achieves nothing. The consumers use
`Application.Command` types as heavily as DTOs — 63 references in HttpClients and 61 in the portal — because the portal POSTS
command types as its request bodies. The commands ARE its wire contract.

So freeing the consumers from Application means separating each command from its handler and from its MediatR
`IRequest<Result<T>>` binding: either MediatR comes into Contracts, or the portal gets its own request models and something maps
them. That is a redesign of how the portal talks to the API, not a file move, and no compiler-verified mechanical stage gets there.

Whoever picks this up should decide FIRST which of those two it is. Until then the DTO move on its own is churn with no benefit —
the consumers would still reference Application for the commands.

**The office chose option (b) — the portal gets its own request models — and a further measurement 2026-08-16 shows that option
carries a condition that must be decided with it.**

`[FromBody]` binds the COMMAND TYPE ITSELF on 70 API endpoints (31 others already bind a purpose-made request type). That is what
makes the wire contract compile-checked end to end today: add a parameter to a command and the portal stops building, before
anything is deployed. It is accidental, but it is real protection on a money path.

Giving the portal its own request models while the API goes on binding commands would REMOVE that check and replace it with
nothing. The two shapes would then agree only by convention, and drift would appear at runtime as a field the API silently
ignores, a required record parameter arriving absent as a 400, or — worst and quietest — a decimal defaulting to zero. A rate, a
duration or an amount could go missing on a form that still reports success.

So option (b) is only safe if the API binds the SAME request models the portal posts, with the handler mapping request → command.
That is the whole 70 endpoints, and it is what makes the change worth doing rather than merely tidy: it separates the wire
contract from the MediatR message, which is the actual coupling.

**Decision needed before any code moves:** does the API bind the new request models too (safe, ~70 endpoints touched, compile-time
checking preserved on both sides), or does the portal define models the API does not share (smaller, and silently
drift-prone — not recommended)? If the answer is the first, this can be staged one controller at a time, each stage compiling and
testable, which is the only way a change this size stays verifiable.

One thing that helps and is cheap whenever it happens: the namespaces can stay as they are. `Result<T>` moved to
`Application.Common` with consumer projects declaring the namespace globally rather than editing 497 files, and the same approach
keeps a Contracts move reviewable — the assembly changes, the namespace does not.

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

### 8. Inject time instead of static clocks — DONE on the server (Client display defaults remain)

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

DONE for the whole SERVER (2026-08-15). Domain now reads the static clock in ZERO places.

The last three were `Contract.IsExpired`, `Contract.IsExpiringSoon` and `Terminate`'s default end date. An entity cannot be given
a constructor dependency, so the two properties became methods taking the date — `IsExpiredOn(DateOnly asOf)` and
`IsExpiringSoonOn(DateOnly asOf)` — and `Stall.IsContractExpired()` took a date with them, since it delegates.

Why methods rather than a convenience property that reads the clock: this decides whether a contract still ACCRUES, and the
office asks it of past months as well as of today. A register for June cannot be answered with August's opinion, and while it
was a property no test could state a date at all. 14 new tests pin the boundary the office's own paper takes — a term effective
the 7th runs THROUGH the 7th N years on — plus the renewal window, that expired and expiring-soon are never both true, and that
an open-ended space never quietly falls due. Proven load-bearing: making the method ignore its parameter failed 7 of the 14.

Smaller findings from the same pass:

- **The blast radius was a tenth of what it looked like.** A search for `.IsExpired` returns ~30 hits, but almost all are
  `ContractAttentionDto.IsExpired` (already computed as-of a date by the repository) or `OnboardingDraft.IsExpired` (a token,
  different type). Only `Stall.IsContractExpired` and three test assertions actually used the entity's property. Worth checking
  the receiver's TYPE before sizing a change like this.
- **`Terminate`'s `?? PhilippineTime.Today` default was dead.** All 29 call sites already passed an explicit date, so the
  parameter is now required. The end date decides which months belong to the outgoing lessee and which to the incoming one, so
  defaulting it to "the day the clerk did the paperwork" was never right.
- **The two EF `builder.Ignore` lines for these properties were removed** — a method cannot be mapped, so the model is
  unchanged and no migration is involved.
- **`AddMonths(3)` in the entity is now `DomainRules.ExpiringSoonMonths`**, which is 3. The handlers already passed that
  constant to the repository while the entity hardcoded the number.

RESOLVED 2026-08-16 on the office's ruling, and now ONE rule:
- **Two expressions of the expiry rule — unified.** `Contract.IsExpiredOn(asOf)` was `asOf > ExpiryDate` while
  `DomainRules.TermHasExpired` additionally guarded `durationYears > 0` and `!= OpenEndedTermYears`. They agreed on every
  contract `Create` produced but DISAGREED on a signed term of zero years: the entity called it expired the day after it began,
  the rule called it not expired. The office ruled such a contract INVALID, so the state is refused at entry and the entity now
  DELEGATES to `DomainRules.TermHasExpired` — there is no longer a second answer to disagree with.
  - Refused in three places, each for a different reason. `Contract.Create` and `Contract.UpdateTerms` refuse a signed term of
    nought years so the state is unrepresentable rather than merely unreached. `UpdateStallCommandHandler` answers the same case
    with a stated reason, because a domain exception would reach the office as a server error — and it is the only screen that
    could ever have set an existing term to nought.
  - **NOT in the validator, and that is deliberate.** Requiring at least one year there was tried first and would have broken a
    legitimate edit: the stall DTO reports `activeContract?.DurationYears ?? 0`, so a stall with NO active contract reports
    nought years, and both edit forms pass whatever they were handed straight back. The command does not carry the arrangement,
    so a validator cannot tell "no term to state" from "a term of nought years". `UpdateStallContractYearsTests` pins the
    validator's acceptance of nought so it cannot be tidied away.
  - An occupancy WITHOUT a signed contract keeps the open-ended sentinel whatever number arrives, so correcting a space-only
    row's effectivity date is still possible and cannot leave it holding a nought-year term.
  - **Stored rows written before the invariant remain possible** and are handled safely: EF materialises them without the
    factory, and both rules now say NOT expired, while `BillsCalendarMonth` still says they owe nothing. So such a row generates
    no demand and is not reported as an expired contract needing renewal. Production was NOT queried for them — the database
    password is only in CI secrets — but every application path already refused nought-year signed terms before this change
    (create form, stallholder import, renewal, assigning a past occupant), so the only way one could exist is a hand-written row.
    **If the office ever reports a contract showing no term, that is the row to look for.**
- `StallContractStatus`'s parameterless `IsCurrentVendor(dto)` overload still reads the static clock. It delegates to an
  `IsCurrentVendor(dto, today)` that IS testable, and its single caller is in the Client — so it belongs with the Client bucket.
- **The Client's clock reads — AUDITED 2026-08-17, and the audit is the answer.** The count was **241 sites in 62 files**, not ~205:
  `PhilippineTime.Now` 147, `PhilippineTime.Today` 58, `ToPhilippineTime` 29, plus 7 raw `DateTime.*`. Every one was classified before
  anything was changed, and **only one was a defect**. Converting the rest would be churn.
  - **The reason most of them are fine is worth stating, because it is easy to forget: this is Blazor SERVER.** All of it runs on the
    server, so `PhilippineTime.Now` is the server's clock in Philippine time — there is no browser clock to distrust, and none of
    these is the "untestable static clock" problem the server-side work was about.
  - **80 display or formatting** (`ToString`, interpolation, year/month labels). **114 field and property initialisers** — form
    defaults and date-picker seeds, e.g. a slaughter transaction's date defaulting to today. **Calendar and navigation seeds**
    (`DailyCollectionCalendar`, `Report`, `CollectionExceptions`, `LiveClock`, `Transactions`' future-date clamp) legitimately want
    the real today. **2 API arguments**, both asking for the current month's view in `Profile`. All correct as they stand.
  - **7 raw `DateTime` sites, now 4, none ever a defect.** The four that remain are UTC-to-UTC comparisons that must stay UTC (JWT
    expiry ×2, a health span, a backup trigger timestamp). The other three went with the dead code below.
  - **Both dead spots DELETED 2026-08-17**, for the same reason the admin console's seeded records went: dead data that reads as real
    is worse than dead data.
    - `PaymentSubmitDto.SavedAt` was stamped with `DateTime.UtcNow` and read by nobody — no consumer of `OnSave` ever looked at it, so
      it recorded a time that answered no question. When a payment is recorded is the server's to stamp, not a modal's.
    - `PayorDemoData` was **referenced nowhere** and fabricated a named payor with outstanding balances (₱2,400 unpaid, ₱1,200
      partial) and a payment history carrying **OR numbers** — 124533, 120114, 118402 — against a REAL facility, Tampak Commercial
      Center. Invented receipt numbers have no business sitting in a revenue system's source, even unreferenced.
  - **7 sites evaluate contract expiry in the portal** (`DomainRules.TermHasExpired` in six facility pages and one import screen).
    Left alone deliberately: they call the SHARED domain rule, so there is no second opinion — only a display badge computed from it.
  - **THE ONE DEFECT: "Expiring soon" on the stall profile.** It wrote the renewal window as a literal `3` months AND omitted the
    "not already expired" half of the rule, so **a term that ran out two years ago was badged "Expiring soon"** — the one thing that
    badge exists to distinguish. `Vendor.razor` had the logic right but also hardcoded the 3. Both now use
    `DomainRules.ExpiringSoonMonths`, and the profile matches `Contract.IsExpiringSoonOn`.
    - `ExpiringSoonWindowTests` pins the rule at its boundaries AND asserts against the markup that neither screen holds its own
      copy — the domain tests alone stayed green while the page mislabelled an expired term, which is why the source assertions
      exist. Both faults proven caught by reintroducing each one.
- **Audit stamps stay put.** ~150 `DateTime.UtcNow` in Domain are `CreatedAt`/`UpdatedAt` assignments; they belong with the
  interceptor.

### 9. Split API / Infrastructure / Client registrations — DONE

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

DONE (2026-08-15 completed the reshuffle). `AddInfrastructureService` is now a seven-line composition over seven groups that
each register ONE concern: `AddPersistence`, `AddEemoCaching`, `AddTenancyAndRates`, `AddRepositories`,
`AddInfrastructureServices`, `AddOnlinePayments`, `AddBackupGateway`. It was a single 150-line method mixing all of them, so
nothing could be changed without reading all of it and a dropped line looked like every other line.

**The blocker recorded here — "no test can prove it beyond the application still starts" — was removed first, deliberately, then
the reshuffle was done against it.** `Testing/Infrastructure/CompositionRootTests.cs`:

- **Resolves every service this codebase registers** — 550 of them, including every MediatR handler and every validator — from
  the real composition, built in the same order as `Program.cs` (`AddApi` + `AddInfrastructureService` + `AddApplicationService`).
  A group left uncalled is a failing test rather than a 500 on the one page that needed it. Proven by deleting the `IClock`
  registration: 66 of 549 services became unbuildable.
- **Asserts no service type is registered twice.** This is what makes moving registrations between groups SAFE: with each type
  registered once, order cannot decide which registration wins, nor what `IEnumerable<T>` yields. Had there been a duplicate, a
  reshuffle could have altered the application with every other test still green.
- Deliberately NOT `ValidateOnBuild` over the whole container: that also walks the framework's descriptors (SignalR's
  connection dispatcher, MVC's result executors, Swagger's options), which need pieces only a real `WebApplication` provides.
  Stubbing those would chase a moving target and prove nothing about this codebase.

The reshuffle itself was verified by comparing the container before and after: 551 registrations, same service types, same
lifetimes, same implementations — IDENTICAL. (Done with a temporary snapshot test, removed once it had served.)

One finding, recorded not fixed: **the PayMongo gateway throws at RESOLUTION when `PayMongo:BaseUrl` is unset**, and three
online-payment handlers depend on it. In a deployment missing that key those three endpoints fail at request time rather than at
startup — fail-late where fail-fast would be kinder. The composition test supplies the key so it represents a configured
deployment; making startup refuse instead is a separate decision.

### 10. Strengthen the architecture tests — DONE, with two deliberate gaps

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
- **Application free of EF** — answered as a BOUNDARY, not an absence: `ApplicationEfBoundaryTests` pins the 38 files that use EF and
  fails when a 39th appears. The full absence still needs item 5's per-feature conversion, which was measured and declined as a sweep;
  the reasoning and the evidence are under item 5. The allow-list emptying is what would make the original test free.
- **No API-client interfaces in Application** — will not be built. It needed item 6, which the office closed: the portal keeps posting
  command types because that is what makes the wire contract compile-checked across 70 endpoints. A deliberate gap, not pending work.
- **Cross-tenant services explicitly named** — DONE. `CrossTenantReadsAreNamedTests` pins the 86 `IgnoreQueryFilters()` call
  sites, by file, each under the pattern that justifies it. See item 1's residuals for the audit.
- **API policy and Application authorization share one authorizer** — unified behind `PlatformOperatorPolicy` in `8d58fc9`,
  but asserting it structurally means reading an authorization-policy lambda. Judged too brittle to be worth it; the
  behavioural tests in `PlatformOperatorGuardTests` cover the rule itself.

### 11. Reorganize Application into feature folders — CLOSED 2026-08-18, the office decided the current structure stands

**Decision: keep it. This is CQRS over Clean Architecture, not vertical slices, and the current layout is the coherent expression of
that.** Feature folders are a vertical-slice idiom; adopting them here would half-adopt an architecture this system does not use.

Checked before agreeing, because "leave it" deserves evidence as much as a change does:

- `Command/` (262 files) and `Queries/` (275) are **already grouped by feature** — Auth, Onboarding, Payments, Rates, Collectors,
  DailyCollections, Municipalities, and so on.
- Each use case **already has its own folder** holding its command, handler and validator together, e.g.
  `Command/Payments/BulkImportDailyHistory/` contains exactly `BulkImportDailyHistoryCommand.cs`,
  `…CommandHandler.cs`, `…CommandValidator.cs`.
- `Dtos/` (107 files) **mirrors the same feature names** — `Dtos/Auth`, `Dtos/Onboarding`, `Dtos/Payments`.

So the shape is layer → feature → use case, and a use case is already co-located. The only thing a vertical slice would add is putting
each DTO in the same folder as its handler — and since the DTOs are already grouped under the same feature names, that is a small gain
against moving roughly **660 files** and changing every namespace in the layer.

**What would change this.** If the team ever moves to vertical slices deliberately, this is the change to make, and it should be made
wholesale rather than as a partial migration that leaves two conventions side by side. Short of that, the current structure is not a
compromise — it is the right answer for the architecture in use.

**The architecture backlog is now closed.** Items 1, 2, 3, 4, 7, 8, 9 done; item 5 answered as a pinned boundary with per-feature
conversion left as opportunistic work; items 6 and 11 closed as decisions with their reasoning recorded; item 10 done except the two
tests that 5 and 6 would have unlocked, which are now deliberate gaps rather than pending work.

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

**The sheet is a Monthly Collection Report, not a month-end one.** Renamed 2026-08-22 at the office's request, after they
pointed out the title claimed something the document does not do: nothing about it waits for the month to close, and the
office opens August on the twenty second. A closed month prints its period plainly; a month still running prints "as of"
the day the figures were taken, so a filed copy cannot be mistaken for the final position. The route
(`/reports/month-end`) and the C# type names were deliberately left alone — a bookmark should keep working, and renaming
`MonthEndReportDto` would be churn the office never sees.

**No LGU holds destructive power over another's data.** Raised by the office itself (2026-08-20) and acted on 2026-08-21.
The platform operator is now an account carrying the `IsPlatformOperator` flag and nothing else: `PlatformOperatorPolicy.IsOperator`
takes one argument and returns it. Until then the policy also accepted `isDefaultTenant && role == SuperAdmin`, a documented
fallback from when the default municipality was the only one on the platform. That clause made one municipality's Head the operator
over all of them, including `POST api/backup/restore`, a destructive restore across the whole shared database with every LGU's
records in it. The Head keeps its own per-LGU backup and restore, exactly like every other Head, and loses `BackupController`'s six
endpoints and the nine `PlatformOperatorGuard.IsCurrentAsync` sites (the onboarding pipeline and the two operator queries).
`AdminAuthController`'s two MFA endpoints are `Roles = SuperAdmin` and were never gated by the policy. Console sign-in already
read the flag directly, so `admin.stalltrack.site` is unchanged by it. The full record, including what was verified before
deleting the clause and which tests were inverted, is under "Retired work" below.

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

**A collector's feed answers for the money, the office's reports answer for the day.** Established 2026-08-24 when the office
settled three payors' Aug 22, 23 and 24 in one afternoon and found ₱30 on the app's Records tab against a ₱90 receipt. Two
bases coexist deliberately. The mobile Records feed is a cash view: every source in it, including NPM daily fees since this
date, is selected on when the money was taken, and a receipt shared by several days reads as one card carrying the whole
amount with the days it covered named in its detail. Every facility report is an accrual view: a fee counts in the period the
day it covers falls in, which is why a late-settled day turns its own calendar cell green and why the trend showed ₱90 on each
of the three days rather than ₱270 on one. Nothing in the facility report repositories reads a record timestamp, so the two
never disagree by accident. The caption over the trend was corrected the same day; it had said "recorded collections".

**One entry per payor on the collector's Records feed.** Decided 2026-08-24 after the office saw one payor twice on the same
day, ₱90 on one receipt and ₱30 on another, and asked for the name to appear once. An entry is a payor at one stall for the
day, whatever number of receipts they were given, and it states the money that payor handed over. Nothing the office answers
for is collapsed: the entry holds each receipt intact and the detail names every one against its own time and amount, with
the days or months it covered underneath. Never merged, and each refusal is tested: an absence (a ₱0 statement, not money),
an office-recorded entry (attribution must stay plain), and two stalls of one payor (the office reads a stall). The rule
lives in `EEMOCantilanSDS.Mobile.Core/Records/CollectorRecordGrouping.cs` rather than the razor page precisely so it can be
tested, the mobile UI having no automated coverage; `CollectorRecordGroupingTests` is the record of it.

**The three billing rules** stand as implemented, per the same interview: a term of N years owes exactly N × 12 months'
rent; an expired contract stops accruing rent but keeps its balance collectable; a current or yearly market report counts
only the market days elapsed as of the report date.

---

## Open questions for the office

These cannot be answered by reading code.

1. **Two Postgres firewall rules — REMOVED 2026-08-17** on the office's instruction. They opened `180.194.5.178` and
   `180.195.158.234` indefinitely, for machines nobody uses. `AllowAllAzureServicesAndResourcesWithinAzureIps` remains, which is how
   the API and the backup workflow reach the database — verified straight after removal by `/health/ready` answering `ready` and
   `/api/municipalities` still reading. A one-off inspection now needs a temporary rule for the current address, which is the
   sequence recorded in `ONBOARDING_FLOW.md`.

---

## Retired work

Items that were open and are now closed, kept because the reasoning is what stops them being reintroduced.

- **The 0 KB collector download — FIXED 2026-08-22.** Collectors scanning the QR and tapping Download were sometimes left with a
  0 KB `.apk`. Measured against production before changing anything: **four of eight** full downloads of the 41 MB file returned
  Azure Static Web Apps' own "500 Internal Server Error" page. The file itself was sound — `HEAD` answered 200 with the right
  `content-type`, ranged requests answered 206 with exact byte counts, and the body began with a real `PK` archive signature — so
  it was the host failing on the large body, not the file, the MIME type or the config.
  - The APK is now attached to a **GitHub Release** under a fixed asset name, so
    `/releases/latest/download/stalltrack-collector-latest.apk` always resolves to the newest build. Verified after publishing:
    **eight of eight** downloads returned all 43,082,581 bytes.
  - `publish-apk.yml` no longer stages the APK into the site, so the failing host never serves it. The workflow needs
    `contents: write` for the release and nothing else. The old path redirects (302) to the release, verified, so links already
    shared keep working — which also means the API's built-in default URL stays correct without any app setting.
  - `Mobile__DownloadUrl` on the API points straight at the release so the in-app update check follows no redirect. The
    release asset is named **`stalltrack-collector-latest.apk`**: `/releases/latest/download/stalltrack-collector.apk`
    answers 302 and then 404s, so a check that stops at the redirect proves nothing. Verified 2026-08-25 by following the
    redirect: the configured name returns 200 at 43,115,349 bytes.
  - **Published builds:** `collector-1.1.1-3` (the arrears sheet answering for its own day), `collector-1.1.2-4` (the
    Records card reading the whole receipt, the payor's own area on it, the market round's area chips),
    `collector-1.1.3-5` (one entry per payor with every receipt named inside it) and `collector-1.1.4-6` (Profile states
    v1.1.4 rather than "v1.1.3 (5)", and the update prompts name the version on offer). All 2026-08-24.
  - `Mobile__LatestVersionCode` / `Mobile__LatestVersion` were raised from 2 / 1.1.0 to **6 / 1.1.4** on 2026-08-25 at the
    office's instruction, which restarted the API. `api/mobile/version` confirmed afterwards, and both containers were
    re-checked against HEAD. Devices below versionCode 6 are now prompted to update.
  - The 41 MB binary also stopped being tracked in git; the release holds it and `mobile-app-site/download/*.apk` is ignored.

- **The platform-operator fallback clause is RETIRED — done 2026-08-21.** `PlatformOperatorPolicy.IsOperator` now takes one
  argument and returns it: the `IsPlatformOperator` flag on the account is the whole rule. Until this change the policy also
  returned true for `isDefaultTenant && role == SuperAdmin`, which made the DEFAULT municipality's Head the platform operator, and
  therefore let one municipality's Head trigger `POST api/backup/restore` — a destructive restore over the whole shared database,
  every LGU's records included. The office raised it itself (2026-08-20): a single LGU should hold no destructive power over
  another's data. The clause was a documented fallback from when that municipality was the only one on the platform, and its own
  remarks named the condition to delete it on.

  That condition was met: the office created its operator at `admin.stalltrack.site/setup` and confirmed it signs in. Verified
  before deleting the clause that this would not lock everyone out — `TokenService` puts the `PlatformOperator` claim on the token
  for any account carrying the flag, which is the fact the API's policy reads, so the dedicated operator satisfies both the API
  policy and the database-backed guard.

  What the default office's Head lost, and it is deliberate: `BackupController` (6 endpoints), and the nine
  `PlatformOperatorGuard.IsCurrentAsync` call sites — the onboarding pipeline (assessment approve/decline, validation approve,
  return-to-draft, activation) and the two operator queries. `AdminAuthController`'s two MFA endpoints are `Roles = SuperAdmin`
  and were never gated by the policy, so they are unaffected. The Head keeps its own per-LGU backup and restore, exactly like
  every other Head.

  Changed in the same commit, or the portal would have contradicted the API: `Settings.razor` decided
  `_isPlatformOperator = isSuperAdmin && Branding.IsDefaultTenant` locally, and now calls the policy the way `Backups.razor` does.
  Left alone it would have gone on showing the default office's Head whole-database controls the API refuses, and shown the real
  operator none of the ones it allows. `PlatformOperatorGuard.IsCurrentAsync` no longer reads the caller's municipality at all,
  since the question no longer turns on it.

  Two tests that existed to assert the fallback were inverted rather than deleted, so the refusal is now what is pinned:
  `PlatformOperatorGuardTests.TheDefaultMunicipalitysHeadIsRefused` and
  `ConsoleAdminHandlerTests.Guard_DefaultMunicipalitysHead_IsRefused`. `PlatformOperatorPolicyTests` also asserts that the rule
  takes exactly one argument, so a future clause about a role or a municipality cannot be added quietly. Four onboarding test
  classes had been acting as the default municipality's Head to reach activation and approval; they now seed a real operator
  account, which is how the platform itself creates one (under the default municipality's id, per
  `CreateFirstConsoleAdminCommandHandler`).

  Console sign-in was already reading the flag directly rather than the policy, so `admin.stalltrack.site` behaviour is unchanged
  by this; the comments in `LoginCommand` and `LoginCommandHandler` explaining the old divergence were corrected.

## Deferred product work

- **Report of Collections (per collector) — WIRED TO REAL FIGURES 2026-08-25, at `/collectors/{id}/report`, opened from the
  document action on the collector's row.** The office asked for a collector report and chose the treasury wording for its
  title. What was agreed, after checking what every existing page already answers: the document is one collector over one
  period, and the four things nothing else answers are a per-collector facility breakdown, a daily record whose "For Earlier
  Days" column explains a day whose collection exceeds what that day could owe, the complete receipt listing that is ticked
  against the booklet, and the absences that collector marked. Deliberately excluded: payor balances and follow-up lists
  (the facility reports own them), facility revenue against expected (same), and any comparison with other collectors,
  because a ranking is not what an accountable officer signs. Utilities keep their own table, a meter charge not being a
  stall or daily fee. Period tabs are Daily, Weekly and Monthly with Monthly opening, a day being a cash view and a month
  the accountability view. The office ruled OUT stating an amount in words.
  - Every figure comes from ONE query (`GetReportOfCollections`), so the summary, the facility breakdown, the daily record
    and the receipt listing cannot disagree. `CollectorReportTests` pins that the receipt listing adds up to the summary and
    that the reconciliation strip agrees with itself.
  - Receipt numbers are typed one by one, so a from-to range prints only where the numbers are numeric and run unbroken; a
    plain count stands in its place otherwise. Tested three ways.
  - "Days with collections" is a count, NOT "so many of so many": what a collector could have collected depends on each
    facility's own calendar, and a figure the document cannot substantiate has no place on it.
  - The signatory footer needs its own three columns on every sheet. `SignatureStrip` declares `display: contents` on
    purpose, handing layout to its host, so a sheet that omits `.print-report-signatures` gets the lines stacked down the
    page. That is what this document did until the wrapper was added, and a test now pins the slots inside that footer.
  - **Remittance — BUILT AND THEN RETIRED ON THE OFFICE'S DECISION, 2026-08-25.** The office answered the five questions: a remittance covers a DATE RANGE of collections, it may never exceed what was
    collected (a refusal, not a warning, in their words bad design otherwise), electricity and water are banked separately as
    additional income and are therefore outside it, the Head and Administrators are the ones who record it, and a reference
    number is optional but its absence is reported back. `CollectorRemittance` is additive: a new table, two indexes and a
    restricted key to the collector, so production applies it at startup without touching anything that exists.
    - Two rules make the figure exact rather than a guess. Coverage ranges of one collector may not overlap. And the money is
      matched on WHEN IT WAS TAKEN, never on the day a fee was for: match on the fee day and a payor settling owed days
      leaves cash that can never be remitted, since its day already sits inside an earlier remittance.
    - A part payment on a monthly bill is applied to the fee charge first and capped there, the excess belonging to the
      separately banked utilities, because the received amount carries no split of its own.
    - It is never deleted. A mistake is voided with a reason, which frees its days for the correct record.
    - `CollectorRemittances` is in `TenantDataTables.Restorable` and the export: a restore that reinstated the collections
      without it would show every peso as still in a collector's hands. The architecture test caught that omission.
    - Proven by injection: with the ceiling rule neutered the refusal test fails, and with utilities counted as fee money two
      repository tests fail.
    - Recorded from the office side: `GET`/`POST api/Collectors/{id}/remittances`, both `SuperAdmin,Admin` while the rest of
      the controller stays Head-only, and a drawer opened from the collector's activity view that states the three figures
      before an amount is typed. A refusal is shown as the server wrote it, since those messages name the figures and the
      days another remittance covers.
    - One definition of fee money serves the report: `CollectorFeeMoney.MonthlyFeePortion`. A document whose summary and its
      own tables disagree would be worse than one that states a single figure and says what it excludes.
    - **RETIRED 2026-08-25, the same day, by the office's decision.** They concluded that what the collector records on the
      phone IS the basis, that the office handles the handover of cash in its own way, and that the feature added complexity
      for no gain they could use. Removed: the drawer, both endpoints, the client calls, `RecordCollectorRemittance`,
      `GetCollectorRemittances`, the repository and the Remittances section of the sheet. The memo line for money recorded at
      the office stays, standing on its own.
      - The `CollectorRemittances` table, its entity and its backup wiring REMAIN, written by nothing. Dropping them is the
        one destructive step in the change, and no data should be risked to delete an empty, unused table. The entity's own
        comment says so, and dropping it is a one-line migration if the office is sure.
      - A correction the office should have on record: they worried a collector could press a remittance button without cash
        arriving. In what was built a collector could not record one at all, the endpoint being Head and Administrator only
        and the handler refusing anyone holding a collector identity. The decision rests on their other reasons.

- **Monthly facilities have no statement of which month a late payment answers for.** Raised by the office 2026-08-25 and
  deliberately deferred by them. A monthly payor's earlier unpaid months can be settled, but nothing names the month a late
  payment covers the way the market sheet now names its days, so a Report of Collections line for a rental states the billed
  month of the record it was entered against and nothing more.

- **Eleven report stylesheets still carry unscoped phone media queries, which a printed page can match.** A printed page is
  measured in CSS pixels, so `@media (max-width: 900px)` matches paper as readily as a phone — proven on the month-end sheet, where
  an unscoped `768px` block printed the signatories stacked down the page and gave the sheet a phone's 14px side padding instead of
  its 12mm margin. Because those blocks sit AFTER the print block in each file, they win.
  - Fixed where documents are actually printed and were reported: `MonthEndReport`, `StallHolderList` and `ExportData` now scope
    their phone blocks to `screen`.
  - Still unscoped, and each has a print block that could be overridden the same way: `BbqReports`, `CustomReports`, `IceReports`,
    `NccReports`, `NpmReports` (four blocks), `SlhReports`, `TccReports`, `TpmReports`, `TrmReports`, `ClosedAccounts`,
    `CollectionExceptions`, `FollowUpQueue`, `PastFollowUpQueue`. The change is mechanical — add `screen and` — but each one should
    be printed once before and after, since a print layout could unknowingly be leaning on a phone rule today.

- **A market may price its areas apart — PHASE 1 SHIPPED 2026-08-23 (`88e94d92`), phases 2 to 5 open.** The office asked
  for it: Cantilan charges ₱30 across its market, but another LGU may charge ₱35 for vegetables and ₱30 for fish.
  - **Done.** `FeeRateKey.NpmDailyStallVegetable|Fish|Meat` (10, 11, 12) — ordinary rate keys, so an area's rate inherits
    an effective date, a never-retroactive change, the audit trail and the resolver's wrong-facility refusal. No
    migration (the column is already an int). `NpmDailyFee.ForStallOrNull/ForStall/ForAreaOrNull` states the whole rule
    in one place: own-area stall's own rate → the area's stated rate → the market's stated rate → nothing.
    `FacilityRateKeys.PerAreaDailyKey` / `IsPerAreaDailyKey`. Pinned by `NpmDailyFeeTests` (10 assertions) and by the
    invariant in `FeeRateSnapshotFacilityTests` that every key is either offered by its owner or withheld on purpose.
  - **Deliberately withheld:** the three keys are NOT in `FacilityRateKeys.For(NPM)`, which is the single list behind the
    rate editor's rows (`FacilityRepository:114`), its write validator (`SetFacilityRateCommandValidator:17`) and
    activation's pairing rule (`ActivateMunicipalityCommandValidator:99`). So no screen offers one and nothing can store
    one. They join that list in the same change that makes billing read them, because a rate an office could set while
    every collection still charged the market rate is worse than not offering it.
  - **Phase 2, the money work:** route every daily-fee read through `NpmDailyFee`. The sites, counted:
    `RecordDailyCollectionCommandHandler:98` · `SettleNpmDaysCommandHandler:58,86` · `SettleNpmMonthCommandHandler:78,
    104,106,125` · `NpmMonthSettlementService:138,148,221,227,229` · `BulkImportDailyHistoryCommandHandler:82,235` ·
    `BulkImportStallholdersCommandHandler:43` · `PaymentRepository.Ledger:127,131,301,305,428` ·
    `StallRepository.ClosedAccounts:54,217,221` · `StallRepository.Mobile:35` · `StallRepository.Register:70` ·
    `CollectorRepository:53` · `FacilityReportsRepository:51` · `GetSettleableNpmDaysQueryHandler:76` ·
    `GetDailyCollectionMonthQueryHandler:68` · `GetMonthEndReportQueryHandler:63` · `GetCollectionReportQueryHandler:44`
    · `GetFinancialReportQueryHandler:101` · `GetSystemSettingsQueryHandler:99` · `GetNpmRatesQueryHandler:29`. The
    pattern `stall.ResolveDailyFee(snapshot.Resolve(NpmDailyStall, day))` becomes `NpmDailyFee.ForStall(stall, snapshot,
    day)`; the several sites that resolve a bare rate for display need an area or a stall in hand first. Do it in small
    groups, with the Phase 0 Cantilan baselines green after each.
  - **Phase 2 SHIPPED 2026-08-23** in two commits: `f4cd817b` (2a, what creates a charge) and `86ff114d` (2b, what
    states one). Rerouted through `NpmDailyFee`: `RecordDailyCollection` · `SettleNpmDays` · `SettleNpmMonth` ·
    `NpmMonthSettlementService` · `BulkImportDailyHistory` · `BulkImportStallholders` (per AREA imported) ·
    `PaymentRepository.Ledger` · `StallRepository.ClosedAccounts` / `.Mobile` / `.Register` · `GetSettleableNpmDays` ·
    `GetDailyCollectionMonth` · `FacilityReportsRepository` (+ Revenue, Breakdowns, Compliance) · `CollectorRepository`
    (+ Mobile, Recognition). Three refusals became per-stall rather than per-market, and the daily-history import gate
    now asks `NpmDailyFee.AnyStated` with a per-ROW refusal naming the stall's area, so a day can never be filed at zero.
    Proven by `RecordDailyCollectionPerAreaRateTests` (billing) and `StallHoldersListPerAreaRateTests` (reporting), both
    against seeded rate rows; TEMP-DEFECTs restoring the market-only reads failed them.
  - **Phase 2c SHIPPED 2026-08-23** with phase 3 (`860f78d1`). The month-end, collection and financial reports measured
    every stall's coverage at the MARKET's rate and derived a month as thirty daily fees, ignoring a stated
    `NpmMonthlyStall` — so an office stating ₱1,000 a month while collecting ₱35 a day read ₱1,050 on its reports beside
    ₱1,000 on its roster, a disagreement that predated per-area rates. `DomainRules.DailyBilledMonthCoverage` now answers
    for all three: the stated month where there is one, else thirty installments of THIS space's fee, less one
    installment per excused day. Pinned by `DailyBilledMonthCoverageTests` and one handler assertion.
  - **Phase 3 SHIPPED 2026-08-23** (`860f78d1`). The three keys joined `FacilityRateKeys.For(NPM)`, so the rate editor
    lists them, its write path accepts them and activation's pairing rule allows them. The invariant test's
    withheld-on-purpose branch is gone.
    - **A cleared area rate (zero) reads as "not priced apart"** and the market's rate answers. The rate editor posts a
      row only when its value changes, so there is no delete: an office withdraws an area rate by clearing it. Read
      literally, zero would have made that area's stalls free and written a ₱0 collection. The MARKET's own rate keeps
      its documented meaning, where zero says the office charges nothing under that head.
  - **Phase 4 SHIPPED 2026-08-23** (eemo `b710868b`, platform `0930e38`). The onboarding form always took a rate per
    section; the console filed the FIRST as the market's rate and dropped the rest. Each priced area is now filed under
    its own key. The market's rate is still sent and answers for an area left unpriced and for a stall of the market's own
    areas carrying no rate; it is read from the first of the THREE the office priced, never from a custom row, so one
    area's figure is not handed to the rest. The "prices its areas differently" warning is gone (nothing is dropped); the
    warning that remains names the unpriced areas and the rate they will bill at. Pinned by five console specs, two API
    pairing facts and one activation-handler fact.
  - **The feature is complete for the portal and onboarding.** Remaining: **phase 5**, the mobile collector app, which
    receives a resolved rate (`MobileSlaughterCollectionDto` and the NPM daily reads) and needs the area's; and
    `GetSystemSettings` / `GetNpmRates`, which state the MARKET's rate as a settings figure - correct as far as it goes,
    but they should list the per-area rows now that an office can set them.
  - **Phase 5:** the mobile collector app, which receives a resolved rate (`MobileSlaughterCollectionDto` and the NPM
    daily reads) and needs the area's.
  - **A month, when an area is priced apart:** the existing custom-section rule is the precedent — a stall let at its own
    rate has its month as thirty of those. `NpmMonthlyStall` stays market-wide, and the office's stated monthly still
    wins where it states one. The onboarding monthly-rent field is KEPT on the office's own instruction (2026-08-23):
    removing it would silently re-price any office whose ordinance states a month directly (₱1,000 a month with a ₱35
- **Borrowed ordinance constants: the audit of 2026-08-23/24.** A sweep for `FeeRates.*` and `?? <constant>` outside the
  Domain, the seeder and the tests found the reference municipality's figures reaching other offices' screens. Fixed in
  `a293b552` and the commit after it: the mobile slaughterhouse payload (built from `FeeRates.SlhHogTotalPerHead` /
  `SlhLargeTotalPerHead` for every LGU), the mobile app's own `?? 250m` / `?? 365m`, TPM's `?? 100m` and its uncollected
  total, TRM's four `?? 30m` and its pending total, and the Collection Manager's expected total at `FeeRates.NpmDailyFee`.
  Each now reads the office's own resolved rate, and states nothing until that answer arrives.
  - **Still borrowing, and deliberately left alone — landmines, not live defects.** Five initialisers hold Cantilan's
    figure as their starting value: `TpmDtos.VendorFee` and `TrmDtos.TripFee` (both always overwritten by their overview
    handler's `with { }`), and `_npmDailyRate` / `_npmFishRate` on `CollectorRepository`, `FacilityReportsRepository` and
    `TransactionFeedRepository` (the value before `LoadNpmRatesAsync` runs). Changing them to zero is only safe once every
    public entry point on those repositories is confirmed to load rates first; doing it without that check would turn a
    borrowed figure into a silent zero, which is worse. The check is bounded: find every read of the two fields and walk
    up to its entry point.
  - **Why the mobile one survived so long:** `GetMobileSlaughterCollectionQueryHandlerTests` mocks the repository, so the
    constants never ran in a test. The rule this suggests: a payload assembled in a repository needs a repository-level
    test, not only a handler test. `MobileSlaughterCollectionRatesTests` is that test.

- **Two shell rules used to reach paper, and both are now settled globally (2026-08-23, `50677d49`).** Recorded because every
  printable page inherited them, so a future report does not have to rediscover either.
  - `body { background: var(--bg) }` (#f0f4f8) printed across the whole page whenever background graphics are on, which every
    report needs for the seal and the table headings. The office reported it as "the outer whitespace is not pure white".
    `print.css` now sets `html, body { background: #fff !important }`; it loads last, so it holds everywhere.
  - `.admin-layout { min-height: 100vh }` survived printing. On paper a viewport height IS a page height, so the layout box stayed
    a whole page tall even when the sheet inside it ended halfway down, and the leftover printed as a trailing blank sheet — the
    blank second page reported on the stallholder roster, which had never been diagnosed. The floor is lifted inside app.css's
    print block. Both are pinned by `StallHolderListPrintSheetTests`.

- **A municipality's seal is a base64 data URI, which costs a round trip on every login paint.** Because a seal can be large, it is
  deliberately kept out of persistent component state: putting one there can exceed the circuit's SignalR message limit and drop the
  connection with "connection closed with an error". The login page therefore carries the office's NAME across the prerender
  boundary but re-fetches the seal, showing a plain municipal-hall outline until it arrives. Nothing wrong is ever painted, but the
  slot visibly fills a moment later, which the office reported as flicker (2026-08-22).
  - The durable fix is to serve a seal as a URL rather than embed it: an endpoint that streams the stored bytes with a long
    cache-control, and branding returning that address. The persisted value becomes a short string, the browser caches the image
    across pages and refreshes, and the first paint carries it.
  - Not done here because it is a server change touching how branding is stored and served, and the signed-in shell — where the
    complaint actually came from — was fixed on its own (the sidebar now carries branding across the boundary like the login and
    change-password pages already did).

- **The Financial report now counts the market's utilities; Month-End and the Collection report still do not.** Asked for by the
  office 2026-08-22: electricity and water are the market's revenue, so NPM's Collected states them. Done in
  `GetFinancialReportQueryHandler` only — the row, the footer totals, the row's rate, and every bar of the trend. The other two
  reports read the same `FacilityReportsDto.TotalRevenue`, which is deliberately still stall fees alone, so for the same month the
  Financial report's NPM total will now exceed Month-End's by the utilities collected.
  - Left that way on purpose rather than changed in passing. Month-End prints a line per stall and a facility total the office ties
    together by hand; utilities are billed per stall too, but its payor lines carry stall fees only, so folding utilities into its
    total would stop that document adding up. Extending it means giving it a utilities line of its own, which is a change to a
    printed month-end document and wants the office's word first.
  - `CalculateNpmRevenueAsync` was deliberately NOT touched, which is what keeps every per-stall document consistent.
  - Weekly is excluded by design: a utility bill is billed for a month and carries no week, so a weekly report counts stall fees
    alone. Pinned by `AWeeklyReport_CountsStallFeesAlone`.
  - The utility figures are attributed by BILLING period, not by payment date, which is how `GetNpmUtilityTotalsAsync` has always
    read them. `UtilityBill` does carry `ElecPaidAt`/`WaterPaidAt`, so a cash-received view is possible later if the office wants
    the column to mean money received in the period rather than money against that period's bills.

- **Thirty eight other dialogs still close when their backdrop is clicked, and some of them hold typed work.** The rule the
  office's report established (2026-08-21, Reset Password on Staff Accounts): a dialog holding text somebody typed must not be
  dismissed by a stray click on the backdrop, because there is no warning, no undo, and nothing to recover it from. A confirm
  prompt holding no input may keep the convenience. Reset Password was fixed and pinned by
  `AccountsResetPasswordTests.TheBackdropIsInert_SoAStrayClickCannotDiscardTheTypedPasswords`; the remaining overlays were
  counted (39 `modal-overlay` elements carrying a click handler) but not swept, because the ones that hold a form and the ones
  that hold a confirmation have to be told apart by reading each, and a blanket change would make every confirm prompt harder
  to leave. The candidates worth reading first are the ones with typed input: `Collector.razor` (3), `Backups.razor` (6),
  `Accounts.razor`'s two remaining prompts, and the facility forms in `NPM`, `TPM` and `TRM`.

- **The Tabo-an weekly trend still expands one weekday across a month.** `TpmReports.razor`'s `MarketDaysOfMonth` plots each
  occurrence of `_marketDay`, and `_marketDay` is inferred from the first attendance record of the month. In a month the office
  moved its market day, that plots one weekday only: for a Friday to Thursday move starting 27 August it would draw bars for 7, 14,
  21 and 28 August, when the 28th is no longer a market day and the 27th is. It reads from recorded data, so it never relabels
  history the way the calendar did (fixed 2026-08-21, see `TpmOverviewMarketDatesTests`); it simply plots the wrong set in the one
  month a change begins. The fix is the same one the calendar took: read `TpmOverviewDto.MarketDates`, which now carries the
  month's real dates. It needs the reports page to fetch the overview, which it does not currently do, which is why this is
  recorded rather than bundled into the calendar fix.

- **Eight report stylesheets still print edge-to-edge, awaiting a go-ahead.** `Bbq`, `Custom`, `Ice`, `Ncc`, `Slh`, `Tcc`, `Tpm`,
  `Trm` each carry `.print-report-sheet { padding: 0 !important }` in their own scoped stylesheet, so their sheets print hard
  against the paper edge. `NpmReports.razor.css` had the identical line and was fixed to `12mm` on 2026-08-18; the other eight are
  the same one-line change each. The office has been told and has not yet said to proceed, which is why they are recorded rather
  than done.
  - The mechanism, since it is not obvious: `print.css` loads LAST and sets `@page { margin: 0 }` deliberately, so the browser has
    no margin box in which to print its own date, URL and page numbers. **Each sheet therefore supplies its own padding.** A
    component stylesheet saying `padding: 0` is not overriding a default — it is removing the only margin the page has.
  - And it wins even though `print.css` is later and also `!important`: scoped CSS compiles `.print-report-sheet` to
    `.print-report-sheet[b-xxxxx]`, and the attribute selector outranks the bare class on specificity.

**Out of scope — not StallTrack.** Screenshots of a "Console Ops / Deployment Control Center" page were sent during this work
and carried in the notes as outstanding UI work. It is a DIFFERENT product: its own screenshots list Spinner API, AMYL and
StockPilot alongside StallTrack as rows in a multi-project deployment dashboard. The office confirmed 2026-08-15 to stick to
StallTrack. Recorded here only so nobody picks it up again. (An earlier screenshot of a "Staff Accounts / Bookings / Pickup"
page was likewise from another project.)

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


- **NPM daily history for CUSTOM sections — now tested end to end** (2026-08-15). The path was CORRECT; what was missing was
  any test that could tell. The existing import tests mock the stall lookup with `It.IsAny<MarketSection?>()`, so whatever
  section is asked for, the mock hands back the same stall — dropping the section entirely could not fail them.
  `BulkImportDailyHistoryCustomSectionTests` uses the real repository over a real context and seeds the same space NUMBER in
  three sections (Vegetable Area, "Sari Sari", "Carinderia"), because the market numbers spaces independently per section and
  the only thing between one lessee's money and another's account is that the section is carried through and matched.
  - **A weakness found while proving the tests bite, and worth keeping in mind:** with the filter dropped, one import test
    failed and its MIRROR passed by luck. `BulkImportDailyHistoryCommandHandler` keys matched stalls into a dictionary by
    number (`stallsByNo[no] = stall`), so when two same-numbered spaces come back the second silently OVERWRITES the first, and
    which lessee is credited depends on the order rows are returned in. The section filter means it never happens today. The
    test that cannot be lucky is the repository-level one, which counts what the filter returned.
  - **RESOLVED 2026-08-16 on the office's ruling: an ambiguous row is REFUSED.** All three imports keyed matched spaces into a
    dictionary by number, so when two spaces shared one the second silently replaced the first and which lessee was credited
    depended on the order the repository returned them in. They now GROUP, and a row naming a number that more than one space
    carries is rejected with a reason that names the number and says what to do — the office is the only party that knows which
    space it meant.
    - Applied to all three, because it is one rule: the daily-history import (which day's money), the payment-history import
      (which account paid), and the stallholders import (which occupancy a lessee holds — there, guessing would have renewed,
      reopened or re-rated the wrong space).
    - Proven load-bearing by disabling the refusal: only the ambiguous-row test failed, and the unambiguous case still settles
      normally, so the refusal cannot be passing by rejecting everything.
- **Hiding or soft-deleting an OR — RETIRED 2026-08-16, there is no such thing.** The office states that once an Official
  Receipt is issued it stays part of the record; there is no withdrawal step in the actual workflow. So nothing was built, and
  the queue is already right: "Missing OR" flags only records whose OR is BLANK, which is exactly a collection taken but not yet
  receipted.
  - Verified the correction paths the office DOES use already exist, as the office pointed out: `SetStallMonthlyException` and
    `ClearStallMonthlyException` excuse or un-excuse a past billing month (₱0 owed, never counted unpaid), `RecordPayment`
    records money against a past month, and `SettleNpmDays`/`SettleNpmMonth` settle past market days. Correcting a past period
    is therefore a matter of marking it, not of unmaking a receipt.

## Rulings from the office, 2026-08-16

Seven questions that had been blocking work were answered. Recorded here with what each one settles, because the reasoning is
the part that gets lost.

1. **An issued OR is never withdrawn.** See above — retired rather than built.
2. **Forwarded headers: accept, trusted as tightly as Azure allows.** So absolute redirects stop being scheme-downgraded to
   `http`.
3. **A signed contract of zero years is INVALID.** Zero years is only legitimate for a space-only occupancy, which is why such
   a row is not treated as expired — it carries the open-ended sentinel instead. The two expiry rules must be made to agree.
4. **PayMongo: fail fast at startup** rather than three endpoints failing at request time.
5. **An import row naming a number two spaces share is REFUSED**, not placed on a best guess.
6. **The Earlier Terms occupant match reuses `PersonName`** — same name, however spelled or spaced, is the same person, which
   is the rule the office already confirmed for the slaughterhouse.
7. **Section colours reuse the three existing colours cyclically** — no new hues, and every section distinguishable whatever an
   LGU calls its own.

And for item 6: **the portal gets its own request models** rather than continuing to post command types.
- **The Backups page's duplicate headings — DONE 2026-08-16, and it turned up something worse.** Four cards, not two: a platform
  operator saw **two** headed "Recent backups" and **two** headed "Recent restores". The first pair is this LGU's own saved data
  ("the most recent 15 are kept"); the second, inside `@if (_isOperator)`, is the whole-database workflow runs. Only an operator
  sees both, which is why it survived — but an operator is exactly who must never confuse one municipality's restore with every
  municipality's. Renamed to **"Whole-database backup runs"** and **"Whole-database restore runs"**, each subtitled "every
  municipality at once, not this office alone". The office-facing pair keeps its plain wording. The old line numbers in this note
  were stale; the titles were at 162/245/386/448.
  - **The real find: the portal decided who the platform operator is BY ITSELF**, comparing the municipality claim against the
    default tenant's code written straight into the markup. `PlatformOperatorPolicy` exists specifically to stop that — its own
    summary says the rule once "lived in three" places and "they disagreed, and not harmlessly" — and the portal was a fourth.
  - It carried only the documented FALLBACK clause (default tenant + SuperAdmin) and ignored the `IsPlatformOperator` account flag
    entirely. So **a dedicated operator account — the intended mechanism, and what a fresh deployment gets — was shown none of the
    whole-database controls the API already permits it**, while any SuperAdmin of the default tenant saw them.
  - **Not a security hole:** every endpoint on `BackupController` is `[Authorize(Policy = "PlatformOperator")]`, checked before
    concluding anything. The UI flag decided only what was DISPLAYED. Verified endpoint by endpoint.
  - Now calls `PlatformOperatorPolicy.IsOperator`, exactly as the API's policy does, using `AppClaimTypes` instead of literals.
    (At the time this meant passing three facts, including the tenant code from `TenantConstants.DefaultTenantCode`; the fallback
    clause was retired on 2026-08-21 and the rule now takes only the operator flag, so both call sites pass just that.) The
    comparison mirrors the API's case sensitivity deliberately: looser would offer controls the API then refuses, stricter would
    hide ones it allows.
  - `BackupsOperatorDecisionTests` states the decision for every combination that matters, and asserts **no file in the portal
    contains the default tenant's code at all** — the multi-tenancy guard, asserted against the source because that is where the
    fault lived. Both were proven load-bearing by reintroducing the old line.
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
- **`Profile` "Earlier Terms" card — now tested** (2026-08-15). It had no test because no fixture existed to render the profile;
  there is one now (`ComponentTests/Pages/ProfileEarlierTermsTests.cs`), built against a NON-market facility deliberately, since
  the NPM profile also builds a daily heat-map and fetches resolved rates that this card never touches.
  Seven cases, and the ones that matter are about money belonging to the right lessee: a stranger's ENDED term must not offer
  "record payment on this term" (it would invite the clerk to post the present lessee's money onto the previous lessee's
  account), a LAPSED term must offer it (that term is still the one in force), and a re-let stall's two rows must each state
  their own collected and uncollected. Proven load-bearing by loosening the guard to `Uncollected > 0`: only the stranger case
  failed.
  - **Found while writing it, and NOT changed:** the card decides "same lessee as now" with
    `string.Equals(prior.Occupant?.Trim(), Stall.ActualOccupant.Trim(), OrdinalIgnoreCase)` — it does not use `PersonName`,
    so unlike the slaughterhouse it does not collapse INTERNAL whitespace. "Kim  Chui" and "Kim Chui" are the same person to one
    and different to the other. Aligning them would make this money gate offer collection in a case it currently withholds it,
    which is a behaviour change on a money path: the office should say so first. Recorded rather than decided.
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

- **Known small duplications**: mobile `_recordsCache`/`_reportCache` are never cleared on logout;
  `FacilityReportsModal.razor` still hardcodes `#4a9eff`.

  - **`SettleNpmMonthCommandHandler`'s repeated logic — resolved 2026-08-15.** What the two settle handlers actually shared was a
    16-line prologue, and the part of it that mattered was the AUTHORISATION rule: a collector may settle only where they are
    assigned. An authorisation rule kept in two copies is one that eventually gets fixed in only one of them, and this one decides
    who may record that the office received money. Both now call `Common/Authorization/NpmSettlementAccess`.
    - **Neither copy had ever been tested**: every existing test for these handlers runs as an administrator, so the collector
      branch never executed. Six cases now cover it from BOTH entry points — a rule kept in one place still needs proving at each
      door that uses it — including the other direction, so the guard cannot pass by refusing everyone. Proven load-bearing by
      dropping the assignment requirement: the two "not assigned" cases failed, one per handler.
    - One dead condition went with it: the old guard also tested `stall.Facility is null`, which cannot be true there because the
      preceding line returns unless `stall.Facility?.Code == NPM`.

  Two entries that used to sit in this list were examined on 2026-08-15 and turned out to be misdescribed. Recorded here so
  nobody "fixes" them into a defect:

  - **"the dashboard computes compliance twice" — it does not.** The two calls answer DIFFERENT questions over different spans:
    `GetFacilitySnapshotAsync` gives THIS MONTH's compliance for ONE facility (the per-facility cards), while
    `GetDelinquentStallsAsync(null, …)` gives rolling-window delinquency across ALL facilities (the overdue list), deliberately
    the same computation the Financial Reports attention list uses so the two agree. They share a helper; they are not the same
    question. Merging them to save a pass would change the figures on one of the two.
  - **"mobile caches are never cleared on logout" — true, and CONFIRMED BY THE OFFICE 2026-08-16 as by design.** Both key on
    `Session.Menu?.CollectorId`, so two collectors on one device do NOT mix. The pattern is cache-then-refresh: the cached view
    shows instantly and a background fetch replaces it, and keeping the cached view when that fetch fails is deliberate offline
    resilience for a collector in the field. Clearing on logout would trade that away. Nothing to fix.

  - **`FacilityReportsRepository.Revenue.cs`'s occupancy memo was `static`; now an instance field** (2026-08-15). The comment
    claimed "cached for the life of this request", which a process-wide table does not deliver — it only behaved that way
    because EF hands each request its own entity graph. The memo also has no as-of date in its key while `Stall.Occupancies`
    takes one, so a caller asking as of a different date would silently get the first answer. Harmless today (the as-of date
    affects only `IsCurrent`, which neither predicate there reads) and now limited to one request and one clock, which is what
    the comment always said.
- **Absolute redirects are scheme-downgraded to `http`.** Found 2026-08-14 while verifying the root route: the live
  `Location` header is `http://console.stalltrack.site/login`, not `https://`. TLS terminates at Azure's front end, so the app
  sees `http` and builds absolute redirect URLs with that scheme; there is no `UseForwardedHeaders` in the Client pipeline.
  HSTS is set (`max-age=31536000`), so a returning browser upgrades internally, and the `http` URL 301s to `https` anyway —
  but a first-time visitor makes one plaintext request for the URL.
  - NOT fixed on purpose. The remedy is `UseForwardedHeaders` as the FIRST middleware, and on App Service the usual
    configuration clears `KnownProxies`/`KnownNetworks` because the proxy address is not fixed — which means trusting
    `X-Forwarded-*` from any caller. That is spoofable, and it also changes the client IP that rate limiting and the firewall
    rules see. A trade-off for the office to choose, not one to make quietly.

- **The router's `NotFound` — DONE 2026-08-16, on the third mechanism.** Any mistyped URL rendered a blank page. Confirmed live
  before the fix: 404 with a body of ZERO bytes, so the office saw white space with no statement and no way back; the status code
  was already correct. Now answered by a **terminal fallback endpoint** in `Client/Program.cs`, rendering
  `Components/Pages/NotFoundDocument.razor` with a 404. Verified live: 726 bytes, styled, no app shell, no tenant named.
  - **Three mechanisms were tried and two shipped uselessly. Do not try them again.**
    1. The router's `<NotFound>` markup. The framework documents it as **ineffective in a Blazor Web App** — and it is. Worse,
       **bUnit honours it**, so the test asserting it PASSED while production stayed blank. A test that lied.
    2. `Router.NotFoundPage`, the .NET 10 replacement. Correct for navigation inside a running circuit; never consulted for a
       typed address. It is still wired up, for that case.
    3. `UseStatusCodePagesWithReExecute`. Ruled out **by evidence**: an unmatched undotted path returns an explicit
       `Content-Length: 0`, and `StatusCodePagesMiddleware` does nothing when a length is already set. Comparing a dotted path
       (no Content-Length) with an undotted one (Content-Length: 0) is what revealed it.
  - The fallback's shape carries three decisions: the `:nonfile` constraint so a missing asset still fails as a missing file;
    `/api` excluded because those callers are code, not people (the sign-in form and token refresh read those responses, and
    handing them HTML to parse is how "wrong password" becomes a parse error); and it renders a whole DOCUMENT, because a fallback
    renders a component with nothing to supply the page around it — the first working version answered with 219 bytes of bare
    unstyled markup.
  - `NotFoundDocument` is deliberately lighter than `App.razor`, not a copy: no Blazor script, no circuit, no splash, no SignalR.
    The page shows nothing that changes and its only control is a link. `app.css` alone carries `.empty-state`, `.btn-primary` and
    the colour variables.
  - The way back is an `<a href>`, not a button — load-bearing, because this render is STATIC, so an `@onclick` would be wired to
    nothing and the only way off the page would silently do nothing. `AccessDenied` can afford a button; it only renders inside a
    live circuit.
  - `/not-found` uses `MinimalLayout` (the page declares it, as `PayorLayout` pages do). On a not-found render the path IS the
    mistyped address, so `MainLayout`'s path-based sidebar rule would have drawn the full app shell around it — a menu offered to
    a visitor who may not be signed in. Listing `/not-found` in `AppShell` was tried and reverted: it implies the path governs,
    when the page does.
  - **The lesson worth keeping: run the built portal locally before deploying.** Three deploys were spent verifying in production
    because it was treated as the only way to see the truth. Starting the Release DLL on a spare port takes seconds, and it is what
    caught the missing document — as well as confirming a missing `.css`, an unknown `/api` path, sign-in, and the SignalR
    handshake were all still correct.

- **Mojibake in five stylesheet comment blocks — REPAIRED 2026-08-17.** `Accounts`, `Collector`, `FacilityConfiguration`, `Menu`
  and `Settings`. Comments only, and that was PROVEN rather than assumed: with every `/* … */` block stripped, the remaining CSS in
  all five files is byte-identical before and after. Line endings were preserved per file (they differ — `Accounts` is all CRLF,
  `FacilityConfiguration` is almost all LF), so the diff is 39 lines and no more.
  - Three distinct manglings, each restored to what it was meant to be: `â€"` → `—` (em dash), `â"€` → `─`, and a six-character
    run → `═`. The last had been through CP437 rather than CP1252, which is why it looked nothing like the others.
  - **The sweep I had been using was BLIND to two of the three.** `Select-String -Pattern "Ã|â€"` matches the em-dash form but
    NOT the mangled `─` or `═`, because neither contains the byte pair it looks for. It reported these files as clean while they
    held 238 corrupt sequences — the check said what I wanted to hear. Use this instead, which looks for all three:

    ```powershell
    $bad = @([string]([char]226)+[char]8364+[char]8221,   # em dash, via CP1252
             [string]([char]226)+[char]8221+[char]8364,   # box light horizontal
             [string]([char]206)+[char]8220+[char]195+[char]178+[char]195+[char]8240)  # box double, via CP437
    # plus the generic markers: "Ã" and "â€"
    ```
  - Cause worth knowing, because it recurs: `powershell -File` on 5.1 reads a BOM-less script as ANSI, so em dashes inside a
    script's own string literals reach the files it writes already corrupted. The same thing damaged three comment lines during
    the `CollectorRepository` split and was repaired in the commit after it. Sweep BEFORE committing, and STOP when it reports.
  - A further trap found while repairing these: PowerShell **silently drops console output** for lines containing these bytes, so
    a scan can appear to find nothing when it found plenty. Print line NUMBERS and an ASCII-folded rendering, never the raw line.

- **Orphaned CSS in `FollowUpQueue.razor.css` — REMOVED 2026-08-17.** 184 lines of dead rules: `.fq-intro*`, `.fq-scope*` and the
  whole `.fq-pay-*` monthly-payment-modal block. Confirmed dead by scanning the ENTIRE Client: those tokens appear nowhere except
  that stylesheet. The diff is `0 insertions, 184 deletions` — a pure removal.
  - **Seven classes looked orphaned and were NOT.** `.fq-pri-critical/high/normal/review` and `.fq-sec-critical/high/review` never
    appear literally in the markup because they are composed at runtime — `fq-pri-@PriClass(it.Priority)` and
    `fq-sec-@grp.Section.Tone`. A name-by-name search called all 25 candidates unused; deleting the seven would have silently
    stripped the priority and severity colour-coding off a screen the office uses to chase money. **Search by PREFIX, and read the
    markup, before deleting a class.**
  - The "mixed line endings" hazard recorded here was a working-copy illusion: `.gitattributes` sets `* text=auto`, so the file is
    stored with LF whatever the checkout looks like, and the diff stayed clean.
