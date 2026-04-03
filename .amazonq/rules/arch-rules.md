# EEMO Cantilan — Architecture Rules
# Agent: read this file fully before generating any code.

---

## Project Identity

- System: EEMO Revenue Collection System
- Client: Municipality of Cantilan, Surigao del Sur
- Purpose: Digitize stall rental collection across 6 government-managed facilities
- Solution name: `EEMOCantilanSDS`

---

## Solution Layout

```
EEMOCantilanSDS.sln
├── EEMOCantilanSDS.Domain          → Entities, Enums, Constants, Domain Interfaces
├── EEMOCantilanSDS.Application     → CQRS, MediatR, FluentValidation, DTOs, App Interfaces
├── EEMOCantilanSDS.Infrastructure  → EF Core, Repositories, UnitOfWork, Migrations
├── EEMOCantilanSDS.Api             → ASP.NET Core Controllers, Middleware, DI
├── EEMOCantilanSDS.Client          → Blazor Server (UI only, no business logic)
├── EEMOCantilanSDS.Mobile          → .NET MAUI Hybrid (later phase)
└── EEMOCantilanSDS.UnitTest        → xUnit
```

---

## Tech Stack

- Frontend: Blazor Server (.NET 10)
- Backend: ASP.NET Core Web API
- ORM: Entity Framework Core 9 + Npgsql
- Database: PostgreSQL
- CQRS: MediatR
- Validation: FluentValidation
- Auth: Custom JWT — NO ASP.NET Identity
- Password: `PasswordHasher<T>` from `Microsoft.AspNetCore.Identity` only
- Patterns: Clean Architecture · DDD · CQRS · Result Pattern · UnitOfWork · Global Exception Handling

---

## Layer Responsibilities (STRICT — never cross these)

### Domain
- Entities with private setters and static `Create()` factories
- Enums, `FeeRates`, `DomainRules` constants
- Business rules live HERE — never in handlers
- Zero external NuGet dependencies

### Application
- CQRS: Commands + Queries defined as records
- Handlers: `IRequestHandler<TRequest, TResponse>`
- Validators: `AbstractValidator<T>` — one per command/query, no exceptions
- DTOs: never expose domain entities to callers
- Interfaces: `IUnitOfWork`, `IStallRepository`, `IPaymentRepository`, etc. defined here
- Pipeline behaviors: `ValidationBehavior`, `LoggingBehavior`
- No EF Core `DbContext` reference here — only interfaces

### Infrastructure
- `AppDbContext : DbContext`
- One `IEntityTypeConfiguration<T>` per entity
- Repository implementations of Application interfaces
- `UnitOfWork : IUnitOfWork` wraps DbContext + all repo instances
- Migrations (auto-generated — never manually edited)
- `DependencyInjection.cs` — registers DbContext, UnitOfWork

### API
- Controllers are thin — only `IMediator.Send()`, nothing else
- Middleware: `ExceptionHandlingMiddleware`, `JwtMiddleware`
- Zero business logic

### Client (Blazor Server)
- Razor components only — zero business logic, zero direct DbContext
- Calls API via `HttpClient` services
- CSS: `app.css` globals + `{Component}.razor.css` isolation per component
- No Tailwind — custom CSS only, use CSS variables from design tokens

---

## DI Registration Rules

- Infrastructure method: `AddInfrastructureService` (not `AddInfrastructure`)
- Application method: `AddApplicationService` (not `AddApplication`)
- Always use `typeof(ApplicationAssemblyMarker).Assembly` — never `Assembly.GetExecutingAssembly()`
- Behaviors registered via `AddOpenBehavior` inside `AddMediatR` — never via `AddTransient`
- Repositories are wired inside `UnitOfWork` — do NOT register them individually in DI
- AutoMapper: optional — only uncomment when a profile actually exists

---

## Naming Conventions

| Thing | Pattern | Example |
|---|---|---|
| Command | `{Action}{Entity}Command` | `RecordPaymentCommand` |
| Query | `Get{Entity}By{Filter}Query` | `GetStallsByFacilityQuery` |
| Handler | `{Command/Query}Handler` | `RecordPaymentCommandHandler` |
| Validator | `{Command/Query}Validator` | `RecordPaymentCommandValidator` |
| DTO | `{Entity}Dto` or `{Entity}Response` | `PaymentRecordDto` |
| EF Config | `{Entity}Configuration` | `StallConfiguration` |
| Repo interface | `I{Entity}Repository` | `IStallRepository` |
| Repo impl | `{Entity}Repository` | `StallRepository` |
| Service | `{Name}Service` | `JwtService`, `AuditService` |
| Controller | `{Entity}Controller` | `PaymentsController` |
| Blazor page | `{PageName}.razor` | `Vendors.razor` |
| Component CSS | `{Component}.razor.css` | `PaymentHistoryModal.razor.css` |

---

## PostgreSQL Types — always use these, never SQL Server equivalents

| Use | Never |
|---|---|
| `text` | `nvarchar(max)` |
| `character varying(n)` | `nvarchar(n)` |
| `boolean` | `bit` |
| `uuid` | `uniqueidentifier` |
| `timestamp with time zone` | `datetime` / `datetime2` |
| `numeric(18,2)` | `decimal(18,2)` |
| `integer` | `int` |
| `jsonb` | `nvarchar` for JSON |

---

## Auth Rules

- No ASP.NET Identity — custom JWT only
- `PasswordHasher<BaseUser>` used in `AdminUser.Create()` and `CollectorUser.Create()`
- Access token: 15 minutes. Refresh token: 7 days, stored on `BaseUser`
- `BaseUser` methods: `SetRefreshToken()`, `IsRefreshTokenValid()`, `ClearRefreshToken()`
- Admin registration is closed — SuperAdmin only creates new accounts
- 5 failed logins → account locked for 15 minutes (`DomainRules.MaxFailedLoginAttempts`, `DomainRules.LockoutMinutes`)
- `IsLockedOut` is computed — must be ignored in EF config
- `MustChangePassword = true` on first admin creation

---

## OR Number Rules

- Entered manually by admin — never auto-generated by the system
- Globally unique across both `PaymentRecord.ORNumber` and `DailyCollection.ORNumber`
- Uniqueness validated in FluentValidation via `IUnitOfWork` — never in the handler
- Only shown in UI when status is `Paid` or `Partial`
- Format is free-form (e.g. `OR-2026-0301`)

---

## Blazor Design Tokens

```
--navy: #0d2137        --gold: #c8a84b        --green: #2d7a5f
--navy-2: #112d47      --gold-light: #e8cc76  --green-bg: #e6f4ef
--navy-3: #1e3a5f      --bg: #f0f4f8          --red: #8b3a3a
--bg-card: #ffffff     --bg-icon: #eef2f6     --red-bg: #fdf0f0
--border: #dde4ea      --text-muted: #8faabf  --text-subtle: #6a8aa0
```

---

## Reusable Blazor Components (already built — do not recreate)

- `Sidebar.razor` — collapsible nav sidebar
- `Toolbar.razor` — search + filters + action buttons
- `ActionBar.razor` — facility-specific quick actions
- `FacilityStallsTable.razor` — generic stall table (`@typeparam TStall`)
- `FacilityPaymentModal.razor` — record payment modal
- `PaymentHistoryModal.razor` — 12-month payment ledger
- `AddVendorModal.razor` — add/edit vendor

---

## What NOT to Do (hard rules — never violate)

- Never inject `DbContext` directly in handlers — use repos + `IUnitOfWork`
- Never call `context.SaveChangesAsync` directly in handlers — always via `uow.SaveChangesAsync()`
- Never call `uow.CommitAsync()` — the method is `SaveChangesAsync()`
- Never use `IDisposable` on `IUnitOfWork` or `UnitOfWork`
- Never use private readonly field injection in handlers — always primary constructor
- Never use `nvarchar`, `datetime`, `uniqueidentifier` — PostgreSQL only
- Never call `HasKey()` on `AdminUser` or `CollectorUser` — TPH shares base table key
- Never put business rules in MediatR handlers — Domain entities only
- Never return Domain entities from handlers — always DTOs
- Never auto-generate OR Numbers — always manual admin input
- Never use `AssignedArea` string on `CollectorUser` — use `CollectorFacilityAssignment`
- Never use `HasDefaultValue(1)` on enum columns
- Never skip FluentValidation — every command/query must have a validator
- Never put validation logic in handlers — use `ValidationBehavior` pipeline
- Never hardcode fee values — always use `FeeRates` constants
- Never use `FeeRates` Min/Max range constants for billing — use `Stall.MonthlyRate`
- Never put `PayorId` on `Stall` — use `Contract` for tenant tracking
- Never put business logic in Blazor components — API calls only
- Never use `<form>` tags in Blazor — use `@onclick` / `@onchange`
- Never map computed properties in EF — always `builder.Ignore()`
- Never create public setters on entity properties — always `private set`