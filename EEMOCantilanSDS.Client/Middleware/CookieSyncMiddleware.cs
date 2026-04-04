using EEMOCantilanSDS.Infrastructure.Securities;

namespace EEMOCantilanSDS.Client.Middleware;

public class AuthCookieMiddleware(RequestDelegate next)
{
    private const string AccessTokenKey = "AccessToken";
    private const string RefreshTokenKey = "RefreshToken";

    public async Task InvokeAsync(HttpContext context)
    {
        await next(context);

        if (context.Items.TryGetValue("SetAuthCookies", out var tokens) && tokens is (string accessToken, string refreshToken))
        {
            context.Response.Cookies.Append(AccessTokenKey, accessToken, CookieOptionsBuilder.BuildAccessTokenOptions());
            context.Response.Cookies.Append(RefreshTokenKey, refreshToken, CookieOptionsBuilder.BuildRefreshTokenOptions());
        }

        if (context.Items.ContainsKey("ClearAuthCookies"))
        {
            context.Response.Cookies.Delete(AccessTokenKey);
            context.Response.Cookies.Delete(RefreshTokenKey);
        }
    }
}
