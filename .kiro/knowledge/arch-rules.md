# Architecture Rules

Repository-wide rules. **This file wins** when it disagrees with the other knowledge files.

---

## 1. Solution layout

Clean Architecture, dependencies pointing inward.

```
EEMOCantilanSDS.Domain          entities, enums, constants, Result<T>, PhilippineTime   (no dependencies)
EEMOCantilanSDS.Application     CQRS handlers, DTOs, validators, interfaces            (→ Domain)
EEMOCantilanSDS.Infrastructure  EF Core, repositories, security, caching, tenancy      (→ Application, Domain)
EEMOCantilanSDS.HttpClients     typed API clients used by the presentation apps        (→ Application)
EEMOCantilanSDS.Api             controllers, middleware, hubs, auth                    (→ Application, Infrastructure)
EEMOCantilanSDS.Client          Blazor Server portal + payor portal                    (→ Application, HttpClients)
EEMOCantilanSDS.Mobile          .NET MAUI collector app                                (→ Application, HttpClients, Mobile.Core)
EEMOCantilanSDS.Mobile.Core     platform-agnostic mobile services and models
EEMOCantilanSDS.Testing         xUnit unit/integration tests (EEMOCantilanSDS.UnitTest.csproj)
EEMOCantilanSDS.ComponentTests  bUnit render tests for Blazor components
```

**Never:** Domain referencing anything; Application referencing Infrastructure; a presentation project
referencing another presentation project; a repository or handler reaching into a UI concern.

The Client and the Mobile app do **not** reference each other. Where they must share a table of values, share
the FILE via a linked `Compile` item (as `FacilityMarkArt.cs` is), not a copy.

---

## 2. Layer responsibilities

**Domain** — business state and invariants. Entities have private setters and expose intention-named methods
(`ConfirmMfaEnrollment`, `RecordFailedLogin`, `Close`, `UpdateRates`). No EF attributes, no DTOs, no services.

**Application** — one folder per use case, three files: the command/query record, its handler, its validator.
Handlers depend on interfaces only (`IStallRepository`, `IFeeRateResolver`, `ICurrentUserService`,
`IUnitOfWork`, `IEemoCacheInvalidator`). Handlers return `Result<T>` and never throw for expected failures.

**Infrastructure** — EF Core configurations, repositories, the tenancy filter, caching, security
(`TotpService`, `QrCodeGenerator`, `CredentialProtector`), fee-rate resolution. Repositories project to DTOs
for reads; they do not decide policy.

**Api** — thin controllers: authorise, send the request, `HandleResponse(result)`. No business logic. Composing
a URL from configuration or attaching a generated QR is acceptable; deciding money is not.

**Client / Mobile** — presentation only. All data through typed API clients. No DbContext, ever.

---

## 3. CQRS conventions

- `{Action}{Entity}Command` / `Get{Entity}By{Filter}Query`, with `{Name}Handler` and `{Name}Validator`.
- One use case per folder. Do not co-locate unrelated handlers.
- A query never mutates. A command returns the minimum the caller needs.
- Validation lives in FluentValidation validators, executed by a pipeline behaviour — not inside handlers.
- `Result<T>`: `Success`, `Failure(message, statusCode)`, `NotFound()`, `Forbidden()`, `Unauthorized()`.
  `Failure` takes an `int`; read it back as `result.StatusCode ?? 400`.

---

## 4. Multi-tenancy — the rule that breaks the most things

Every tenant-owned entity carries `MunicipalityId`, and a global query filter scopes reads to the current
tenant. Consequences you must respect:

- **Uniqueness is per tenant.** A username, email, stall number or OR number is unique within a municipality,
  not globally. Anything that resolves a user by email across tenants must handle MULTIPLE matches.
- `IgnoreQueryFilters()` is a deliberate act, allowed only where no tenant context can exist yet
  (sign-in, refresh, activation, platform-operator work) or where the platform operator is legitimately
  cross-tenant. Every use needs a comment saying why.
- Anything the user can see must be tenant-resolved: office name and acronym, seal, facility names, section
  labels, fee rates, OR series, market day. **No hardcoded "Cantilan", "EEMO", ₱30 or ₱900 in the UI.**
- Cantilan is the accuracy baseline. A change for another LGU that moves a Cantilan figure is a bug.

---

## 5. Money rules

- Resolve rates through `IFeeRateResolver` **as of a date**; `FeeRates` constants are the fallback only.
- A stall's daily fee comes from `Stall.ResolveDailyFee(resolvedOrdinanceRate)` — nowhere else.
- A daily-billed facility's monthly obligation is the rent the space is let for —
  `Stall.ResolveMonthlyRent(dailyRate, FeeRateKey.NpmMonthlyStall)`: the LGU's own stated month, or thirty
  installments when it states none. The daily fee is the **installment**, not the measure. Never the stored
  `Stall.MonthlyRate`.
- **Read every daily-billed figure from the monthly obligation ledger** (`DomainRules.DailyBilledMonthObligation`,
  `…MonthCredit`, `…MonthOutstanding`), per calendar month: Expected − Collected − Credits = Outstanding, floored at
  nil. Twelve complete months are exactly 12 × the rent; February owes the same as August. A month whose
  installments cannot reach the rent carries a month-end adjustment on its last installment
  (`DailyCollection.AddMonthEndAdjustment`), collectible only once the month has closed — nothing may be read as
  arrears before its due date. Collecting beyond the obligation is revenue, never a negative balance.
- **A stall outlives its lessees.** Attribute money to the occupancy that answers for the period it was raised
  FOR (`Stall.Occupancies`), never to the stall's current contract. A month is answered for by exactly one
  occupancy — `StallOccupancy.AnsweringForMonth` is the rule — so nothing may charge or credit a handover
  month twice. A past occupancy's rent is its own `Contract.MonthlyRentalRate`, not the stall's current rate.
- **A period-scoped view states that period's figures**; lifetime totals belong to the cumulative ("Whole
  time") view, and a span shown beside an amount is scoped the same way as the amount.
- Writes that a field device may retry carry a **client operation id** so a duplicate is discarded.
- Financial mutations must pass through the audit interceptor. Do not bypass `SaveChangesAsync`.
- `decimal` for money, `numeric(18,2)` in Postgres. Never `double`.

---

## 6. EF Core and Postgres

- Postgres-native types only: `text`, `character varying(n)`, `boolean`, `uuid`,
  `timestamp with time zone`, `numeric(18,2)`, `integer`, `jsonb`.
- One `{Entity}Configuration` per entity. Migrations are **additive**: new nullable columns or new tables. No
  destructive DDL — production applies migrations at startup (`Database__ApplyMigrationsAtStartup=true`).
- Reads: `AsNoTracking()`, project to DTOs, batch to avoid N+1.
- InMemory tests do not run migrations and will not catch Npgsql-only failures. Inspect
  `dotnet ef migrations script` output for anything schema-shaped.
- Note: `AddScoped<IAppDbContext, AppDbContext>()` currently creates a SECOND context instance per scope
  alongside `AddDbContext<AppDbContext>`. Known, deliberately unchanged; do not "fix" it casually — roughly
  twenty handlers depend on the current behaviour.

---

## 7. Authentication and authorisation

- JWT access token 15 min, refresh token 7 days, hashed at rest, single-source, revoked on logout.
- Lockout after 5 failed attempts for 15 minutes. A wrong second factor counts as a failed attempt.
- Guards, not ad-hoc checks: `AdminManagementGuard` (no Head may act on a peer Head),
  `PlatformOperatorGuard.IsCurrentAsync` (operator flag OR default-municipality Head fallback) and
  `PlatformOperatorGuard.IsDedicatedOperatorAsync` (flag only — required for cross-tenant reach).
- Guards fail **closed**.
- Every authenticated API client must be registered with the authorization and refresh handlers
  (`AddApiHttpClient`). `IAuthApiClient` is registered WITHOUT them and may host anonymous endpoints only —
  putting an `[Authorize]` endpoint there silently produces 401s.
- Error responses: the API may return `error` (string) or `errors` (field-keyed). The client parser handles
  both, case-insensitively.

---

## 8. Blazor rules (portal and collector app)

- `@rendermode InteractiveServer` for interactive pages; typed API clients for all data.
- `OnInitializedAsync` runs **twice** under prerendering. Never consume a one-time token there — use
  `OnAfterRenderAsync(firstRender)`, and prefer making the operation idempotent.
- Card, form and panel classes are **component-scoped**. A new routable page or component needs its own
  `.razor.css`; it cannot borrow another page's classes.
- A chrome-less route must be listed in `MainLayout.UpdateSidebarVisibility()`.
- Every `.razor.css` edit must be brace-balanced. One unbalanced brace corrupts the entire scoped bundle and
  breaks every page, and neither `dotnet build` nor `/health` will catch it. Prefer appending a new block at
  the end of the file over surgical edits, and count braces after editing.
- Text inputs stay **uncontrolled** (no `value=` with `@oninput`). A controlled input round-trips every
  keystroke and characters visibly revert on a slow connection; clear programmatically by bumping a `@key`.
- Razor gotchas: a loop variable named `code` collides with the `@code` directive; Razor comments
  (`@* *@`) are not valid in Angular or HTML files.
- No inline styles, no Tailwind in components, no CSS-in-JS. Design tokens live in `app.css`.

---

## 9. Testing

- Unit and integration tests in `EEMOCantilanSDS.Testing`; bUnit render tests in
  `EEMOCantilanSDS.ComponentTests`.
- **Run the two suites in separate commands.** Running them together causes a bUnit timing flake.
- bUnit's default `WaitForAssertion` timeout is 1 second and pages render after an async load — pass an
  explicit generous timeout.
- Money, reports, delinquency and tenancy changes need a test that FAILS before the fix. Reintroduce the
  defect once to prove the test catches it.
- A behaviour that only one municipality exercises still needs a Cantilan-unchanged test beside it.

---

## 10. Working in this repository

- Tooling: dedicated file editors only. Scripted in-place edits (PowerShell string replacement) have twice
  corrupted source files — stripped a UTF-8 BOM, mangled `₱`/`—`/`…`, and produced invalid YAML. Do not use
  them on tracked files.
- Stage files by explicit path and check `git diff --cached --name-only` before committing. Never stage
  `.gitignore`, `.env`, keystores, database dumps, APKs or `artifacts/`.
- `master` is production: a push deploys (~10–13 minutes). Always verify afterwards — image tag equals HEAD,
  API `/health` 200, portal `/login` 200, and the scoped CSS bundle brace-balanced.
- Mobile changes need a RELEASE APK rebuild before collectors see them.
- Documentation-only paths (`.kiro/**`, `README.md`, `AGENTS.md`) are excluded from the deploy trigger.
