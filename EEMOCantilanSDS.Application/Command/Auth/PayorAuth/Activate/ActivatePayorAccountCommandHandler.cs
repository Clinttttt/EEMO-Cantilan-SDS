using EEMOCantilanSDS.Application.Common.Interface.Security;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Auth.PayorAuth.Activate;

public class ActivatePayorAccountCommandHandler(
    IPayorRepository payorRepository,
    IMunicipalityRepository municipalityRepository,
    IRequestTenantScope tenantScope,
    ITokenService tokenService,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher) : IRequestHandler<ActivatePayorAccountCommand, Result<TokenResponseDto>>
{
    public async Task<Result<TokenResponseDto>> Handle(ActivatePayorAccountCommand request, CancellationToken cancellationToken)
    {
        var contactNumber = request.ContactNumber!.Trim();

        var code = await payorRepository.GetActivationCodeAsync(request.ActivationCode!.Trim(), cancellationToken);

        // Validate the code without revealing which specific check failed (anti-enumeration).
        if (code is null || !code.CanBeRedeemedBy(contactNumber))
            return Result<TokenResponseDto>.Failure("Invalid or expired activation code.", ResultStatus.Invalid);

        // Activation is anonymous, so this request would otherwise resolve to the DEFAULT tenant (Cantilan).
        // Pin it to the code's OWN municipality so the new payor account + stall link are stamped (and
        // tenant-scoped) under the correct LGU. For a Cantilan code this resolves to Cantilan — unchanged.
        var municipality = await municipalityRepository.GetByIdAsync(code.MunicipalityId, cancellationToken);
        if (municipality is not null)
            tenantScope.Use(municipality.Id, municipality.TenantCode);

        // One mobile number = one payor (enforced at code generation). If an account already exists for
        // this number, the payor has already activated — direct them to sign in. Never link the code's
        // stall onto the existing account here: a code only proves stall ownership, not that the same
        // PERSON owns the account, so auto-linking would merge two unrelated payors.
        var existing = await payorRepository.GetByContactNumberAsync(contactNumber, cancellationToken);
        if (existing is not null)
            return Result<TokenResponseDto>.Failure(
                "This mobile number is already activated. Please sign in instead.", ResultStatus.Conflict);

        // The name is the office's own, not the payor's typing. It was asked for on the activation form and proved nothing:
        // the code plus the registered number are the whole proof of ownership, and the name was only ever the greeting on
        // the portal. Asking for it invited a mismatch with the register (a payor typed "Godon Larl" for the office's
        // "Godon Lar"), so it is read from the active contract for the stall the code was issued for. A typed name is still
        // accepted as a fallback for a space the office holds no occupant name for, and failing that the number they signed
        // up with, so activation can never be blocked by a gap in the register.
        var occupantName = await payorRepository.GetOccupantNameAsync(code.StallId, code.MunicipalityId, cancellationToken);
        var fullName = occupantName
                       ?? (string.IsNullOrWhiteSpace(request.FullName) ? contactNumber : request.FullName.Trim());

        var payor = PayorUser.Create(fullName, contactNumber, passwordHasher.Hash(request.Password!));
        await payorRepository.AddPayorAsync(payor, cancellationToken);

        await payorRepository.AddStallLinkAsync(PayorStallLink.Create(payor.Id, code.StallId), cancellationToken);
        code.MarkUsed(payor.Id);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<TokenResponseDto>.Success(await tokenService.CreateTokenResponse(payor));
    }
}
