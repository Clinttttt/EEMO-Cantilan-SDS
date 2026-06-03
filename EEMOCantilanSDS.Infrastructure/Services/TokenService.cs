using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
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
                new(AppClaimTypes.UserId, user.Id.ToString()),
                new(AppClaimTypes.FullName, user.FullName ?? string.Empty),
                new(AppClaimTypes.Username, user.Username ?? string.Empty),
                new(AppClaimTypes.Email, user.Email  ?? string.Empty),
                new(AppClaimTypes.Role, role),
                new(AppClaimTypes.IsActive, user.IsActive.ToString()),
                new(AppClaimTypes.MustChangePassword, user.MustChangePassword.ToString()),
            };
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Key"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha512);
            var token = new JwtSecurityToken
            (
                issuer: configuration["Jwt:Issuer"],
                audience: configuration["Jwt:Audience"],
                expires: DateTime.UtcNow.AddMinutes(15),
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
            user.SetRefreshToken(HashRefreshToken(refreshToken), DateTime.UtcNow.AddDays(7));
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

        public string CreateAccessToken(BaseUser user) => CreateToken(user, GetRole(user));

        public async Task<BaseUser> ValidateRefreshToken(string RefreshToken, CancellationToken cancellationToken = default)
        {
            var hashed = HashRefreshToken(RefreshToken);
            var user = await context.Users.FirstOrDefaultAsync(
                s => s.RefreshToken == hashed && !s.IsDeleted && s.IsActive, cancellationToken);
            if (user is null
                || user.RefreshTokenExpiryTime <= DateTime.UtcNow
                || (user.LockedUntil.HasValue && user.LockedUntil > DateTime.UtcNow))
            {
                return null!;
            }
            return user;
        }

        public async Task RevokeRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(refreshToken)) return;
            var hashed = HashRefreshToken(refreshToken);
            var user = await context.Users.FirstOrDefaultAsync(s => s.RefreshToken == hashed, cancellationToken);
            if (user is null) return;
            user.ClearRefreshToken();
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        private static string HashRefreshToken(string token) =>
            Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
