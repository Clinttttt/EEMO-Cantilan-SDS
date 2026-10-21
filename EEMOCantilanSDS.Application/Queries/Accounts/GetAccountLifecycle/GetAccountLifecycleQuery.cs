using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Lifecycle;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Accounts.GetAccountLifecycle;

/// <summary>The read-only lifecycle of one account: its state, history and the actions available (with reasons).</summary>
public sealed record GetAccountLifecycleQuery(LifecycleSource Source, Guid AccountId) : IRequest<Result<AccountLifecycleDto>>;

public sealed class GetAccountLifecycleQueryHandler(
    AccountLifecycleResolver adapters,
    ICurrentUserService user,
    ICurrentMunicipalityAccessor tenant) : IRequestHandler<GetAccountLifecycleQuery, Result<AccountLifecycleDto>>
{
    public async Task<Result<AccountLifecycleDto>> Handle(GetAccountLifecycleQuery request, CancellationToken ct)
    {
        if (!user.IsAuthenticated || user.UserId is not { } userId || userId == Guid.Empty) return Result<AccountLifecycleDto>.Unauthorized();
        // Viewing is Admin or SuperAdmin everywhere it exists today (StallsController / ObligationsController reads).
        if (!LifecycleRules.HasRole(user.Role, "Admin")) return Result<AccountLifecycleDto>.Forbidden();
        if (tenant.MunicipalityId == Guid.Empty || user.MunicipalityId is { } claimed && claimed != tenant.MunicipalityId)
            return Result<AccountLifecycleDto>.Forbidden();
        if (!Enum.IsDefined(request.Source) || request.AccountId == Guid.Empty)
            return Result<AccountLifecycleDto>.Failure("Choose a valid account source and account.", ResultStatus.Invalid);

        var adapter = adapters.For(request.Source);
        var dto = adapter is null ? null : await adapter.GetAsync(request.Source, request.AccountId, user.Role, ct);
        // Not found covers "no such account" and "another municipality's account" alike: nothing is revealed across tenants.
        return dto is null ? Result<AccountLifecycleDto>.NotFound() : Result<AccountLifecycleDto>.Success(dto);
    }
}

/// <summary>
/// The lifecycle of several accounts of one source in a single request, for a register that shows an action state per row (Closed Accounts).
/// Accounts that do not exist in the caller's municipality are simply absent. Capped so one request cannot sweep a municipality.
/// </summary>
public sealed record GetAccountLifecycleBatchQuery(LifecycleSource Source, IReadOnlyList<Guid> AccountIds) : IRequest<Result<IReadOnlyList<AccountLifecycleDto>>>;

public sealed class GetAccountLifecycleBatchQueryHandler(
    AccountLifecycleResolver adapters,
    ICurrentUserService user,
    ICurrentMunicipalityAccessor tenant) : IRequestHandler<GetAccountLifecycleBatchQuery, Result<IReadOnlyList<AccountLifecycleDto>>>
{
    public const int MaxAccounts = 200;

    public async Task<Result<IReadOnlyList<AccountLifecycleDto>>> Handle(GetAccountLifecycleBatchQuery request, CancellationToken ct)
    {
        if (!user.IsAuthenticated || user.UserId is not { } userId || userId == Guid.Empty) return Result<IReadOnlyList<AccountLifecycleDto>>.Unauthorized();
        if (!LifecycleRules.HasRole(user.Role, "Admin")) return Result<IReadOnlyList<AccountLifecycleDto>>.Forbidden();
        if (tenant.MunicipalityId == Guid.Empty || user.MunicipalityId is { } claimed && claimed != tenant.MunicipalityId)
            return Result<IReadOnlyList<AccountLifecycleDto>>.Forbidden();
        var ids = request.AccountIds.Where(x => x != Guid.Empty).Distinct().ToList();
        if (!Enum.IsDefined(request.Source) || ids.Count == 0 || ids.Count > MaxAccounts)
            return Result<IReadOnlyList<AccountLifecycleDto>>.Failure($"Choose a valid account source and between 1 and {MaxAccounts} accounts.", ResultStatus.Invalid);

        var adapter = adapters.For(request.Source);
        var found = new List<AccountLifecycleDto>();
        if (adapter is not null)
            foreach (var id in ids)
                if (await adapter.GetAsync(request.Source, id, user.Role, ct) is { } dto) found.Add(dto);
        return Result<IReadOnlyList<AccountLifecycleDto>>.Success(found);
    }
}
