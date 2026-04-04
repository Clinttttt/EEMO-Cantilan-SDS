using Microsoft.AspNetCore.Http;

namespace EEMOCantilanSDS.Infrastructure.Securities;

public static class CookieOptionsBuilder
{
    public static CookieOptions BuildAccessTokenOptions()
    {
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddMinutes(15)
        };
    }

    public static CookieOptions BuildRefreshTokenOptions()
    {
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddDays(7)
        };
    }
}
