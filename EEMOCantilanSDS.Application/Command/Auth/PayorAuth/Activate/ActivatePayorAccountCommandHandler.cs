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

        // v1: one payor account per contact number. If it already exists, the payor should log in
        // (linking additional stalls to an existing account is future work).
        var existing = await payorRepository.GetByContactNumberAsync(contactNumber, cancellationToken);
        if (existing is not null)
            return Result<TokenResponseDto>.Conflict();

        var payor = PayorUser.Create(request.FullName!.Trim(), contactNumber, request.Password!);
        await payorRepository.AddPayorAsync(payor, cancellationToken);

        await payorRepository.AddStallLinkAsync(PayorStallLink.Create(payor.Id, code.StallId), cancellationToken);
        code.MarkUsed(payor.Id);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<TokenResponseDto>.Success(await tokenService.CreateTokenResponse(payor));
    }
}
