using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Queries.Mobile.GetMobileBindInfo;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

/// <summary>
/// Anonymous collector-app binding. A freshly installed generic app opens the bind link and calls this to
/// learn which LGU it belongs to (+ that LGU's branding). Not a security boundary — login + LGU-scoped
/// accounts remain the real gate.
/// </summary>
[Route("api/mobile/bind")]
[ApiController]
public class MobileBindController(ISender sender) : ApiBaseController(sender)
{
    [HttpGet("{token}")]
    [AllowAnonymous]
    public async Task<ActionResult<MobileBindInfoDto>> ResolveAsync(string token)
    {
        var result = await Sender.Send(new GetMobileBindInfoQuery(token));
        return HandleResponse(result);
    }
}
