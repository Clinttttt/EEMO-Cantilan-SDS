using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Application.Command.Auth.AdminAuth.Login
{
    public class LoginCommandHandler(ITokenService tokenService, IAppDbContext context) : IRequestHandler<LoginCommand, Result<TokenResponseDto>>
    {
        public async Task<Result<TokenResponseDto>> Handle(LoginCommand request, CancellationToken cancellationToken)
        {
            var user = await context.AdminUsers.FirstOrDefaultAsync(s => s.Username == request.Username);
            if (user is null) return Result<TokenResponseDto>.NotFound();

            if(new PasswordHasher<BaseUser>().VerifyHashedPassword(user, user.PasswordHash,request.Password) 
                == PasswordVerificationResult.Failed)
            {
                return Result<TokenResponseDto>.Unauthorized(); 
            }
            return Result<TokenResponseDto>.Success(await tokenService.CreateTokenResponse(user));
        }
    }
}
