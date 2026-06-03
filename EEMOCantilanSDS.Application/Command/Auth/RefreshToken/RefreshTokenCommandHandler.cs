using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Application.Command.Auth.GenerateRefreshToken
{
    public class RefreshTokenCommandHandler(ITokenService tokenService) : IRequestHandler<RefreshTokenCommand, Result<TokenResponseDto>>
    {
        public async Task<Result<TokenResponseDto>> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
        {
            var user = await tokenService.ValidateRefreshToken(request.RefreshToken, cancellationToken);
            if (user is null) return Result<TokenResponseDto>.Failure("Invalid refresh token");

            // No rotation: keep the existing refresh token (only the access token is renewed).
            return Result<TokenResponseDto>.Success(new TokenResponseDto
            {
                AccessToken = tokenService.CreateAccessToken(user),
                RefreshToken = request.RefreshToken
            });
        }
    }
}
