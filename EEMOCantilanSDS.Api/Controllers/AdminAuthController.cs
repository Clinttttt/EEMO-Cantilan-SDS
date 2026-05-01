using EEMOCantilanSDS.Api.Extensions;
using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.Login;
using EEMOCantilanSDS.Application.Command.Auth.GenerateRefreshToken;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace EEMOCantilanSDS.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
[IgnoreAntiforgeryToken]
public class AdminAuthController(ISender sender) : ApiBaseController(sender)
{
    [HttpPost("login")]
    public async Task<ActionResult<TokenResponseDto>> LoginAsync([FromBody] LoginCommand request)
    {
        var result = await Sender.Send(request);

        if (result.IsSuccess)   
            CookieHelper.SetAuthCookies(Response, result.Value!.AccessToken, result.Value.RefreshToken);
        
        return HandleResponse(result);
    }

    [HttpPost("refresh-token")]
    public async Task<ActionResult<TokenResponseDto>> RefreshAsync()
    {
        var refreshToken = CookieHelper.GetRefreshTokenFromCookie(Request);

        var command = new RefreshTokenCommand { RefreshToken = refreshToken?.Value! };
        var result = await Sender.Send(command);

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
   

    [HttpPost("logout")]
    [Authorize]
    public ActionResult Logout()
    {
        CookieHelper.ClearAuthCookies(Response);
        return Ok(new { message = "Logged out successfully" });
    }
}
