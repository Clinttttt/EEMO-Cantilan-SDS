using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Collections;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Collections.GetCollectionActivity;

public sealed record GetCollectionReconciliationQuery(DateOnly From, DateOnly To, CollectionReconciliationBasis Basis,
    DateTimeOffset? AsOf = null, int Limit = 500) : IRequest<Result<CollectionReconciliationDto>>;

public sealed class GetCollectionReconciliationQueryHandler(ICollectionActivityReader reader, ICurrentUserService user,
    ICurrentMunicipalityAccessor tenant, IClock clock) : IRequestHandler<GetCollectionReconciliationQuery, Result<CollectionReconciliationDto>>
{
    public async Task<Result<CollectionReconciliationDto>> Handle(GetCollectionReconciliationQuery request, CancellationToken ct)
    {
        if (!user.IsAuthenticated || user.UserId is null || user.UserId == Guid.Empty) return Result<CollectionReconciliationDto>.Unauthorized();
        if (user.Role is not ("Admin" or "SuperAdmin") || tenant.MunicipalityId == Guid.Empty
            || user.MunicipalityId is { } claimed && claimed != tenant.MunicipalityId) return Result<CollectionReconciliationDto>.Forbidden();
        if (!Enum.IsDefined(request.Basis) || request.From > request.To || request.To.DayNumber - request.From.DayNumber >= 31
            || request.Limit is < 1 or > 5000 || request.AsOf > clock.UtcNow
            || request.Basis == CollectionReconciliationBasis.AsOriginallyKnown && request.AsOf is null
            || request.Basis != CollectionReconciliationBasis.AsOriginallyKnown && request.AsOf is not null)
            return Result<CollectionReconciliationDto>.Failure("Choose a period of at most 31 days and an explicit past cutoff for As Originally Known only.", ResultStatus.Invalid);
        var cutoff = request.AsOf?.UtcDateTime ?? clock.UtcNow;
        var activity = request.Basis == CollectionReconciliationBasis.CorrectionActivity;
        var events = (await reader.GetReconciliationAsync(tenant.MunicipalityId, request.From, request.To, cutoff, activity, ct))
            .OrderBy(e => activity ? e.Corrections.Min(c => c.EffectiveDate) : e.BusinessDate)
            .ThenBy(e => e.RecordedAtUtc).ThenBy(e => e.EventKey, StringComparer.Ordinal).ToArray();
        var original = activity ? 0 : events.Sum(e => e.Amount);
        var effect = events.Sum(e => e.CorrectionEffect);
        return Result<CollectionReconciliationDto>.Success(new(request.From, request.To, request.Basis, cutoff,
            events.Take(request.Limit).ToArray(), events.Length, events.Length > request.Limit, original, effect, original + effect,
            "Reconstructed canonical evidence only; legacy mutable history, rent/utility sources without dated cutover evidence, and issued submission snapshots are not reconstructed. " +
            "Correction Activity contains signed correction effects, not new receipts, deposits or remittances. Official cross-period RCD treatment remains pending Office confirmation."));
    }
}
