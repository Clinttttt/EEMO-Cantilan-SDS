using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Payors;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Payors.GenerateStallActivationCode;

public class GenerateStallActivationCodeCommandHandler(
    IStallRepository stallRepository,
    ICollectorRepository collectorRepository,
    IPayorRepository payorRepository,
    ICurrentUserService currentUser,
    IUnitOfWork unitOfWork) : IRequestHandler<GenerateStallActivationCodeCommand, Result<StallActivationCodeDto>>
{
    private const int DefaultValidityDays = 30;

    public async Task<Result<StallActivationCodeDto>> Handle(GenerateStallActivationCodeCommand request, CancellationToken cancellationToken)
    {
        var stall = await stallRepository.GetByIdAsync(request.StallId, cancellationToken);
        if (stall is null)
            return Result<StallActivationCodeDto>.NotFound();

        // Collectors may only issue codes for a facility they are assigned to. Admins/heads are
        // not assignment-restricted (mirrors the payment-recording guard).
        if (currentUser.Role == "Collector")
        {
            if (currentUser.CollectorId is not { } collectorId || stall.Facility is null)
                return Result<StallActivationCodeDto>.Forbidden();

            var collector = await collectorRepository.GetByIdAsync(collectorId, cancellationToken);
            if (collector is null ||
                !collector.FacilityAssignments.Any(a => a.FacilityCode == stall.Facility.Code))
            {
                return Result<StallActivationCodeDto>.Forbidden();
            }
        }

        var issuedBy = currentUser.Username ?? "Staff";
        var contactNumber = request.ContactNumber!.Trim();

        // Only one redeemable code per stall — void any prior unredeemed one.
        await payorRepository.RevokeActiveCodesForStallAsync(stall.Id, issuedBy, cancellationToken);

        // Generate a collision-free code.
        string code;
        do
        {
            code = PayorActivationCode.GenerateCode();
        }
        while (await payorRepository.ActivationCodeExistsAsync(code, cancellationToken));

        var validityDays = request.ValidityDays is > 0 ? request.ValidityDays.Value : DefaultValidityDays;
        var expiresAt = DateTime.UtcNow.AddDays(validityDays);

        var activationCode = PayorActivationCode.Create(code, contactNumber, stall.Id, expiresAt, issuedBy);
        await payorRepository.AddActivationCodeAsync(activationCode, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<StallActivationCodeDto>.Success(new StallActivationCodeDto(
            stall.Id,
            stall.StallNo,
            stall.Facility!.Code,
            code,
            contactNumber,
            expiresAt));
    }
}
