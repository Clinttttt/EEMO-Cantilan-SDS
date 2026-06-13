using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Auth.PayorAuth.Activate;

public class ActivatePayorAccountCommandHandler(
    IPayorRepository payorRepository,
    ITokenService tokenService,
    IUnitOfWork unitOfWork) : IRequestHandler<ActivatePayorAccountCommand, Result<TokenResponseDto>>
{
    public async Task<Result<TokenResponseDto>> Handle(ActivatePayorAccountCommand request, CancellationToken cancellationToken)
    {
        var contactNumber = request.ContactNumber!.Trim();

        var code = await payorRepository.GetActivationCodeAsync(request.ActivationCode!.Trim(), cancellationToken);

        // Validate the code without revealing which specific check failed (anti-enumeration).
        if (code is null || !code.CanBeRedeemedBy(contactNumber))
            return Result<TokenResponseDto>.Failure("Invalid or expired activation code.", 400);

        // One mobile number = one payor (enforced at code generation). If an account already exists for
        // this number, the payor has already activated — direct them to sign in. Never link the code's
        // stall onto the existing account here: a code only proves stall ownership, not that the same
        // PERSON owns the account, so auto-linking would merge two unrelated payors.
        var existing = await payorRepository.GetByContactNumberAsync(contactNumber, cancellationToken);
        if (existing is not null)
            return Result<TokenResponseDto>.Failure(
                "This mobile number is already activated. Please sign in instead.", 409);

        var payor = PayorUser.Create(request.FullName!.Trim(), contactNumber, request.Password!);
        await payorRepository.AddPayorAsync(payor, cancellationToken);

        await payorRepository.AddStallLinkAsync(PayorStallLink.Create(payor.Id, code.StallId), cancellationToken);
        code.MarkUsed(payor.Id);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<TokenResponseDto>.Success(await tokenService.CreateTokenResponse(payor));
    }
}
