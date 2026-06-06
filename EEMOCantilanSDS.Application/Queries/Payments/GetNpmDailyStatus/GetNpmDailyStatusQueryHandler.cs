using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Payments.GetNpmDailyStatus;

public class GetNpmDailyStatusQueryHandler(IPaymentRepository paymentRepository)
    : IRequestHandler<GetNpmDailyStatusQuery, Result<IReadOnlyList<NpmStallDailyStatusDto>>>
{
    public async Task<Result<IReadOnlyList<NpmStallDailyStatusDto>>> Handle(GetNpmDailyStatusQuery request, CancellationToken ct)
    {
        var status = await paymentRepository.GetNpmDailyStatusAsync(request.FacilityCode, request.Year, request.Month, ct);
        return Result<IReadOnlyList<NpmStallDailyStatusDto>>.Success(status);
    }
}
