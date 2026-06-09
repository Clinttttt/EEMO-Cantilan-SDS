using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Payments.GetStallLedgerSummary;

public class GetStallLedgerSummaryQueryHandler(IPaymentRepository paymentRepository)
    : IRequestHandler<GetStallLedgerSummaryQuery, Result<StallLedgerSummaryDto>>
{
    public async Task<Result<StallLedgerSummaryDto>> Handle(GetStallLedgerSummaryQuery request, CancellationToken ct)
    {
        var summary = await paymentRepository.GetStallLedgerSummaryAsync(request.StallId, ct);
        return Result<StallLedgerSummaryDto>.Success(summary);
    }
}
