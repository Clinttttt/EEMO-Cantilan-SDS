using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Infrastructure.Services
{
    public class TokenService(IConfiguration configuration, IUnitOfWork unitOfWork, AppDbContext context) : ITokenService
    {
        public string CreateToken(BaseUser user, string role)
        {
            var claim = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.FullName ?? string.Empty),
                new Claim(ClaimTypes.Email, user.Email ?? string.Empty),
                new Claim(ClaimTypes.Role, role)
            };
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Key"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha512);
            var token = new JwtSecurityToken
            (
                issuer: configuration["Jwt:Issuer"],
                audience: configuration["Jwt:Audience"],
                expires: DateTime.Now.AddDays(7),
                claims: claim,
                signingCredentials: creds
            );
            return new JwtSecurityTokenHandler().WriteToken(token);
        }
        public string GenerateRefreshToken()
        {
            var randomNumber = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomNumber);
            return Convert.ToBase64String(randomNumber);
        }
        public async Task<string> GenerateAndSaveRefreshToken(BaseUser user, CancellationToken cancellationToken = default)
        {
            var refreshToken = GenerateRefreshToken();
            user.SetRefreshToken(refreshToken, DateTime.UtcNow.AddDays(7));
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return refreshToken;
        }
        public async Task<TokenResponseDto> CreateTokenResponse(BaseUser user)
        {
            return new TokenResponseDto
            {
                AccessToken = CreateToken(user, GetRole(user)),
                RefreshToken = await GenerateAndSaveRefreshToken(user)
            };
        }
        public string GetRole(BaseUser user)
        {
            return user switch
            {
                AdminUser admin => admin.Role.ToString(),
                CollectorUser collector => "Collector",
                _ => throw new InvalidOperationException("Unknown user type")
            };
        }
        public async Task<BaseUser> ValidateRefreshToken(string RefreshToken, CancellationToken cancellationToken = default)
        {
            var user = await context.AdminUsers.FirstOrDefaultAsync(s => s.RefreshToken == RefreshToken, cancellationToken);
            if (user?.RefreshToken != RefreshToken || user.RefreshTokenExpiryTime <= DateTime.UtcNow)
            {
                return null!;
            }
            return user;
        }

    }
}
