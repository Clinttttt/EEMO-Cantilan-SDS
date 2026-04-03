# EEMO Cantilan — Patterns Reference
# Agent: read this fully before generating any handler, repo, query, or command.

---

## CRITICAL — Never Violate These

- NEVER inject IAppDbContext, AppDbContext, or any DbContext into a handler — not even for read-only queries
- NEVER access context.PaymentRecords, context.Stalls, context.DailyCollections etc. from a handler
- NEVER use computed properties in EF/LINQ queries — TotalBill, FishFeeAmount, IsActive, TotalAmount are C# computed, not DB columns. Recalculate from raw stored fields inside the repo when aggregating.
- NEVER skip the repository — even simple dashboard aggregations go through a dedicated repo method
- NEVER reference a property on an entity that is not listed in domain.md

---

## Feature Folder Structure

All Application code is organized by feature, not by type.

Application/
  Features/
    Dashboard/
      Queries/
        GetDashboardOverview/
          GetDashboardOverviewQuery.cs
          GetDashboardOverviewQueryHandler.cs
          GetDashboardOverviewQueryValidator.cs
    Payments/
      Commands/
        RecordPayment/
          RecordPaymentCommand.cs
          RecordPaymentCommandHandler.cs
          RecordPaymentCommandValidator.cs
      Queries/
        GetPaymentHistory/
          GetPaymentHistoryQuery.cs
          GetPaymentHistoryQueryHandler.cs
          GetPaymentHistoryQueryValidator.cs
    Stalls/
    Collectors/
    Vendors/
    DailyCollections/
    Slaughterhouse/
    Auth/
  Interfaces/
    IUnitOfWork.cs
    IDashboardRepository.cs
    IPaymentRepository.cs
    IStallRepository.cs
    IDailyCollectionRepository.cs
    ISlaughterRepository.cs
    IContractRepository.cs
    ICollectorRepository.cs
  Dtos/
    Dashboard/
    Payments/
    Stalls/

One folder per feature. One subfolder per command/query. Handler + record + validator always co-located.

---

## Unit of Work

Interface — Application/Interfaces/IUnitOfWork.cs
- Contains ONLY: Task SaveChangesAsync(CancellationToken cancellationToken = default)
- No repo properties, no DbSet properties, nothing else
- No CommitAsync, no IDisposable

Implementation — Infrastructure/Persistence/UnitOfWork.cs
- Primary constructor: AppDbContext context only
- SaveChangesAsync delegates to context.SaveChangesAsync(cancellationToken)
- No repo fields or params

---

## Repository Pattern

Rule: every data access goes through a repo — no exceptions.
Handlers inject repo interfaces + IUnitOfWork. They never touch DbContext directly.

Interface: Application/Interfaces/I{Feature}Repository.cs
Implementation: Infrastructure/Persistence/Repositories/{Feature}Repository.cs

Implementation rules:
- Primary constructor: AppDbContext context
- Use context.{DbSet} internally — DbContext lives only inside repos
- Complex aggregations and projections belong inside the repo method, not the handler
- Computed properties are not DB columns — recalculate from raw stored fields inline in the repo:
  - TotalBill → use stored rate + utilities fields (check entity in domain.md for formula)
  - FishFeeAmount → d.FishKilos * 1.0m
  - SlaughterTransaction total → s.AnimalType == AnimalType.Hog ? 250m : 365m
- For dashboard/report queries: return a DTO directly from the repo — do not return entities and map in handler
- Return types: Task<TEntity?>, Task<IReadOnlyList<TEntity>>, Task<TDto>, Task<bool>, Task<int>
- Never return IQueryable out of a repo

DI Registration — Infrastructure/DependencyInjection.cs:
  services.AddScoped<IPaymentRepository, PaymentRepository>();
Each repo registered individually. Not wired inside UnitOfWork.

---

## Repo Method Naming Conventions

- GetByIdAsync(Guid id, CancellationToken ct)
- GetAllAsync(CancellationToken ct)
- GetBy{Filter}Async(FilterType value, CancellationToken ct)
- GetOverviewAsync(int year, int month, CancellationToken ct)   // dashboard-style projections
- AddAsync(TEntity entity, CancellationToken ct)
- UpdateAsync(TEntity entity, CancellationToken ct)
- IsORNumberUniqueAsync(string orNumber, CancellationToken ct)
- ExistsAsync(Guid id, CancellationToken ct)

For complex reads (dashboard, reports, summaries) — repo returns the DTO directly, not a list of entities.

---

## CQRS / MediatR

Commands and Queries:
- Defined as C# records inside their feature folder
- Commands mutate state: {Action}{Entity}Command returns Result<TDto> or Result<bool>
- Queries read data: Get{Entity}By{Filter}Query returns Result<TDto> or Result<IReadOnlyList<TDto>>
- Never return raw entities, never return void

Handler Rules:
- Always primary constructor — never private readonly fields
- Query handlers: inject only the needed repo(s) — no IUnitOfWork needed (no writes)
- Command handlers: inject repo(s) + IUnitOfWork — call uow.SaveChangesAsync() after all writes
- NEVER inject DbContext or IAppDbContext
- Query step order: call repo method → return Result<T>.Success(dto)
- Command step order: fetch → domain method → repo add/update → uow.SaveChangesAsync() → return DTO
- Business logic stays in the entity — never inline in handler
- Not found: Result<T>.NotFound() — never throw

Validator Rules:
- One AbstractValidator<T> per command/query — no exceptions
- May inject repo interfaces for async checks (e.g. uniqueness)
- OR Number uniqueness checked here via IPaymentRepository — not in handler
- Partial amount: required and > 0 only when Status == PaymentStatus.Partial
- Handlers are validation-free

Pipeline Order: Logging → Validation → Handler
Registered via AddOpenBehavior inside AddMediatR.

---

## Result Pattern — Domain/Common/Result.cs

Factory                          | HTTP | When to use
Result<T>.Success(value)         | 200  | Normal success
Result<T>.NoContent()            | 204  | Success, no body
Result<T>.Failure("msg", code)   | 400  | General failure
Result<T>.ValidationFailure(err) | 400  | Validation errors dict
Result<T>.NotFound()             | 404  | Not found
Result<T>.Unauthorized()         | 401  | Auth failure
Result<T>.Forbidden()            | 403  | Permission denied
Result<T>.Conflict()             | 409  | Duplicate / conflict
Result<T>.InternalServerError()  | 500  | Unexpected error

---

## HandleResponse — Blazor HTTP Wrapper

File: Infrastructure/HttpClients/HandleResponse.cs
All Blazor service HTTP calls go through this — never raw HttpClient.
Every method returns Result<TResponse>.
- PostAsync<TRequest, TResponse>(url, request)
- PostAsync<TResponse>(url)
- GetAsync<TResponse>(url)
- UpdateAsync<TRequest, TResponse>(url, request)
- UpdateAsync<TResponse>(url)
- DeleteAsync<TResponse>(url)

ValidationErrorResponse shape: { Errors: Dictionary<string, string[]> }

---

## EF Core Configuration Rules

File: Infrastructure/Configuration/{Entity}Configuration.cs — IEntityTypeConfiguration<TEntity>
- builder.ToTable("TableName") first
- builder.HasKey(x => x.Id) + HasColumnType("uuid")
- Strings: HasColumnType("text") or HasColumnType("character varying(n)")
- Decimals: HasColumnType("numeric(18,2)")
- Enums: store as integer — never HasDefaultValue(1)

TPH Users (CRITICAL):
- Single table "Users", discriminator "UserType" with values "Admin" / "Collector"
- HasKey() only in BaseUser config — never repeated in AdminUser or CollectorUser

Unique Indexes:
- Stall: (FacilityId, StallNo)
- PaymentRecord: (StallId, BillingYear, BillingMonth)
- DailyCollection: (StallId, CollectionDate)
- CollectorFacilityAssignment: (CollectorId, FacilityId)
- BaseUser: Username and Email separately

Computed Properties — always builder.Ignore():
- BaseUser: IsLockedOut
- SlaughterTransaction: TotalAmount
- PaymentRecord: TotalBill, BalanceDue, AmountPaid, FishFeeAmount, PeriodKey
- DailyCollection: TotalCollected, FishFeeAmount
- Contract: ExpiryDate, IsExpired, IsExpiringSoon, WholeYearRental
- Stall: IsActive

---

## Known Feature Commands and Queries

Dashboard:
- GetDashboardOverviewQuery → DashboardOverviewDto (uses IDashboardRepository)

Payments:
- RecordPaymentCommand, UpdatePaymentStatusCommand
- GetPaymentHistoryQuery

Stalls:
- CreateStallCommand, SoftDeleteStallCommand
- GetStallsByFacilityQuery

Collectors:
- AddCollectorCommand, GetCollectorActivityQuery

Daily Collections:
- RecordDailyCollectionCommand, GetDailyCollectionQuery

Slaughterhouse:
- RecordSlaughterCommand, GetSlaughterTransactionsQuery

Reports:
- GetFacilitySummaryQuery, GetDelinquentVendorsQuery

Auth:
- LoginAdminQuery, LoginCollectorQuery