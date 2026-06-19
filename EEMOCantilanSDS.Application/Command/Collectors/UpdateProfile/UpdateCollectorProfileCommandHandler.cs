using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Collectors.UpdateProfile;

public sealed class UpdateCollectorProfileCommandHandler(
    ICollectorRepository collectorRepository,
    ICurrentUserService currentUser,
    IUnitOfWork unitOfWork) : IRequestHandler<UpdateCollectorProfileCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(UpdateCollectorProfileCommand request, CancellationToken ct)
    {
        // A collector may only edit their OWN profile — the id comes from the token, not the request.
        if (currentUser.CollectorId is not { } collectorId)
            return Result<bool>.Forbidden();

        var collector = await collectorRepository.GetByIdAsync(collectorId, ct);
        if (collector is null)
            return Result<bool>.NotFound();

        collector.UpdateProfile(
            request.FullName.Trim(),
            request.ContactNumber.Trim(),
            request.Email.Trim(),
            currentUser.Username ?? "Collector");

        await unitOfWork.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }
}
