# Project Structure

## Solution Organization

Clean Architecture with strict layer separation and dependency flow inward:

```
EEMOCantilanSDS/
├── EEMOCantilanSDS.Domain/          → Core business logic (no dependencies)
├── EEMOCantilanSDS.Application/     → Use cases, CQRS, validation
├── EEMOCantilanSDS.Infrastructure/  → Data access, EF Core, repositories
├── EEMOCantilanSDS.Api/             → HTTP endpoints, middleware
├── EEMOCantilanSDS.Client/          → Blazor Server UI
├── EEMOCantilanSDS.Mobile/          → .NET MAUI (future)
└── EEMOCantilanSDS.Testing/         → xUnit tests
```

**Dependency Rule:** Presentation → Application → Domain ← Infrastructure

## Domain Layer (Zero Dependencies)

```
EEMOCantilanSDS.Domain/
├── Common/
│   ├── BaseEntity.cs              → Base with Id: Guid
│   ├── AuditableEntity.cs         → Adds audit fields + soft delete
│   ├── Result.cs                  → Result<T> pattern for error handling
│   └── CursorPagedResult.cs       → Pagination model
├── Constants/
│   ├── FeeRates.cs                → All fee constants (NPM, TCC, NCC, etc.)
│   └── DomainRules.cs             → Business rule constants
├── Entities/
│   ├── Users/
│   │   ├── BaseUser.cs            → Abstract base (TPH)
│   │   ├── AdminUser.cs           → Inherits BaseUser
│   │   ├── CollectorUser.cs       → Inherits BaseUser
│   │   └── CollectorFacilityAssignment.cs
│   ├── Facilities/
│   │   ├── Facility.cs
│   │   ├── Stall.cs
│   │   └── Contract.cs
│   ├── Payments/
│   │   ├── PaymentRecord.cs
│   │   └── DailyCollection.cs
│   ├── Slaughterhouse/
│   │   └── SlaughterTransaction.cs
│   └── Audit/
│       └── AuditLog.cs
└── Enums/
    ├── FacilityCode.cs            → NPM=1, TCC=2, NCC=3, BBQ=4, ICE=5, SLH=6
    ├── StallStatus.cs
    ├── PaymentStatus.cs
    ├── AdminRole.cs
    └── AnimalType.cs
```

## Application Layer (Feature Folders)

Organized by feature, not by type. Each command/query has 3 separate files:

```
EEMOCantilanSDS.Application/
├── Command/
│   ├── Auth/
│   │   ├── AdminAuth/
│   │   │   ├── Login/
│   │   │   │   ├── LoginCommand.cs
│   │   │   │   ├── LoginCommandHandler.cs
│   │   │   │   └── LoginCommandValidator.cs
│   │   │   └── CreateFirstAdmin/
│   │   │       ├── CreateFirstAdminCommand.cs
│   │   │       ├── CreateFirstAdminCommandHandler.cs
│   │   │       └── CreateFirstAdminCommandValidator.cs
│   │   └── RefreshToken/
│   ├── Collectors/
│   │   └── CreateCollector/
│   ├── Payments/
│   │   ├── RecordPayment/
│   │   └── UpdatePaymentStatus/
│   ├── Stalls/
│   │   ├── CreateStall/
│   │   ├── ToggleStallStatus/
│   │   └── SoftDeleteStall/
│   └── Slaughterhouse/
│       └── RecordSlaughter/
├── Queries/
│   ├── Auth/
│   │   └── GetSetupStatus/
│   ├── Dashboard/
│   │   └── GetDashboardOverview/
│   ├── Payments/
│   │   └── GetPaymentHistory/
│   ├── Stalls/
│   │   └── GetStallsByFacility/
│   └── Reports/
│       └── GetDelinquentVendors/
├── Common/
│   └── Interface/
│       ├── ApiClients/           → Blazor → API client interfaces
│       │   ├── IAuthApiClient.cs
│       │   ├── IStallsApiClient.cs
│       │   └── IPaymentsApiClient.cs
│       └── Persistence/          → Repository interfaces
│           ├── IUnitOfWork.cs
│           ├── IStallRepository.cs
│           ├── IPaymentRepository.cs
│           └── IDashboardRepository.cs
├── Dtos/
│   ├── Auth/
│   ├── Dashboard/
│   ├── Payments/
│   └── Stalls/
├── Requests/                     → Controller request models
│   └── Stalls/
│       └── StallRequests.cs
├── Extensions/
│   └── PaginationExtensions.cs
└── DependencyInjection.cs        → AddApplicationService()
```

## Infrastructure Layer

```
EEMOCantilanSDS.Infrastructure/
├── Persistence/
│   ├── AppDbContext.cs
│   ├── UnitOfWork.cs
│   └── Seeders/
│       └── FacilitySeeder.cs
├── Configuration/                → EF entity configurations
│   ├── BaseUserConfiguration.cs
│   ├── AdminUserConfiguration.cs
│   ├── CollectorUserConfiguration.cs
│   ├── StallConfiguration.cs
│   ├── PaymentRecordConfiguration.cs
│   └── DailyCollectionConfiguration.cs
├── Repositories/
│   ├── StallRepository.cs
│   ├── PaymentRepository.cs
│   ├── DashboardRepository.cs
│   └── SetupRepository.cs
├── HttpClients/
│   ├── HandleResponse.cs         → Base class for API clients
│   └── ApiClients/               → Typed API client implementations
│       ├── AuthApiClient.cs
│       ├── StallsApiClient.cs
│       └── PaymentsApiClient.cs
├── Migrations/                   → EF Core migrations (auto-generated)
└── DependencyInjection.cs        → AddInfrastructureService()
```

## API Layer

```
EEMOCantilanSDS.Api/
├── Controllers/
│   ├── ApiBaseController.cs      → Base with HandleResponse()
│   ├── AdminAuthController.cs
│   ├── CollectorAuthController.cs
│   ├── StallsController.cs
│   ├── PaymentsController.cs
│   ├── VendorsController.cs
│   ├── FacilitiesController.cs
│   ├── CollectorsController.cs
│   ├── DailyCollectionsController.cs
│   ├── SlaughterController.cs
│   ├── TpmController.cs          → Tampak Commercial Center
│   ├── TrmController.cs
│   └── SetupController.cs
├── Middleware/
│   └── ExceptionHandlingMiddleware.cs
├── Extensions/
│   ├── AuthenticationExtensions.cs
│   └── CookieHelper.cs
├── Program.cs                    → App configuration
├── DependencyInjection.cs        → AddApi()
└── appsettings.json
```

## Client Layer (Blazor Server)

```
EEMOCantilanSDS.Client/
├── Components/
│   ├── Layout/
│   │   ├── AdminLayout.razor
│   │   ├── Sidebar.razor
│   │   └── Sidebar.razor.css
│   ├── Pages/                    → Routable pages (@page directive)
│   │   ├── Auth/
│   │   │   ├── Login.razor
│   │   │   └── Setup.razor
│   │   ├── Dashboard/
│   │   │   └── Dashboard.razor
│   │   ├── Facilities/
│   │   │   ├── Vendors.razor
│   │   │   ├── Stalls.razor
│   │   │   └── Profile.razor
│   │   └── Shared/               → Feature-specific components
│   │       ├── Toolbar.razor
│   │       ├── ActionBar.razor
│   │       └── FacilityStallsTable.razor
│   ├── Modals/
│   │   ├── FacilityPaymentModal.razor
│   │   ├── PaymentHistoryModal.razor
│   │   ├── PaymentConfirmationModal.razor
│   │   └── AddVendorModal.razor
│   ├── Shared/                   → Generic reusable components
│   ├── App.razor
│   ├── Routes.razor
│   └── _Imports.razor
├── Securities/
│   ├── AuthStateProvider.cs
│   ├── AuthService.cs
│   ├── TokenService.cs
│   ├── AuthorizationDelegatingHandler.cs
│   ├── RefreshTokenDelegatingHandler.cs
│   └── TokenCircuitHandler.cs
├── Extensions/
│   └── HttpClientExtensions.cs   → AddApiHttpClient<T>()
├── Utilities/
│   └── JwtParser.cs
├── wwwroot/
│   ├── css/
│   │   ├── site.css              → Tailwind input
│   │   └── site.min.css          → Tailwind output
│   ├── images/
│   ├── js/
│   └── app.css                   → Design tokens (CSS variables)
├── Program.cs
├── DependencyInjection.cs        → Client services
├── package.json                  → npm scripts
└── appsettings.json
```

## Key Conventions

### Naming Patterns
- **Commands:** `{Action}{Entity}Command` (e.g., `RecordPaymentCommand`)
- **Queries:** `Get{Entity}By{Filter}Query` (e.g., `GetStallsByFacilityQuery`)
- **Handlers:** `{Command/Query}Handler`
- **Validators:** `{Command/Query}Validator`
- **DTOs:** `{Entity}Dto` or `{Entity}Response`
- **Repositories:** `I{Entity}Repository` / `{Entity}Repository`
- **API Clients:** `I{Feature}ApiClient` / `{Feature}ApiClient`
- **EF Configs:** `{Entity}Configuration`

### File Organization Rules
- One command/query per folder
- Three files per command/query: record, handler, validator (always separate)
- One EF configuration per entity
- Component CSS in `.razor.css` files (scoped)
- Global CSS in `app.css` (design tokens)

### Dependency Injection
- **Application:** `AddApplicationService()` - registers MediatR, FluentValidation, behaviors
- **Infrastructure:** `AddInfrastructureService()` - registers DbContext, repositories, UnitOfWork
- **API:** `AddApi()` - registers controllers, middleware, auth
- **Client:** Registers API clients, auth services, circuit handlers

### Important Paths
- **Migrations:** Run from solution root, target Infrastructure project
- **Steering rules:** `.kiro/steering/` (this folder)
- **Documentation:** `.amazonq/rules/` (architecture, patterns, domain reference)
- **Docs:** `docs/` (component status, implementation notes)
