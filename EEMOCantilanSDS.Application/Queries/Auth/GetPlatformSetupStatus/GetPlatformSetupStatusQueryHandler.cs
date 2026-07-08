using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Queries.Auth.GetPlatformSetupStatus
{
    public class GetPlatformSetupStatusQueryHandler(IAppDbContext context)
        : IRequestHandler<GetPlatformSetupStatusQuery, Result<PlatformSetupStatusDto>>
    {
        public async Task<Result<PlatformSetupStatusDto>> Handle(GetPlatformSetupStatusQuery request, CancellationToken ct)
        {
            var operatorExists = await context.AdminUsers
                .IgnoreQueryFilters()
                .AnyAsync(u => u.IsPlatformOperator && !u.IsDeleted, ct);

            return Result<PlatformSetupStatusDto>.Success(new PlatformSetupStatusDto(!operatorExists));
        }
    }
}
