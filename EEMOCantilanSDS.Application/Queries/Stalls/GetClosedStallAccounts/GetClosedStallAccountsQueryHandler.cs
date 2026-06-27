using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Stalls.GetClosedStallAccounts;

public class GetClosedStallAccountsQueryHandler(IStallRepository stallRepository)
    : IRequestHandler<GetClosedStallAccountsQuery, Result<IReadOnlyList<ClosedStallAccountDto>>>
{
    public async Task<Result<IReadOnlyList<ClosedStallAccountDto>>> Handle(
        GetClosedStallAccountsQuery request, CancellationToken ct)
    {
        var result = await stallRepository.GetClosedStallAccountsAsync(ct);
        return Result<IReadOnlyList<ClosedStallAccountDto>>.Success(result);
    }
}
