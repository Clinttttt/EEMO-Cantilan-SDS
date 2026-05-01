using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Infrastructure.Services
{
    public class CurrentUserService(IHttpContextAccessor accessor) : ICurrentUserService
    {
        public bool IsAuthenticated =>
      accessor.HttpContext?.User?.Identity?.IsAuthenticated == true;
        public AdminUserDto? GetCurrentUser() =>
       IsAuthenticated ? MapToDto(accessor.HttpContext!.User) : null;
        private static AdminUserDto? MapToDto(ClaimsPrincipal principal)
        {
            var userId = principal.FindFirstValue(AppClaimTypes.UserId);
            var fullName = principal.FindFirstValue(AppClaimTypes.FullName);
            var username = principal.FindFirstValue(AppClaimTypes.Username);
            var email = principal.FindFirstValue(AppClaimTypes.Email);
            var roleClaim = principal.FindFirstValue(AppClaimTypes.Role);
            var isActiveClaim = principal.FindFirstValue(AppClaimTypes.IsActive);
            var mustChangeClaim = principal.FindFirstValue(AppClaimTypes.MustChangePassword);


            if (userId is null || fullName is null || username is null ||
                email is null || roleClaim is null || isActiveClaim is null ||
                mustChangeClaim is null)
                return null;

            if (!Enum.TryParse<AdminRole>(roleClaim, out var adminRole))
                return null;

            if (!bool.TryParse(isActiveClaim, out var isActive))
                return null;

            if (!bool.TryParse(mustChangeClaim, out var mustChangePassword))
                return null;

            return new AdminUserDto
            {
                UserId = userId,
                FullName = fullName,
                UserName = username,
                Email = email,
                AdminRole = adminRole,
                IsActive = isActive,
                MustChangePassword = mustChangePassword,
            };
        }

    }
}
