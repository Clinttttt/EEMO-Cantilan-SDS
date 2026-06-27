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

        // One mobile number = one payor. Refuse to issue a code that would later collide with another
        // payor on activation. Without this, two different occupants given codes under the same number
        // get merged into a single account (one ends up owning the other's stall).
        var alreadyRegistered = await payorRepository.GetByContactNumberAsync(contactNumber, cancellationToken);
        if (alreadyRegistered is not null)
        {
            var ownsThisStall = await payorRepository.LinkExistsAsync(alreadyRegistered.Id, stall.Id, cancellationToken);
            return Result<StallActivationCodeDto>.Failure(
                ownsThisStall
                    ? "This stall is already linked to an activated payor account."
                    : "This mobile number is already registered to a payor account. Use a different number.",
                409);
        }

        if (await payorRepository.ActiveCodeExistsForContactOnOtherStallAsync(contactNumber, stall.Id, cancellationToken))
            return Result<StallActivationCodeDto>.Failure(
                "This mobile number already has a pending activation code for another stall.", 409);

        // One activation record per stall: remove any prior code(s) so re-issuing REPLACES the old
        // instead of accumulating a new row each time. Only the newest code is ever redeemable.
        await payorRepository.RemoveCodesForStallAsync(stall.Id, cancellationToken);

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
