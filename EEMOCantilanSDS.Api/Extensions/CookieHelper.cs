using EEMOCantilanSDS.Domain.Common;
using Microsoft.AspNetCore.Http;

namespace EEMOCantilanSDS.Api.Extensions
{
    public static class CookieHelper
    {
        private const string AccessTokenCookie = "accessToken";
        private const string RefreshTokenCookie = "refreshToken";

        public static void SetAuthCookies(HttpResponse response, string accessToken, string refreshToken)
        {
          
           if (!string.IsNullOrEmpty(accessToken) && !string.IsNullOrEmpty(refreshToken))
           {
            response.Cookies.Append(
                AccessTokenCookie,
                accessToken,
                new CookieOptions
                {
                    HttpOnly = true, 
                    Secure = true,
                    SameSite = SameSiteMode.Strict,
                    Expires = DateTimeOffset.UtcNow.AddMinutes(15)
                }
            );
            response.Cookies.Append(
                RefreshTokenCookie,
                refreshToken,
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Strict,
                    Expires = DateTimeOffset.UtcNow.AddDays(7)
                }
            );
        }
        }

        public static void ClearAuthCookies(HttpResponse response)
        {
            response.Cookies.Delete(AccessTokenCookie);
            response.Cookies.Delete(RefreshTokenCookie);
        }

        public static Result<string>? GetRefreshTokenFromCookie(HttpRequest request)
        {
           if(!request.Cookies.TryGetValue(RefreshTokenCookie, out var token))
            {
                return Result<string>.Unauthorized();
            }
            return Result<string>.Success(token);
        }
    }
}
