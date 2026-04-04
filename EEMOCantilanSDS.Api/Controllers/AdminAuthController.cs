using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.Login;
using EEMOCantilanSDS.Application.Command.Auth.GenerateRefreshToken;
using EEMOCantilanSDS.Application.Dtos;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AdminAuthController(ISender sender) : ApiBaseController(sender)
{
    [HttpPost("login")]
    public async Task<ActionResult<TokenResponseDto>> LoginAsync([FromBody] LoginCommand request)
    {
        var result = await Sender.Send(request);
        return HandleResponse(result);
    }

    [HttpPost("refresh-token")]
    public async Task<ActionResult<TokenResponseDto>> RefreshAsync([FromBody] RefreshTokenCommand request)
    {
        var result = await Sender.Send(request);
        return HandleResponse(result);
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        return Ok();
    }
}
