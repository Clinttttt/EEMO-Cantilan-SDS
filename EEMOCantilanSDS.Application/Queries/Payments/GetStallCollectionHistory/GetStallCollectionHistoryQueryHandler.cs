using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Payments.GetStallCollectionHistory;

public class GetStallCollectionHistoryQueryHandler(IPaymentRepository paymentRepository)
    : IRequestHandler<GetStallCollectionHistoryQuery, Result<CursorPagedResult<StallCollectionHistoryRowDto>>>
{
    public async Task<Result<CursorPagedResult<StallCollectionHistoryRowDto>>> Handle(
        GetStallCollectionHistoryQuery request, CancellationToken ct)
    {
        var result = await paymentRepository.GetStallCollectionHistoryAsync(
            request.StallId, request.Cursor, request.PageSize, ct);
        return Result<CursorPagedResult<StallCollectionHistoryRowDto>>.Success(result);
    }
}
