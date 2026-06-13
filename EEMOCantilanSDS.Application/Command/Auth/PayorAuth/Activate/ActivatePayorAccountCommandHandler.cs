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

        // A payor can own several stalls. If an account already exists for this contact number, this is
        // an ADDITIONAL stall: the code proves ownership of the stall, and the existing password proves
        // ownership of the account. Link it (idempotent) and sign in — no duplicate account, no error.
        var existing = await payorRepository.GetByContactNumberAsync(contactNumber, cancellationToken);
        if (existing is not null)
        {
            if (!existing.VerifyPassword(request.Password!))
                return Result<TokenResponseDto>.Failure(
                    "This mobile number already has an account. Enter your existing password to add this stall.", 409);

            if (!await payorRepository.LinkExistsAsync(existing.Id, code.StallId, cancellationToken))
                await payorRepository.AddStallLinkAsync(PayorStallLink.Create(existing.Id, code.StallId), cancellationToken);

            code.MarkUsed(existing.Id);
            existing.RecordLogin();
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<TokenResponseDto>.Success(await tokenService.CreateTokenResponse(existing));
        }

        var payor = PayorUser.Create(request.FullName!.Trim(), contactNumber, request.Password!);
        await payorRepository.AddPayorAsync(payor, cancellationToken);

        await payorRepository.AddStallLinkAsync(PayorStallLink.Create(payor.Id, code.StallId), cancellationToken);
        code.MarkUsed(payor.Id);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<TokenResponseDto>.Success(await tokenService.CreateTokenResponse(payor));
    }
}
