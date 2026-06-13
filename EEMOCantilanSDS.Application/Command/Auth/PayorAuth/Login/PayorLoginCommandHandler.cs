using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace EEMOCantilanSDS.Application.Command.Auth.PayorAuth.Login;

public class PayorLoginCommandHandler(
    IPayorRepository payorRepository,
    ITokenService tokenService,
    IUnitOfWork unitOfWork) : IRequestHandler<PayorLoginCommand, Result<TokenResponseDto>>
{
    public async Task<Result<TokenResponseDto>> Handle(PayorLoginCommand request, CancellationToken cancellationToken)
    {
        var payor = await payorRepository.GetByContactNumberAsync(request.ContactNumber!.Trim(), cancellationToken);

        if (payor is null)
            return Result<TokenResponseDto>.NotFound();

        if (payor.IsLockedOut)
            return Result<TokenResponseDto>.Unauthorized();

        var verification = new PasswordHasher<BaseUser>().VerifyHashedPassword(
            payor, payor.PasswordHash, request.Password!);

        if (verification == PasswordVerificationResult.Failed)
        {
            payor.RecordFailedLogin();
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<TokenResponseDto>.Unauthorized();
        }

        if (!payor.IsActive)
            return Result<TokenResponseDto>.Forbidden();

        payor.RecordLogin();
        // CreateTokenResponse persists the reset login state together with the new refresh token.
        return Result<TokenResponseDto>.Success(await tokenService.CreateTokenResponse(payor));
    }
}
