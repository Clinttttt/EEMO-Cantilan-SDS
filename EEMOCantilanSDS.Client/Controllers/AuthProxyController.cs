using Microsoft.AspNetCore.Authorization;
using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.ChangeMyPassword;
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

            // A temporarily locked account is told so — the API only answers 423 once the password itself was
            // correct, so relaying this cannot help anyone discover which usernames exist. Everything else
            // stays a blank 401.
            if (result.Status == ResultStatus.Locked && !string.IsNullOrWhiteSpace(result.Error))
                return StatusCode(423, new { error = result.Error });

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
    /// The signed-in administrator replaces their own password and the cookie session is rebuilt from the new tokens.
    ///
    /// <para>
    /// The rebuild is the point. The requirement to change travels as a claim inside the cookie, so without re-signing in
    /// the user would change their password and still be told to change it — the portal reads the old claim, and the API
    /// keeps refusing on the old access token.
    /// </para>
    /// </summary>
    [HttpPost("change-my-password")]
    [Authorize]
    public async Task<IActionResult> ChangeMyPassword([FromBody] ChangeMyPasswordCommand request)
    {
        var result = await apiAuthService.ChangeMyPasswordAsync(request);

        if (!result.IsSuccess || result.Value is null)
        {
            // The API's message is specific and safe to relay here: the caller is already authenticated as this account, so
            // "your current password is incorrect" reveals nothing they do not know.
            logger.LogWarning("Password change refused for the signed-in administrator");
            return StatusCode(result.StatusCode ?? 400, new { error = result.Error ?? "We could not change your password." });
        }

        await SignInWithTokensAsync(result.Value.AccessToken, result.Value.RefreshToken);
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

        RememberMunicipality(jwtToken.Claims);
    }

    /// <summary>
    /// Remembers which LGU last signed in on THIS BROWSER, so a later visit to a bare /login shows that
    /// municipality's own seal, name and office instead of the default LGU's.
    ///
    /// <para>
    /// Branding only, and never authorisation: every request is still scoped by the caller's own token, and the login
    /// itself still checks the account against whichever LGU it is signing into. The worst a tampered value can do is
    /// paint the wrong crest on a sign-in page.
    /// </para>
    ///
    /// <para>
    /// Not HttpOnly-sensitive and deliberately readable at prerender, which is where the login panel is composed. It is
    /// what makes a bookmarked /login, or the morning after, land on the office's own identity - the case a link
    /// carrying ?lgu= cannot cover.
    /// </para>
    /// </summary>
    private void RememberMunicipality(IEnumerable<Claim> tokenClaims)
    {
        var code = tokenClaims.FirstOrDefault(c => c.Type == AppClaimTypes.Municipality)?.Value;
        if (string.IsNullOrWhiteSpace(code)) return;

        Response.Cookies.Append(LastMunicipalityCookie, code, new CookieOptions
        {
            HttpOnly = false,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            Expires = DateTimeOffset.UtcNow.AddDays(365),
            Path = "/"
        });
    }

    /// <summary>The browser's memory of the last LGU signed in here. Read by the login page when no ?lgu is supplied.</summary>
    public const string LastMunicipalityCookie = "stalltrack_lgu";

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
