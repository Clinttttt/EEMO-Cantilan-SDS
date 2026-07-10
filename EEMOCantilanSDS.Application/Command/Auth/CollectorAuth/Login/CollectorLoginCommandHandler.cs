using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace EEMOCantilanSDS.Application.Command.Auth.CollectorAuth.Login;

public class CollectorLoginCommandHandler(
    ICollectorRepository collectorRepository,
    IMunicipalityRepository municipalityRepository,
    ITokenService tokenService,
    IUnitOfWork unitOfWork) : IRequestHandler<CollectorLoginCommand, Result<TokenResponseDto>>
{
    public async Task<Result<TokenResponseDto>> Handle(CollectorLoginCommand request, CancellationToken cancellationToken)
    {
        // Resolve the target tenant up-front when the collector specified which LGU they are signing into.
        // A username/employee-id shared across LGUs otherwise resolves to an arbitrary tenant's account.
        // When no code is supplied the lookup stays global — existing (Cantilan) clients are unchanged.
        Guid? scopeMunicipalityId = null;
        if (!string.IsNullOrWhiteSpace(request.MunicipalityCode))
        {
            var municipality = await municipalityRepository.GetByIdentifierAsync(request.MunicipalityCode, cancellationToken);
            if (municipality is null) return Result<TokenResponseDto>.Forbidden();
            scopeMunicipalityId = municipality.Id;
        }

        var collector = scopeMunicipalityId is { } mid
            ? await collectorRepository.GetByUsernameOrEmployeeIdAsync(request.UsernameOrEmployeeId!, mid, cancellationToken)
            : await collectorRepository.GetByUsernameOrEmployeeIdAsync(request.UsernameOrEmployeeId!, cancellationToken);

        if (collector is null)
            return Result<TokenResponseDto>.NotFound();

        if (collector.IsLockedOut)
            return Result<TokenResponseDto>.Unauthorized();

        var verification = new PasswordHasher<BaseUser>().VerifyHashedPassword(
            collector,
            collector.PasswordHash,
            request.Password!);

        if (verification == PasswordVerificationResult.Failed)
        {
            collector.RecordFailedLogin();
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<TokenResponseDto>.Unauthorized();
        }

        if (!collector.IsActive)
            return Result<TokenResponseDto>.Forbidden();

        // Defense-in-depth: the account must belong to the requested LGU. With the scoped lookup this always
        // holds; retained so no future non-scoped path can slip through. Checked AFTER the password.
        if (scopeMunicipalityId is { } boundaryId && boundaryId != collector.MunicipalityId)
            return Result<TokenResponseDto>.Forbidden();

        collector.RecordLogin();
        return Result<TokenResponseDto>.Success(await tokenService.CreateTokenResponse(collector));
    }
}
