using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Payors.GetMyPayorProfile;

public class GetMyPayorProfileQueryHandler(
    IPayorRepository payorRepository,
    ICurrentUserService currentUser) : IRequestHandler<GetMyPayorProfileQuery, Result<PayorProfileDto>>
{
    public async Task<Result<PayorProfileDto>> Handle(GetMyPayorProfileQuery request, CancellationToken ct)
    {
        if (currentUser.UserId is not { } payorId)
            return Result<PayorProfileDto>.Unauthorized();

        var payor = await payorRepository.GetPayorByIdAsync(payorId, ct);
        if (payor is null) return Result<PayorProfileDto>.Unauthorized();

        // Read from the row, not from the token's claims: a name corrected in the register should reach the payor's own
        // screen without waiting for them to sign in again.
        return Result<PayorProfileDto>.Success(new PayorProfileDto(
            payor.FullName ?? string.Empty,
            payor.Username ?? string.Empty));
    }
}
