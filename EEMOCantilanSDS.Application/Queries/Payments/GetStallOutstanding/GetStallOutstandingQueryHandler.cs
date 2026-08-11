using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Payments.GetStallOutstanding;

public class GetStallOutstandingQueryHandler(IStallLedgerQueries paymentRepository)
    : IRequestHandler<GetStallOutstandingQuery, Result<IReadOnlyList<PaymentHistoryDto>>>
{
    public async Task<Result<IReadOnlyList<PaymentHistoryDto>>> Handle(GetStallOutstandingQuery request, CancellationToken ct)
        => Result<IReadOnlyList<PaymentHistoryDto>>.Success(
            await paymentRepository.GetOutstandingMonthsAsync(
                request.StallId,
                request.ContractId,
                request.Year is { } y && request.Month is { } m ? new DateOnly(y, m, 1) : null,
                ct));
}
