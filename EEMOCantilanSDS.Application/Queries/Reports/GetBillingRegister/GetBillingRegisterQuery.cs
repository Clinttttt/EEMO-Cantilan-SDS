using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Reports.GetBillingRegister;

/// <summary>The billing register of one facility as of today: every account's Statement of Account on one sheet. Read only.</summary>
public sealed record GetBillingRegisterQuery(FacilityCode Facility) : IRequest<Result<BillingRegisterDto>>;

public sealed class GetBillingRegisterQueryHandler(IFacilityReportsRepository reports) : IRequestHandler<GetBillingRegisterQuery, Result<BillingRegisterDto>>
{
    public async Task<Result<BillingRegisterDto>> Handle(GetBillingRegisterQuery request, CancellationToken ct) =>
        await reports.GetBillingRegisterAsync(request.Facility, ct) is { } register
            ? Result<BillingRegisterDto>.Success(register)
            : Result<BillingRegisterDto>.NotFound();
}
