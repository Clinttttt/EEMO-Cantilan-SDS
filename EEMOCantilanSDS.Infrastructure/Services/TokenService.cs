using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Constants;
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
            // Cache/tenant namespace: carry the USER's municipality TenantCode so each LGU has a distinct
            // cache namespace (no cross-tenant collision). A Cantilan user resolves to "cantilan-sds"
            // (== TenantConstants.DefaultTenantCode), and an unresolved/empty municipality also falls back
            // to the default — so Cantilan's claim is byte-for-byte identical to before. Sync query is fine:
            // token creation is infrequent.
            var tenantCode = context.Municipalities
                .IgnoreQueryFilters()
                .Where(m => m.Id == user.MunicipalityId)
                .Select(m => m.TenantCode)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(tenantCode))
            {
                tenantCode = TenantConstants.DefaultTenantCode;
            }

            var claim = new List<Claim>
            {
                new(AppClaimTypes.UserId, user.Id.ToString()),
                new(AppClaimTypes.FullName, user.FullName ?? string.Empty),
                new(AppClaimTypes.Username, user.Username ?? string.Empty),
                new(AppClaimTypes.Email, user.Email  ?? string.Empty),
                new(AppClaimTypes.Role, role),
                new(AppClaimTypes.IsActive, user.IsActive.ToString()),
                new(AppClaimTypes.MustChangePassword, user.MustChangePassword.ToString()),
                // Tenant seam: the cache/tenant namespace is the user's municipality TenantCode. Cantilan
                // (and any token-less/unresolved flow) yields the default "cantilan-sds" — unchanged today.
                new(AppClaimTypes.Municipality, tenantCode),
                // Per-request tenant identity: the authenticated user's municipality id (Phase 5). The EF
                // global query filter and write-stamp resolve the current LGU from this claim; token-less
                // flows fall back to the default municipality (Cantilan).
                new(AppClaimTypes.MunicipalityId, user.MunicipalityId.ToString()),
            };
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Key"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha512);
            var token = new JwtSecurityToken
            (
                issuer: configuration["Jwt:Issuer"],
                audience: configuration["Jwt:Audience"],
                expires: DateTime.UtcNow.AddMinutes(DomainRules.AccessTokenMinutes),
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
            user.SetRefreshToken(HashRefreshToken(refreshToken), DateTime.UtcNow.AddDays(DomainRules.RefreshTokenDays));
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
                PayorUser payor => "Payor",
                _ => throw new InvalidOperationException("Unknown user type")
            };
        }

        public string CreateAccessToken(BaseUser user) => CreateToken(user, GetRole(user));

        public async Task<BaseUser> ValidateRefreshToken(string RefreshToken, CancellationToken cancellationToken = default)
        {
            var hashed = HashRefreshToken(RefreshToken);
            // The refresh request is unauthenticated (the access token has expired), so it carries no tenant
            // claim and resolves to the DEFAULT municipality. The refresh token is a globally-unique secret,
            // so bypass the per-tenant query filter (keep the soft-delete guard) — otherwise a non-default-LGU
            // user's refresh would never match and they'd be logged out the moment their access token expires.
            var user = await context.Users.IgnoreQueryFilters().FirstOrDefaultAsync(
                s => s.RefreshToken == hashed && s.IsActive && !s.IsDeleted, cancellationToken);
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
            // Same as ValidateRefreshToken: the token is a global secret and logout is unauthenticated, so
            // bypass the per-tenant filter to revoke a non-default-LGU user's token.
            var user = await context.Users.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.RefreshToken == hashed, cancellationToken);
            if (user is null) return;
            user.ClearRefreshToken();
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        private static string HashRefreshToken(string token) =>
            Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
