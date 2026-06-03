using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Auth.Logout;

public class LogoutCommandHandler(ITokenService tokenService) : IRequestHandler<LogoutCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        await tokenService.RevokeRefreshTokenAsync(request.RefreshToken, cancellationToken);
        return Result<bool>.Success(true);
    }
}
