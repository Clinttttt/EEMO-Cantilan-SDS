using EEMOCantilanSDS.Api.Extensions;
using EEMOCantilanSDS.Application.Command.Auth.GenerateRefreshToken;
using EEMOCantilanSDS.Application.Command.Auth.Logout;
using EEMOCantilanSDS.Application.Command.Auth.PayorAuth.Activate;
using EEMOCantilanSDS.Application.Command.Auth.PayorAuth.Login;
using EEMOCantilanSDS.Application.Dtos;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EEMOCantilanSDS.Api.Controllers;

/// <summary>
/// A payor's own sign-in.
///
/// <para>
/// Every successful step also writes the auth cookies, exactly as the operator console's endpoints do: HttpOnly, so
/// no script on any origin can read them, Secure, and SameSite=Strict, which is satisfied because every StallTrack
/// site is a subdomain of one registrable domain. That is what lets the payor portal at payor.stalltrack.site hold
/// no token of its own. The tokens stay in the response body as well, unchanged, because the Blazor portal reads
/// them from there and a client that already works must keep working.
/// </para>
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class PayorAuthController(ISender sender) : ApiBaseController(sender)
{
    [HttpPost("activate")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<TokenResponseDto>> ActivateAsync([FromBody] ActivatePayorAccountCommand request)
    {
        var result = await Sender.Send(request);

        if (result.IsSuccess)
            CookieHelper.SetAuthCookies(Response, result.Value!.AccessToken, result.Value.RefreshToken);
        return HandleResponse(result);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<TokenResponseDto>> LoginAsync([FromBody] PayorLoginCommand request)
    {
        var result = await Sender.Send(request);

        if (result.IsSuccess)
            CookieHelper.SetAuthCookies(Response, result.Value!.AccessToken, result.Value.RefreshToken);
        return HandleResponse(result);
    }

    /// <summary>
    /// Renews the pair. The refresh token is taken from the request body when a client sends one, and otherwise from
    /// the cookie, so a portal that holds nothing can still refresh and the Blazor portal keeps its own behaviour.
    /// </summary>
    [HttpPost("refresh-token")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<TokenResponseDto>> RefreshAsync([FromBody] RefreshTokenCommand? request)
    {
        var refreshToken = string.IsNullOrWhiteSpace(request?.RefreshToken)
            ? CookieHelper.GetRefreshTokenFromCookie(Request)?.Value
            : request!.RefreshToken;

        if (string.IsNullOrWhiteSpace(refreshToken))
            return HandleResponse(Result<TokenResponseDto>.Unauthorized());

        var result = await Sender.Send(new RefreshTokenCommand { RefreshToken = refreshToken! });

        if (result.IsSuccess)
            CookieHelper.SetAuthCookies(Response, result.Value!.AccessToken, result.Value.RefreshToken);
        return HandleResponse(result);
    }

    /// <summary>
    /// Ends the session at the server and clears the cookies. The token is revoked whether the caller sent it or
    /// holds it only as a cookie, so signing out is never merely local.
    /// </summary>
    [HttpPost("logout")]
    [Authorize(Roles = "Payor")]
    public async Task<ActionResult<bool>> LogoutAsync([FromBody] RefreshTokenCommand? request)
    {
        var refreshToken = string.IsNullOrWhiteSpace(request?.RefreshToken)
            ? CookieHelper.GetRefreshTokenFromCookie(Request)?.Value
            : request!.RefreshToken;

        var result = await Sender.Send(new LogoutCommand { RefreshToken = refreshToken ?? string.Empty });

        CookieHelper.ClearAuthCookies(Response);
        return HandleResponse(result);
    }
}
