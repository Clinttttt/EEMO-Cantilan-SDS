using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Auth.GetSetupStatus;

public class GetSetupStatusQueryHandler(ISetupRepository setupRepository) : IRequestHandler<GetSetupStatusQuery, Result<SetupStatusDto>>
{
    public async Task<Result<SetupStatusDto>> Handle(GetSetupStatusQuery request, CancellationToken ct)
    {
        var isSuperAdminExists = await setupRepository.IsSuperAdminExistsAsync(ct);
        return Result<SetupStatusDto>.Success(new SetupStatusDto(!isSuperAdminExists));
    }
}
