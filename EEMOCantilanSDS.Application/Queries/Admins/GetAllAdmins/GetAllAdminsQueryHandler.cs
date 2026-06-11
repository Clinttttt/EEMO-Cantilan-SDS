using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Admins.GetAllAdmins;

public class GetAllAdminsQueryHandler(IAdminRepository adminRepo)
    : IRequestHandler<GetAllAdminsQuery, Result<IReadOnlyList<AdminListDto>>>
{
    public async Task<Result<IReadOnlyList<AdminListDto>>> Handle(
        GetAllAdminsQuery request,
        CancellationToken cancellationToken)
    {
        var admins = await adminRepo.GetAllAsync(cancellationToken);
        return Result<IReadOnlyList<AdminListDto>>.Success(admins);
    }
}
