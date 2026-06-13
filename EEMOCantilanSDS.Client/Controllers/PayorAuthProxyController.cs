using EEMOCantilanSDS.Application.Command.Auth.PayorAuth.Activate;
using EEMOCantilanSDS.Application.Command.Auth.PayorAuth.Login;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Client.Securities;
using EEMOCantilanSDS.Domain.Common;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace EEMOCantilanSDS.Client.Controllers;

/// <summary>
/// Server-side proxy that exchanges payor credentials for tokens (via the API) and persists them
/// in the Blazor auth cookie — mirroring <c>AuthProxyController</c> for admins.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class PayorAuthProxyController(IPayorAuthApiClient payorAuth, ILogger<PayorAuthProxyController> logger) : ControllerBase
{
    [HttpPost("activate")]
    public async Task<IActionResult> Activate([FromBody] ActivatePayorAccountCommand request)
    {
        var result = await payorAuth.ActivateAsync(request);
        return await SignInFromResult(result);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] PayorLoginCommand request)
    {
        var result = await payorAuth.LoginAsync(request);
        return await SignInFromResult(result);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var refreshToken = User.FindFirst("RefreshToken")?.Value;
        if (!string.IsNullOrWhiteSpace(refreshToken))
            await payorAuth.LogoutAsync(refreshToken);

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok();
    }

    private async Task<IActionResult> SignInFromResult(Result<TokenResponseDto> result)
    {
        if (!result.IsSuccess || result.Value is null)
        {
            logger.LogWarning("Payor auth failed (status {Status}).", result.StatusCode);
            // Return the error message so the Blazor page can surface the exact text to the user.
            return StatusCode(result.StatusCode ?? 401, new { error = result.Error });
        }

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Value.AccessToken);
        var claims = new List<Claim>
        {
            new("AccessToken", result.Value.AccessToken!),
            new("RefreshToken", result.Value.RefreshToken!)
        };
        claims.AddRange(jwt.Claims);

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            authProperties);

        return Ok();
    }
}
