using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.Login;
using EEMOCantilanSDS.Application.Command.Auth.GenerateRefreshToken;
using EEMOCantilanSDS.Application.Command.Auth.Mfa;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Client.Securities;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace EEMOCantilanSDS.Client.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthProxyController(IAuthApiClient apiAuthService, ILogger<AuthProxyController> logger) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginCommand request)
    {
        logger.LogInformation("AuthProxyController.Login called for user: {Username}", request.Username);
        
        var result = await apiAuthService.LoginAsync(request);
        
        if (!result.IsSuccess || result.Value == null)
        {
            logger.LogWarning("Login failed for user: {Username}", request.Username);
            return Unauthorized();
        }

        // Two-factor pending: the password was correct but NO tokens were issued, so no auth cookie may be
        // written here. Relay the challenge so the UI can ask for the authenticator code.
        if (result.Value.MfaRequired)
        {
            logger.LogInformation("Login requires two-factor for user: {Username}", request.Username);
            return Ok(new { mfaRequired = true, challengeToken = result.Value.MfaChallengeToken });
        }

        logger.LogInformation("Login successful, setting cookies for user: {Username}", request.Username);

        await SignInWithTokensAsync(result.Value.AccessToken, result.Value.RefreshToken);

        logger.LogInformation("Cookies set successfully via SignInAsync");
        
        return Ok();
    }

    /// <summary>
    /// Second step of a two-factor sign-in: exchanges the challenge + authenticator code for a session and
    /// writes the auth cookie. Anonymous by nature — the challenge is the credential.
    /// </summary>
    [HttpPost("mfa/verify-login")]
    public async Task<IActionResult> VerifyMfaLogin([FromBody] VerifyMfaLoginCommand request)
    {
        var result = await apiAuthService.VerifyMfaLoginAsync(request);

        if (!result.IsSuccess || result.Value == null)
        {
            logger.LogWarning("Two-factor verification failed");
            // Surface the API's message (generic by design) so the user knows to retry or sign in again.
            return BadRequest(new { error = result.Error ?? "That code is not valid." });
        }

        await SignInWithTokensAsync(result.Value.AccessToken, result.Value.RefreshToken);
        return Ok();
    }

    /// <summary>
    /// Builds the cookie principal from the issued tokens. Shared by the password-only login, the two-factor
    /// verification and the refresh path so the claim/sign-in logic exists once.
    /// </summary>
    private async Task SignInWithTokensAsync(string accessToken, string refreshToken)
    {
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(accessToken);

        var claims = new List<Claim>
        {
            new Claim("AccessToken", accessToken),
            new Claim("RefreshToken", refreshToken)
        };

        // Add all claims from JWT (including role)
        foreach (var claim in jwtToken.Claims)
        {
            claims.Add(claim);
        }

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(claimsIdentity),
            authProperties);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        var result = await apiAuthService.RefreshTokenAsync(new RefreshTokenCommand { RefreshToken = request.RefreshToken });
        
        if (!result.IsSuccess || result.Value == null)
            return Unauthorized();

        await SignInWithTokensAsync(result.Value.AccessToken, result.Value.RefreshToken);

        return Ok(result.Value.AccessToken);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var refreshToken = User.FindFirst("RefreshToken")?.Value;
        if (!string.IsNullOrWhiteSpace(refreshToken))
            await apiAuthService.LogoutAsync(refreshToken);

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok();
    }
}

public record RefreshRequest(string RefreshToken);
