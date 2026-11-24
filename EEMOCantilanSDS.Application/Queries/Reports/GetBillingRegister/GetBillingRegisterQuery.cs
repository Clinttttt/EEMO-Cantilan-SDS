using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Reports.GetBillingRegister;

/// <summary>The billing register of one facility as of today, or as of the last day of an earlier month when one is asked for:
/// every account's Statement of Account on one sheet. Read only.</summary>
public sealed record GetBillingRegisterQuery(FacilityCode Facility, int? Year = null, int? Month = null) : IRequest<Result<BillingRegisterDto>>;

public sealed class GetBillingRegisterQueryHandler(IFacilityReportsRepository reports) : IRequestHandler<GetBillingRegisterQuery, Result<BillingRegisterDto>>
{
    public async Task<Result<BillingRegisterDto>> Handle(GetBillingRegisterQuery request, CancellationToken ct)
    {
        DateOnly? asOf = null;
        if (request.Year is not null || request.Month is not null)
        {
            if (request.Year is not (>= 2000 and <= 9999) || request.Month is not (>= 1 and <= 12))
                return Result<BillingRegisterDto>.Failure("State the month as a year and a month from 1 to 12.");
            var first = new DateOnly(request.Year.Value, request.Month.Value, 1);
            asOf = first.AddMonths(1).AddDays(-1);
        }
        return await reports.GetBillingRegisterAsync(request.Facility, asOf, ct) is { } register
            ? Result<BillingRegisterDto>.Success(register)
            : Result<BillingRegisterDto>.NotFound();
    }
}
