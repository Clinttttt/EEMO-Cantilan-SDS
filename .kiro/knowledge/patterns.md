# Patterns Reference

Copy these shapes. Consistency beats cleverness — if a new need does not fit, raise it rather than inventing a
second convention.

---

## 1. Command (write)

Three files in `Application/Command/{Feature}/{UseCase}/`.

```csharp
// CreateStallCommand.cs
public record CreateStallCommand(
    FacilityCode FacilityCode,
    string StallNo,
    decimal MonthlyRate,
    ApplicableFees Fees,
    MarketSection? Section,
    /// <summary>Null means "not supplied — leave the stored rate alone".</summary>
    decimal? DailyRate,
    string ActualOccupant,
    DateTime? ContractDate,
    int ContractYears) : IRequest<Result<StallDto>>;
```

```csharp
// CreateStallCommandHandler.cs — primary constructor, interfaces only
public class CreateStallCommandHandler(
    IStallRepository stallRepo,
    IFacilityRepository facilityRepo,
    IUnitOfWork uow,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext) : IRequestHandler<CreateStallCommand, Result<StallDto>>
{
    public async Task<Result<StallDto>> Handle(CreateStallCommand request, CancellationToken ct)
    {
        var facility = await facilityRepo.GetByCodeAsync(request.FacilityCode, ct);
        if (facility is null)
            return Result<StallDto>.NotFound();

        var stall = Stall.Create(facility.Id, request.StallNo, request.MonthlyRate, request.Fees,
                                 request.Section, dailyRate: request.DailyRate, createdBy: "Admin");

        await stallRepo.AddAsync(stall, ct);
        await uow.SaveChangesAsync(ct);                                  // the only commit point
        await cacheInvalidator.InvalidateReferenceDataAsync(tenantContext.TenantCode, ct);

        return Result<StallDto>.Success(Map(stall));
    }
}
```

```csharp
// CreateStallCommandValidator.cs
public class CreateStallCommandValidator : AbstractValidator<CreateStallCommand>
{
    public CreateStallCommandValidator()
    {
        RuleFor(x => x.StallNo).NotEmpty().MaximumLength(20);
        RuleFor(x => x.MonthlyRate).GreaterThanOrEqualTo(0m);
    }
}
```

Rules: no validation inside the handler; no `DbContext`; return `Result<T>`; invalidate cache after a mutation.

---

## 2. Query (read), with caching

```csharp
public class GetStallHoldersListQueryHandler(
    IStallRepository stallRepository,
    IEemoAppCache cache,
    ITenantContext tenantContext,
    EemoCacheOptions cacheOptions)
    : IRequestHandler<GetStallHoldersListQuery, Result<StallHoldersListDto>>
{
    public async Task<Result<StallHoldersListDto>> Handle(GetStallHoldersListQuery request, CancellationToken ct)
    {
        var key = EemoCacheKeys.StallHolderList(tenantContext.TenantCode, request.FacilityCode, request.Section, request.SearchTerm);
        var regions = EemoCacheRegions.StallHolderListRegions(tenantContext.TenantCode);

        var result = await cache.GetOrCreateAsync(
            key, regions, cacheOptions.StallHolderListTtl,
            token => stallRepository.GetStallHoldersListAsync(request.FacilityCode, request.Section, request.SearchTerm, token),
            ct);

        return Result<StallHoldersListDto>.Success(result);
    }
}
```

**The cached value is shared.** Anything derived (a resolved rate, a computed monthly figure) must be computed
inside the repository projection, before it enters the cache — never by mutating the value afterwards.

---

## 3. Repository

```csharp
public class StallRepository(AppDbContext context, IFeeRateResolver feeRateResolver) : IStallRepository
{
    // Convenience ctor keeps existing tests (`new StallRepository(context)`) working.
    public StallRepository(AppDbContext context) : this(context, new FeeRateResolver(context)) { }

    public async Task<StallHoldersListDto> GetStallHoldersListAsync(
        FacilityCode facilityCode, MarketSection? section, string? searchTerm, CancellationToken ct)
    {
        var stalls = await context.Stalls
            .AsNoTracking()
            .Include(s => s.Contracts)
            .Where(s => s.Facility!.Code == facilityCode && s.Status != StallStatus.Closed)
            .ToListAsync(ct);

        // Rates are per tenant and resolved AS OF a date.
        var snapshot = await feeRateResolver.GetSnapshotAsync(ct);
        var dailyRate = snapshot.Resolve(FeeRateKey.NpmDailyStall, DateOnly.FromDateTime(PhilippineTime.Now));

        // One rule for the money, shared with billing and settlement.
        decimal MonthlyOf(Stall s) => facilityCode == FacilityCode.NPM
            ? s.ResolveDailyFee(dailyRate) * DomainRules.DailyBilledMonthDays
            : s.MonthlyRate;

        return new StallHoldersListDto { /* rows + section totals + grand totals, all via MonthlyOf */ };
    }
}
```

---

## 4. Controller

```csharp
[Authorize(Roles = "SuperAdmin,Admin")]
[Route("api/stalls")]
[ApiController]
public class StallsController(ISender sender) : ApiBaseController(sender)
{
    [HttpGet("holders")]
    public async Task<ActionResult<StallHoldersListDto>> GetHoldersAsync(
        [FromQuery] FacilityCode facilityCode, [FromQuery] MarketSection? section, [FromQuery] string? search)
        => HandleResponse(await Sender.Send(new GetStallHoldersListQuery(facilityCode, section, search)));
}
```

Thin: authorise, send, hand back. Sensitive mutations add `[EnableRateLimiting("auth")]`.

---

## 5. Typed API client

```csharp
// Application/Common/Interface/ApiClients/IMfaApiClient.cs
public interface IMfaApiClient
{
    Task<Result<MfaStatusDto>> GetMfaStatusAsync();
    Task<Result<MfaEnrollmentDto>> BeginMfaEnrollmentAsync(BeginMfaEnrollmentCommand command);
}

// HttpClients/ApiClients/MfaApiClient.cs
public class MfaApiClient(HttpClient http) : HandleResponse, IMfaApiClient
{
    public async Task<Result<MfaStatusDto>> GetMfaStatusAsync() =>
        await GetAsync<MfaStatusDto>("api/AdminAuth/mfa/status");

    public async Task<Result<MfaEnrollmentDto>> BeginMfaEnrollmentAsync(BeginMfaEnrollmentCommand command) =>
        await PostAsync<BeginMfaEnrollmentCommand, MfaEnrollmentDto>("api/AdminAuth/mfa/enroll", command);
}
```

Registration decides whether calls are authenticated — get this wrong and every call silently 401s:

```csharp
// Authenticated: attaches loading, refresh and authorization handlers.
service.AddApiHttpClient<IMfaApiClient, MfaApiClient>(configuration);

// Anonymous ONLY (login, refresh, logout, mfa/verify-login). No [Authorize] endpoint belongs here.
service.AddHttpClient<IAuthApiClient, AuthApiClient>("AuthClient", c => { /* ... */ });
```

---

## 6. Blazor page

```razor
@page "/stalls"
@attribute [Authorize(Roles = "SuperAdmin,Admin")]
@rendermode InteractiveServer
@inject IStallsApiClient StallsApi

<PageTitle>@Branding.OfficeAcronym Admin — Stalls</PageTitle>

@if (_loading) { <Skeleton Width="70%" Height="12px" /> }
else if (_error is not null) { <div class="form-error">@_error</div> }
else { /* table */ }

@code {
    private bool _loading = true;
    private string? _error;

    [PersistentState(AllowUpdates = true)]
    public StallsPageState? Cached { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await Branding.EnsureLoadedAsync();
        if (Cached is { } state) { /* reuse, skip the fetch */ return; }
        await LoadAsync();
    }

    // A one-time token is consumed HERE, not in OnInitializedAsync — that runs twice under prerendering.
    protected override async Task OnAfterRenderAsync(bool firstRender) { if (firstRender) { /* ... */ } }
}
```

Every label comes from `Branding` or the facility catalog. Never a literal "Cantilan", "EEMO", ₱30 or ₱900.

---

## 7. Shared component with a fallback

```razor
@if (_paths is not null)
{
    <svg class="fmark" viewBox="0 0 24 24" width="@Size" height="@Size" aria-hidden="true"
         fill="none" stroke="@FacilityMarkArt.StrokeFor(Ink)" stroke-width="1.8">
        @((MarkupString)_paths)
    </svg>
}
else { @Fallback }

@code {
    [Parameter, EditorRequired] public FacilityCode Code { get; set; }
    [Parameter] public FacilityMarkInk Ink { get; set; } = FacilityMarkInk.Navy;
    [Parameter] public int Size { get; set; } = 22;
    /// <summary>Rendered when this facility has no dedicated artwork.</summary>
    [Parameter] public RenderFragment? Fallback { get; set; }
}
```

Render a shared mark in the SHARED host (`FacilityHero`, `FacilityPage`) rather than in each page, so a new
page cannot forget it. Give the component its own `.razor.css`.

---

## 8. Guard usage

```csharp
if (!await PlatformOperatorGuard.IsCurrentAsync(context, currentUser, ct))
    return Result<T>.Forbidden();

// Cross-tenant reach needs the real operator flag; the default-municipality Head fallback is scoped to itself.
var seesEveryMunicipality = await PlatformOperatorGuard.IsDedicatedOperatorAsync(context, currentUser, ct);
if (!seesEveryMunicipality)
{
    if (currentUser.MunicipalityId is not Guid own) return Result<T>.Forbidden();
    query = query.Where(u => u.MunicipalityId == own);
}
```

Out-of-scope targets answer `NotFound()`, not `Forbidden()`, so a response never confirms that a record outside
the caller's scope exists.

---

## 9. Tests

```csharp
public class StallHoldersListDailyRateTests : RepositoryTestBase
{
    [Fact]
    public async Task Npm_DerivesMonthlyFromTheTenantsDailyRate_NotTheStoredMonthlyRate()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var rate = FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 40m, new DateOnly(2020, 1, 1), Guid.Empty);

        context.AddRange(facility, stall, /* contract */ rate);
        await context.SaveChangesAsync();

        var dto = await new StallRepository(context).GetStallHoldersListAsync(FacilityCode.NPM, null, null, default);

        Assert.Equal(1_200m, Assert.Single(Assert.Single(dto.Sections).Rows).MonthlyRentalRate);  // ₱40 × 30
    }

    [Fact]
    public async Task Npm_WithNoTenantRate_KeepsTheOrdinanceFigures_SoCantilanIsUnchanged() { /* ₱900 */ }
}
```

Handler tests use Moq for repositories; bUnit tests register the `_Imports` services and pass an explicit
`WaitForAssertion` timeout. Prove a fix by reintroducing the defect once and watching the test fail.

---

## 10. Naming

| Thing | Shape |
|-------|-------|
| Command / Query | `{Action}{Entity}Command` / `Get{Entity}By{Filter}Query` |
| Handler / Validator | `{Name}Handler` / `{Name}Validator` |
| DTO | `{Entity}Dto` |
| Repository | `I{Entity}Repository` / `{Entity}Repository` |
| API client | `I{Feature}ApiClient` / `{Feature}ApiClient` |
| EF configuration | `{Entity}Configuration` |
| Page state record | `{Page}PageState` |
