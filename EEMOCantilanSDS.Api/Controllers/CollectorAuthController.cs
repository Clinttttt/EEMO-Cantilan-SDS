using EEMOCantilanSDS.Application.Command.Auth.CollectorAuth.Login;
using EEMOCantilanSDS.Application.Command.Auth.GenerateRefreshToken;
using EEMOCantilanSDS.Application.Command.Auth.Logout;
using EEMOCantilanSDS.Application.Dtos;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class CollectorAuthController(ISender sender) : ApiBaseController(sender)
{
    [HttpPost("login")]
    public async Task<ActionResult<TokenResponseDto>> LoginAsync([FromBody] CollectorLoginCommand request)
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
    [Authorize(Roles = "Collector")]
    public async Task<ActionResult<bool>> LogoutAsync([FromBody] RefreshTokenCommand request)
    {
        var result = await Sender.Send(new LogoutCommand { RefreshToken = request.RefreshToken });
        return HandleResponse(result);
    }
}
