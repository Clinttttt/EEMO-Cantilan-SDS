using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Payments.GetMonthlyExceptions;

public class GetStallMonthlyExceptionsQueryHandler(IStallMonthlyExceptionRepository exceptionRepository)
    : IRequestHandler<GetStallMonthlyExceptionsQuery, Result<IReadOnlyList<int>>>
{
    public async Task<Result<IReadOnlyList<int>>> Handle(GetStallMonthlyExceptionsQuery request, CancellationToken ct)
    {
        var list = await exceptionRepository.GetByStallYearAsync(request.StallId, request.Year, ct);
        IReadOnlyList<int> months = list.Select(x => x.BillingMonth).Distinct().OrderBy(m => m).ToList();
        return Result<IReadOnlyList<int>>.Success(months);
    }
}
