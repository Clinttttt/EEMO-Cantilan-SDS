using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Payors;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Payors.GetPayorBalances;

public class GetPayorBalancesQueryHandler(
    IPayorRepository payorRepository,
    ICurrentUserService currentUser) : IRequestHandler<GetPayorBalancesQuery, Result<IReadOnlyList<PayorStallBalanceDto>>>
{
    public async Task<Result<IReadOnlyList<PayorStallBalanceDto>>> Handle(GetPayorBalancesQuery request, CancellationToken cancellationToken)
    {
        var payorId = currentUser.UserId;
        if (payorId is null)
            return Result<IReadOnlyList<PayorStallBalanceDto>>.Unauthorized();

        var balances = await payorRepository.GetBalancesAsync(payorId.Value, cancellationToken);
        return Result<IReadOnlyList<PayorStallBalanceDto>>.Success(balances);
    }
}
