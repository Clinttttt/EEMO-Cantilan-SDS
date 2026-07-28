# Project Structure

## Solution

Clean Architecture, dependencies pointing inward: **Presentation → Application → Domain ← Infrastructure**.

```
EEMOCantilanSDS/
├── EEMOCantilanSDS.Domain/          entities, enums, constants, Result<T>, PhilippineTime (no dependencies)
├── EEMOCantilanSDS.Application/     CQRS handlers, DTOs, validators, interfaces
├── EEMOCantilanSDS.Infrastructure/  EF Core, repositories, security, caching, tenancy, fees
├── EEMOCantilanSDS.HttpClients/     typed API clients shared by the portal and the collector app
├── EEMOCantilanSDS.Api/             controllers, middleware, hubs, auth
├── EEMOCantilanSDS.Client/          Blazor Server portal + payor portal
├── EEMOCantilanSDS.Mobile/          .NET MAUI collector app (Android)
├── EEMOCantilanSDS.Mobile.Core/     platform-agnostic mobile services and models
├── EEMOCantilanSDS.Testing/         xUnit unit + integration tests (EEMOCantilanSDS.UnitTest.csproj)
└── EEMOCantilanSDS.ComponentTests/  bUnit render tests
```

Repository root also holds: `.github/workflows/` (CI, deploy, APK publish, backup, restore),
`.amazonq/context/knowledge/` (the four rule files), `.kiro/` (steering + skills),
`mobile-app-site/` (the static site behind the APK download and bind links — **written to by
`publish-apk.yml`, do not delete**), `scripts/`, `tools/postgres-dev-mcp/` (local dev MCP server),
`docker-compose.yml`, `.env.example`.

## Domain

```
Domain/
├── Common/            BaseEntity, AuditableEntity, Result<T>, PhilippineTime, CursorPagedResult
├── Constants/         FeeRates (ordinance fallbacks) + DomainRules (thresholds, DailyBilledMonthDays)
├── Enums/             FacilityCode (NPM..TPM + Custom1..5), MarketSection, StallStatus, PaymentStatus,
│                      AnimalType, ApplicableFees, ReportPeriod, FeeRateKey, InactiveAccountState
└── Entities/
    ├── Users/         BaseUser (TPH) → AdminUser, CollectorUser, PayorUser; CollectorFacilityAssignment
    ├── Facilities/    Facility, Stall, Contract, FacilityRate
    ├── Payments/      PaymentRecord, DailyCollection, UtilityBill, OnlinePaymentTransaction
    ├── Slaughterhouse/ SlaughterTransaction
    ├── TaboanMarket/  TpmVendor, TpmAttendance
    ├── TransportTerminal/ TrmTransporter, TrmTrip
    ├── Tenancy/       Municipality, AssessmentRequest, OnboardingDraft
    └── Audit/         AuditLog
```

## Application — one folder per use case, three files each

```
Application/
├── Command/           Auth (AdminAuth, CollectorAuth, Mfa, PasswordReset, EmailVerification),
│                      Admins, Collectors, Stalls, Payments, DailyCollections, Slaughterhouse,
│                      Trm, Tpm, Utilities, OnlinePayments, Municipalities, Onboarding, Backups
├── Queries/           Auth, Dashboard, Payments, Stalls, Reports, Mobile, Municipalities, Rates, Audit
├── Common/
│   ├── Interface/     ApiClients/ (I{Feature}ApiClient), Persistence/ (I{Entity}Repository, IUnitOfWork),
│   │                  Services/ (ITotpService, IQrCodeGenerator, ICurrentUserService, IFeeRateResolver …)
│   ├── Authorization/ AdminManagementGuard, PlatformOperatorGuard
│   ├── Caching/       IEemoAppCache, EemoCacheKeys, EemoCacheRegions, IEemoCacheInvalidator
│   ├── Security/      ICredentialProtector, RecoveryCodes
│   └── Tenancy/       ITenantContext
├── Dtos/              per feature area
├── Requests/          controller request models
└── Behaviors/         validation pipeline
```

## Infrastructure

```
Infrastructure/
├── Persistence/       AppDbContext, UnitOfWork, Interceptors/ (audit), Seeders/
├── Configuration/     one {Entity}Configuration per entity
├── Repositories/      Facilities/, Payments/, Reports/, Auth/, Operations/, SystemHealth/
├── Security/          TotpService, QrCodeGenerator, CredentialProtector
├── Fees/              FeeRateResolver + snapshot
├── Tenancy/           tenant resolution and the global query filter
├── Caching/           IEemoAppCache implementation
├── Services/          email, notifications, backups
└── Migrations/        additive only (62 and counting)
```

## Api

Thin controllers over MediatR (`ApiBaseController` → `Sender.Send` → `HandleResponse`), grouped by area:
auth (admin, collector, payor), facilities and stalls, payments and daily collections, slaughter, TRM, TPM,
utilities, reports, dashboard, transactions, audit, municipalities and profile, rates, OR series, onboarding
and assessment, activation, platform setup, mobile (bind, version, menu), notifications, online payments,
backups, database health, settings, tenant usage.

## Client (Blazor Server)

```
Client/
├── Components/
│   ├── Layout/        MainLayout (chrome-less route list lives here)
│   ├── Pages/         routable pages — Menus/ (dashboard, collectors, vendors, transactions, settings,
│   │                  accounts, online payments, facilities/), Reports/, auth + payor pages,
│   │                  Pages/Shared/ (feature components: Sidebar, TwoFactorPanel, AuthBrandPanel, SH/)
│   ├── Modals/        payment, vendor, facility-report modals
│   └── Shared/        generic components (FacilityHero, FacilityPage, FacilityMark, Skeleton)
├── Securities/        AuthStateProvider, TokenService, authorization + refresh handlers, circuit handler
├── Services/          BrandingState, FacilityState
├── Utilities/         JwtParser, FacilityMarkArt (shared with the collector app via a linked Compile item)
├── Extensions/        AddApiHttpClient
├── Controllers/       AuthProxyController (cookie-bearing relay to the API)
└── wwwroot/           app.css design tokens, css/, images/, js/
```

## Conventions

- Commands/queries: `{Action}{Entity}Command`, `Get{Entity}By{Filter}Query`, plus `{Name}Handler` and
  `{Name}Validator` — three separate files, one use case per folder.
- One EF configuration per entity. DTOs are `{Entity}Dto`. API clients are `I{Feature}ApiClient`.
- Component CSS in `.razor.css` (scoped, must stay brace-balanced); design tokens in `app.css`.
- DI entry points: `AddApplicationService()`, `AddInfrastructureService()`, `AddApi()`, and the Client's
  `DependencyInjection.cs` (note: `AddApiHttpClient` for authenticated clients, plain `AddHttpClient` for the
  anonymous `IAuthApiClient`).
- Migrations run from the solution root against the Infrastructure project.
- Rules: `.amazonq/context/knowledge/`; steering: `.kiro/steering/`.
