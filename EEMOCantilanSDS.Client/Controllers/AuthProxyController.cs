using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.Login;
using EEMOCantilanSDS.Application.Command.Auth.GenerateRefreshToken;
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

        logger.LogInformation("Login successful, setting cookies for user: {Username}", request.Username);
        
        // Parse JWT to extract user claims
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(result.Value.AccessToken);
        
        var claims = new List<Claim>
        {
            new Claim("AccessToken", result.Value.AccessToken!),
            new Claim("RefreshToken", result.Value.RefreshToken!)
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
        
        logger.LogInformation("Cookies set successfully via SignInAsync");
        
        return Ok();
    } 

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        var result = await apiAuthService.RefreshTokenAsync(new RefreshTokenCommand { RefreshToken = request.RefreshToken });
        
        if (!result.IsSuccess || result.Value == null)
            return Unauthorized();

        // Parse JWT to extract user claims
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(result.Value.AccessToken);
        
        var claims = new List<Claim>
        {
            new Claim("AccessToken", result.Value.AccessToken!),
            new Claim("RefreshToken", result.Value.RefreshToken!)
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
