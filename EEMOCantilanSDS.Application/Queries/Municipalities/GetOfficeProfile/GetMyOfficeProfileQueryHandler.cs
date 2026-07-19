using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Queries.Municipalities.GetOfficeProfile;

public class GetMyOfficeProfileQueryHandler(
    IAppDbContext context,
    ICurrentUserService currentUser) : IRequestHandler<GetMyOfficeProfileQuery, Result<OfficeProfileEditDto>>
{
    public async Task<Result<OfficeProfileEditDto>> Handle(GetMyOfficeProfileQuery request, CancellationToken ct)
    {
        // Only the caller's own LGU — the target is their municipality from the token.
        if (currentUser.MunicipalityId is not { } municipalityId || municipalityId == Guid.Empty)
            return Result<OfficeProfileEditDto>.Forbidden();

        // Municipality is a global reference table (not tenant-filtered); load by the caller's id.
        var m = await context.Municipalities
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == municipalityId, ct);
        if (m is null)
            return Result<OfficeProfileEditDto>.NotFound();

        return Result<OfficeProfileEditDto>.Success(new OfficeProfileEditDto(
            m.OfficeName, m.OfficeAcronym, m.Address, m.SealPath, m.Name, m.Province));
    }
}
