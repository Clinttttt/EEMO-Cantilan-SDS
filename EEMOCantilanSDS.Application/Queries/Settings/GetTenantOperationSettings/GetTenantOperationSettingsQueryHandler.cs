using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Onboarding;
using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Settings.GetTenantOperationSettings;

public sealed class GetTenantOperationSettingsQueryHandler(IAppDbContext context, ICurrentUserService currentUser)
    : IRequestHandler<GetTenantOperationSettingsQuery, Result<TenantOperationSettingsDto>>
{
    public async Task<Result<TenantOperationSettingsDto>> Handle(GetTenantOperationSettingsQuery request, CancellationToken ct)
    {
        // The municipality is the caller's own, from their token - never from the request - so one LGU cannot read another's.
        if (!currentUser.IsAuthenticated || currentUser.MunicipalityId is not { } municipalityId || municipalityId == Guid.Empty)
            return Result<TenantOperationSettingsDto>.Forbidden();

        return Result<TenantOperationSettingsDto>.Success(await TenantOperationSettingsStore.ReadAsync(context, municipalityId, ct));
    }
}
