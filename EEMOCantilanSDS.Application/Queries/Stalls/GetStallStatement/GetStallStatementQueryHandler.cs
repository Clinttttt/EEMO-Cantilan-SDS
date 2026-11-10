using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Stalls.GetStallStatement;

public class GetStallStatementQueryHandler(IFacilityReportsRepository reports)
    : IRequestHandler<GetStallStatementQuery, Result<StallStatementDto>>
{
    public async Task<Result<StallStatementDto>> Handle(GetStallStatementQuery request, CancellationToken ct)
    {
        var statement = await reports.GetStallStatementAsync(request.StallId, ct);
        return statement is null ? Result<StallStatementDto>.NotFound() : Result<StallStatementDto>.Success(statement);
    }
}
