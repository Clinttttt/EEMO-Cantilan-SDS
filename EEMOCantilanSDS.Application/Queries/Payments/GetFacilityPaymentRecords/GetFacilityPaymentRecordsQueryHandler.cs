using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Payments.GetFacilityPaymentRecords;

public class GetFacilityPaymentRecordsQueryHandler(IPaymentRepository paymentRepository)
    : IRequestHandler<GetFacilityPaymentRecordsQuery, Result<IReadOnlyList<FacilityPaymentRecordDto>>>
{
    public async Task<Result<IReadOnlyList<FacilityPaymentRecordDto>>> Handle(GetFacilityPaymentRecordsQuery request, CancellationToken ct)
    {
        var records = await paymentRepository.GetFacilityPaymentRecordsAsync(request.FacilityCode, request.Year, request.Month, ct);
        return Result<IReadOnlyList<FacilityPaymentRecordDto>>.Success(records);
    }
}
