using EEMOCantilanSDS.Api.Extensions;
using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.Login;
using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.RequestPasswordReset;
using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.ResetPasswordByToken;
using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.VerifyEmail;
using EEMOCantilanSDS.Application.Command.Auth.Mfa;
using EEMOCantilanSDS.Application.Queries.Auth.GetMfaStatus;
using EEMOCantilanSDS.Application.Queries.Auth.GetPasswordResetContext;
using EEMOCantilanSDS.Application.Dtos.Auth;
using EEMOCantilanSDS.Application.Command.Auth.GenerateRefreshToken;
using EEMOCantilanSDS.Application.Command.Auth.Logout;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;

namespace EEMOCantilanSDS.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
[IgnoreAntiforgeryToken]
public class AdminAuthController(ISender sender) : ApiBaseController(sender)
{
    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<TokenResponseDto>> LoginAsync([FromBody] LoginCommand request)
    {
        var result = await Sender.Send(request);

        if (result.IsSuccess)   
            CookieHelper.SetAuthCookies(Response, result.Value!.AccessToken, result.Value.RefreshToken);
        
        return HandleResponse(result);
    }

    [HttpPost("refresh-token")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<TokenResponseDto>> RefreshAsync([FromBody] RefreshTokenCommand request)
    {
        var refreshToken = string.IsNullOrWhiteSpace(request?.RefreshToken)
            ? CookieHelper.GetRefreshTokenFromCookie(Request)?.Value
            : request.RefreshToken;

        var result = await Sender.Send(new RefreshTokenCommand { RefreshToken = refreshToken! });

        if (result.IsSuccess)
            CookieHelper.SetAuthCookies(Response, result.Value!.AccessToken, result.Value.RefreshToken);
        return HandleResponse(result);
    }

    [HttpGet("current-user")]
    [Authorize]
    public async Task<ActionResult<AdminUserDto>> GetCurrentUser()
    {
        var query = new GetCurrentUserQuery();
        var result = await Sender.Send(query);
        return HandleResponse(result);
    }

    /// <summary>
    /// Starts a self-service password reset and emails a one-time link. Anonymous (the user cannot sign in)
    /// and rate-limited. Always returns success with a neutral message whether or not an account matched, so
    /// the endpoint can never be used to discover which usernames or emails exist.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("forgot-password")]
    public async Task<ActionResult<bool>> ForgotPasswordAsync([FromBody] RequestPasswordResetCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }

    /// <summary>
    /// Completes a self-service password reset using the one-time token from the emailed link. Anonymous
    /// (the token is the credential) and rate-limited; any invalid or expired token returns one generic error.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("reset-password")]
    public async Task<ActionResult<bool>> ResetPasswordAsync([FromBody] ResetPasswordByTokenCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }

    /// <summary>
    /// Resolves which account a password-reset token belongs to, so the reset page can state the username,
    /// office and municipality before a new password is set (one mailbox can hold links for several LGUs).
    /// Anonymous (the token is the credential) and rate-limited; generic failure for any bad token.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpGet("reset-context/{token}")]
    public async Task<ActionResult<TokenAccountContextDto>> GetResetContextAsync(string token)
    {
        var result = await Sender.Send(new GetPasswordResetContextQuery(token));
        return HandleResponse(result);
    }

    /// <summary>
    /// Confirms an email address via its one-time link. Anonymous (the token is the credential) and
    /// rate-limited. Confirming only marks the address verified — it grants no session or other capability.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("verify-email")]
    public async Task<ActionResult<VerifiedAccountDto>> VerifyEmailAsync([FromBody] VerifyEmailCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }
   

    /// <summary>
    /// Two-factor authentication for the SIGNED-IN user's own account. Enrollment is opt-in and does not yet
    /// gate sign-in (that arrives in the login slice), so these endpoints are safe to expose now.
    /// Every one of them re-authenticates with the current password.
    /// </summary>
    [HttpGet("mfa/status")]
    [Authorize]
    public async Task<ActionResult<MfaStatusDto>> GetMfaStatusAsync()
        => HandleResponse(await Sender.Send(new GetMfaStatusQuery()));

    [HttpPost("mfa/enroll")]
    [Authorize]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<MfaEnrollmentDto>> BeginMfaEnrollmentAsync([FromBody] BeginMfaEnrollmentCommand command)
        => HandleResponse(await Sender.Send(command));

    [HttpPost("mfa/confirm")]
    [Authorize]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<MfaRecoveryCodesDto>> ConfirmMfaEnrollmentAsync([FromBody] ConfirmMfaEnrollmentCommand command)
        => HandleResponse(await Sender.Send(command));

    [HttpPost("mfa/disable")]
    [Authorize]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<bool>> DisableMfaAsync([FromBody] DisableMfaCommand command)
        => HandleResponse(await Sender.Send(command));

    [HttpPost("mfa/recovery-codes")]
    [Authorize]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<MfaRecoveryCodesDto>> RegenerateRecoveryCodesAsync([FromBody] RegenerateRecoveryCodesCommand command)
        => HandleResponse(await Sender.Send(command));

    [HttpPost("logout")]
    public async Task<ActionResult> Logout([FromBody] RefreshTokenCommand request)
    {
        var refreshToken = string.IsNullOrWhiteSpace(request?.RefreshToken)
            ? CookieHelper.GetRefreshTokenFromCookie(Request)?.Value
            : request.RefreshToken;

        if (!string.IsNullOrWhiteSpace(refreshToken))
            await Sender.Send(new LogoutCommand { RefreshToken = refreshToken });

        CookieHelper.ClearAuthCookies(Response);
        return Ok(new { message = "Logged out successfully" });
    }
}
