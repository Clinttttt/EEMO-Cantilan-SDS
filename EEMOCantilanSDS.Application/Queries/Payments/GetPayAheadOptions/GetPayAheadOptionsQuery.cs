using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Payments.GetPayAheadOptions;

public record GetPayAheadOptionsQuery(Guid StallId) : IRequest<Result<PayAheadOptionsDto>>;

public class GetPayAheadOptionsQueryHandler(IStallRepository stalls, BillingRules billing) : IRequestHandler<GetPayAheadOptionsQuery, Result<PayAheadOptionsDto>>
{
    public async Task<Result<PayAheadOptionsDto>> Handle(GetPayAheadOptionsQuery request, CancellationToken ct)
    {
        var stall = await stalls.GetByIdAsync(request.StallId, ct);
        return stall is null ? Result<PayAheadOptionsDto>.NotFound() : Result<PayAheadOptionsDto>.Success(await billing.PayAheadAsync(stall, ct));
    }
}
