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
        return HandleResponse(result);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<TokenResponseDto>> LoginAsync([FromBody] PayorLoginCommand request)
    {
        var result = await Sender.Send(request);
        return HandleResponse(result);
    }

    [HttpPost("refresh-token")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<TokenResponseDto>> RefreshAsync([FromBody] RefreshTokenCommand request)
    {
        var result = await Sender.Send(request);
        return HandleResponse(result);
    }

    [HttpPost("logout")]
    [Authorize(Roles = "Payor")]
    public async Task<ActionResult<bool>> LogoutAsync([FromBody] RefreshTokenCommand request)
    {
        var result = await Sender.Send(new LogoutCommand { RefreshToken = request.RefreshToken });
        return HandleResponse(result);
    }
}
