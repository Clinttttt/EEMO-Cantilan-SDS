using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Payors;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Payors.GetPayorPayableItems;

public class GetPayorPayableItemsQueryHandler(
    IPayorRepository payorRepository,
    ICurrentUserService currentUser) : IRequestHandler<GetPayorPayableItemsQuery, Result<IReadOnlyList<PayorPayableItemDto>>>
{
    public async Task<Result<IReadOnlyList<PayorPayableItemDto>>> Handle(GetPayorPayableItemsQuery request, CancellationToken cancellationToken)
    {
        var payorId = currentUser.UserId;
        if (payorId is null)
            return Result<IReadOnlyList<PayorPayableItemDto>>.Unauthorized();

        var items = await payorRepository.GetPayableItemsAsync(payorId.Value, cancellationToken);
        return Result<IReadOnlyList<PayorPayableItemDto>>.Success(items);
    }
}
