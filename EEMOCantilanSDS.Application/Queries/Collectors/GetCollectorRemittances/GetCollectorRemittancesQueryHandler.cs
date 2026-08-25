using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Collectors.GetCollectorRemittances;

public class GetCollectorRemittancesQueryHandler(ICollectorRemittanceRepository remittances)
    : IRequestHandler<GetCollectorRemittancesQuery, Result<CollectorRemittanceSummaryDto>>
{
    public async Task<Result<CollectorRemittanceSummaryDto>> Handle(
        GetCollectorRemittancesQuery request, CancellationToken ct)
    {
        if (request.To < request.From)
            return Result<CollectorRemittanceSummaryDto>.Failure(
                "The period ends before it begins.", ResultStatus.Invalid);

        var collected = await remittances.GetFeeCollectionsTotalAsync(request.CollectorId, request.From, request.To, ct);
        var filed = await remittances.ListAsync(request.CollectorId, request.From, request.To, ct);
        var remitted = filed.Sum(r => r.Amount);

        return Result<CollectorRemittanceSummaryDto>.Success(new CollectorRemittanceSummaryDto(
            request.CollectorId,
            request.From,
            request.To,
            collected,
            remitted,
            collected - remitted,
            filed.Select(r => new CollectorRemittanceLineDto(
                r.Id,
                // Stated in the office's own wall clock, since that is what the officer wrote on the receipt.
                PhilippineTime.ToPhilippineTime(r.ReceivedAt),
                r.Amount,
                r.CoversFrom,
                r.CoversTo,
                r.ReceivedByName,
                r.ReferenceNo,
                r.Notes)).ToList()));
    }
}
