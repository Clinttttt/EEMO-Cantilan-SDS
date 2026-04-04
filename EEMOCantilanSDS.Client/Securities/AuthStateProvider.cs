
using EEMOCantilanSDS.Client.Utilities;
using Microsoft.AspNetCore.Components.Authorization;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Client.Securities
{
    
        public class AuthStateProvider : AuthenticationStateProvider
        {
            private readonly IHttpContextAccessor _httpContextAccessor;

            public AuthStateProvider(IHttpContextAccessor httpContextAccessor)
            {
                _httpContextAccessor = httpContextAccessor;

            }
            public override Task<AuthenticationState> GetAuthenticationStateAsync()
            {
                try
                {
                    var httpContext = _httpContextAccessor.HttpContext;


                    if (httpContext == null)
                        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));

                    if (!httpContext.Request.Cookies.TryGetValue("AccessToken", out var token) ||
                        string.IsNullOrWhiteSpace(token))
                        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));

                    token = token.Trim().Trim('"').Trim();
                    var user = JwtParser.ParseToken(token);

                    return Task.FromResult(user == null
                        ? new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()))
                        : new AuthenticationState(user));
                }
                catch
                {
                    return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
                }
            }

            public async Task<string?> GetUserIdAsync()
            {
                var state = await GetAuthenticationStateAsync();
                return state.User.Identity?.IsAuthenticated == true
                    ? state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    : null;
            }

            public Task MarkUserAsAuthenticated()
            {
                NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
                return Task.CompletedTask;
            }

            public Task MarkUserAsLoggedOut()
            {
                NotifyAuthenticationStateChanged(Anonymous());
                return Task.CompletedTask;
            }

            private static Task<AuthenticationState> Anonymous() =>
                Task.FromResult(new AuthenticationState(
                    new ClaimsPrincipal(new ClaimsIdentity())));
        }
    }

