# EEMO Cantilan — Patterns Reference
# Agent: read this fully before generating any handler, repo, query, or command.

---

## API Client Pattern (Blazor → API)

All Blazor HTTP calls use typed API clients — never inject HttpClient directly into components.

**HttpClient Extension Location:** `Client/Extensions/HttpClientExtensions.cs`

Pattern:
1. Define interface in `Application/Common/Interface/ApiClients/I{Feature}ApiClient.cs`
2. Implement in `Infrastructure/HttpClients/ApiClients/{Feature}ApiClient.cs` extending `HandleResponse`
3. Register in `Client/DependencyInjection.cs` using `AddApiHttpClient<TClient, TImplementation>(configuration)` extension

The `AddApiHttpClient` extension (from `HttpClientExtensions.cs`) automatically:
- Sets BaseAddress from configuration (`ApiBaseUrl`)
- Adds RefreshTokenDelegatingHandler
- Adds AuthorizationDelegatingHandler

Extension signature:
```csharp
public static IHttpClientBuilder AddApiHttpClient<TClient, TImplementation>
    (this IServiceCollection services, IConfiguration configuration)
    where TClient : class where TImplementation : class, TClient
```

For public endpoints (no auth needed like login), use raw `AddHttpClient` instead.

Component usage:
```csharp
@inject IStallsApiClient StallsApi

var result = await StallsApi.GetStallHoldersList();
if (result.IsSuccess) { ... }
```

---

## Cursor Pagination

For scrollable/infinite scroll tables, use cursor-based pagination.

**Extension Method Location:** `Application/Extensions/PaginationExtensions.cs`

Query Pattern:
- Add `DateTime? Cursor` and `int PageSize` parameters
- Return `Result<CursorPagedResult<TDto>>`
- Example: `GetStallsByFacilityPaginatedQuery(FacilityCode, Section, Cursor, PageSize)`

Repository Pattern:
- Filter by cursor: `if (cursor.HasValue) query = query.Where(x => x.CreatedAt < cursor.Value);`
- Order by CreatedAt descending: `query = query.OrderByDescending(x => x.CreatedAt);`
- Use extension: `await query.ToCursorPagedResultAsync(pageSize, x => x.CreatedAt, ct);`
- Returns `CursorPagedResult<T>` with `Items`, `NextCursor`, `HasMore`
- Extension signature: `ToCursorPagedResultAsync<T>(this IQueryable<T> query, int pageSize, Func<T, DateTime?> cursorSelector, CancellationToken ct)`

Validation:
- PageSize: `GreaterThan(0)` and `LessThanOrEqualTo(100)`

Frontend:
- Initial load: pass `Cursor = null`
- Load more: pass `NextCursor` from previous response
- Stop when `HasMore = false`

CursorPagedResult Model (Domain/Common/CursorPagedResult.cs):
```csharp
public class CursorPagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public DateTime? NextCursor { get; set; }
    public bool HasMore { get; set; }
}
```

---

## CRITICAL — Never Violate These

- NEVER inject IAppDbContext, AppDbContext, or any DbContext into a handler — not even for read-only queries
- NEVER access context.PaymentRecords, context.Stalls, context.DailyCollections etc. from a handler
- NEVER use computed properties in EF/LINQ queries — TotalBill, FishFeeAmount, IsActive, TotalAmount are C# computed, not DB columns. Recalculate from raw stored fields inside the repo when aggregating.
- NEVER skip the repository — even simple dashboard aggregations go through a dedicated repo method
- NEVER reference a property on an entity that is not listed in domain.md
- NEVER check !x.IsDeleted in queries — HasQueryFilter handles soft delete filtering automatically
- NEVER use IActionResult — always use ActionResult<T> with the generic type matching the Result<T> from the handler
- NEVER inject HttpClient directly into Blazor components — always use typed API clients

---

## Feature Folder Structure

All Application code is organized by feature, not by type.

Application/
  Command/
    Auth/
      AdminAuth/
        Login/
        CreateFirstAdmin/
      RefreshToken/
    Collectors/
      CreateCollector/
    Payments/
      SaveOrNumber/
    Stalls/
      ToggleStallStatus/
  Queries/
    Auth/
      GetSetupStatus/
    Dashboard/
      GetDashboardOverview/
    Payments/
      GetPaymentHistory/
    Stalls/
  Common/
    Interface/
      ApiClients/
        IAuthApiClient.cs
        ISetupApiClient.cs
        IStallsApiClient.cs
      Persistence/
        IUnitOfWork.cs
        ISetupRepository.cs
        IPaymentRepository.cs
        IStallRepository.cs
  Dtos/
    Dashboard/
    Payments/
    Stalls/

One folder per command/query. Each folder contains EXACTLY 3 separate files:
- `{Name}Command.cs` or `{Name}Query.cs` — record definition only
- `{Name}CommandHandler.cs` or `{Name}QueryHandler.cs` — handler class only
- `{Name}CommandValidator.cs` or `{Name}QueryValidator.cs` — validator class only

NEVER put record + handler + validator in the same file. Always 3 files, always separate.

---

## Unit of Work

Interface — Application/Common/Interface/Persistence/IUnitOfWork.cs
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

Interface: Application/Common/Interface/Persistence/I{Feature}Repository.cs
Implementation: Infrastructure/Repositories/{Feature}Repository.cs

Implementation rules:
- Primary constructor: AppDbContext context
- Namespace: `EEMOCantilanSDS.Infrastructure.Repositories`
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
- IsSuperAdminExistsAsync(CancellationToken ct)  // Setup check

For complex reads (dashboard, reports, summaries) — repo returns the DTO directly, not a list of entities.

---

## CQRS / MediatR

### File Structure (STRICT — 3 files per feature folder)
```
Command/Payments/RecordPayment/
  RecordPaymentCommand.cs          ← record only, implements IRequest<Result<T>>
  RecordPaymentCommandHandler.cs   ← handler class only
  RecordPaymentCommandValidator.cs ← validator class only
```
Same pattern for Queries:
```
Queries/Payments/GetPaymentHistory/
  GetPaymentHistoryQuery.cs
  GetPaymentHistoryQueryHandler.cs
  GetPaymentHistoryQueryValidator.cs
```

### Command Record File (`{Name}Command.cs`)
```csharp
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Payments.RecordPayment;

public record RecordPaymentCommand(
    Guid StallId,
    int Year,
    int Month,
    PaymentStatus Status,
    decimal? PartialAmount,
    string? Remarks
) : IRequest<Result<bool>>;
```

### Handler File (`{Name}CommandHandler.cs`)
```csharp
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Payments.RecordPayment;

public class RecordPaymentCommandHandler(
    IPaymentRepository paymentRepository,
    IStallRepository stallRepository,
    IUnitOfWork unitOfWork) : IRequestHandler<RecordPaymentCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(RecordPaymentCommand request, CancellationToken ct)
    {
        // fetch via repo → domain method → repo add/update → uow.SaveChangesAsync()
    }
}
```

### Validator File (`{Name}CommandValidator.cs`)
```csharp
using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Payments.RecordPayment;

public class RecordPaymentCommandValidator : AbstractValidator<RecordPaymentCommand>
{
    public RecordPaymentCommandValidator()
    {
        RuleFor(x => x.StallId).NotEmpty();
        // inject repo interface in constructor only when async uniqueness check needed
    }
}
```

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
- OR Number uniqueness checked here via repo.IsORNumberUniqueAsync() — not in handler
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
Result<T>.Conflict()             | 409  | Duplicate / conflict (e.g. SuperAdmin already exists)
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

## Request Records (Controller Input Models)

When a controller action needs a request body that doesn't map 1:1 to a Command (e.g. a simple wrapper with 1-2 fields), define it as a record in the Application layer:

**Location:** `Application/Requests/{Feature}/{Feature}Requests.cs`  
**Namespace:** `EEMOCantilanSDS.Application.Requests.{Feature}`

Example:
```csharp
// Application/Requests/Stalls/StallRequests.cs
namespace EEMOCantilanSDS.Application.Requests.Stalls;

public record ToggleStallStatusRequest(bool Close);
```

Rules:
- NEVER define request records inline at the bottom of a controller file
- Group all requests for a feature in one file: `{Feature}Requests.cs`
- Controllers reference them via `using EEMOCantilanSDS.Application.Requests.{Feature};`
- If the request body maps directly to a Command, bind `[FromBody]` to the Command directly — no wrapper needed

---

## API Controllers

File: Api/Controllers/{Entity}Controller.cs
- Inherit from ApiBaseController
- Primary constructor: ISender sender
- Return type: ActionResult<T> where T matches the Result<T> generic type from the handler
- Example: `public async Task<ActionResult<StallDto>> GetStall(Guid id)` for handler returning `Result<StallDto>`
- Example: `public async Task<ActionResult<IReadOnlyList<StallDto>>> GetStalls()` for handler returning `Result<IReadOnlyList<StallDto>>`
- Example: `public async Task<ActionResult<bool>> DeleteStall(Guid id)` for handler returning `Result<bool>`
- Always call HandleResponse(result) to convert Result<T> to ActionResult<T>
- Controllers are thin — only IMediator.Send() and HandleResponse(), nothing else

---

## Known Feature Commands and Queries

Auth:
- LoginCommand (Command/Auth/AdminAuth/Login)
- RefreshTokenCommand (Command/Auth/RefreshToken)
- CreateFirstAdminCommand (Command/Auth/AdminAuth/CreateFirstAdmin) — checks IsSuperAdminExistsAsync, returns Conflict if exists
- GetSetupStatusQuery (Queries/Auth/GetSetupStatus) — returns IsSetupRequired = !IsSuperAdminExistsAsync

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
