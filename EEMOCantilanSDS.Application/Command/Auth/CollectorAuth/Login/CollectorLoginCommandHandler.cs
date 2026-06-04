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
    ITokenService tokenService,
    IUnitOfWork unitOfWork) : IRequestHandler<CollectorLoginCommand, Result<TokenResponseDto>>
{
    public async Task<Result<TokenResponseDto>> Handle(CollectorLoginCommand request, CancellationToken cancellationToken)
    {
        var collector = await collectorRepository.GetByUsernameOrEmployeeIdAsync(
            request.UsernameOrEmployeeId!, cancellationToken);

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

        collector.RecordLogin();
        return Result<TokenResponseDto>.Success(await tokenService.CreateTokenResponse(collector));
    }
}
