using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.CreateFirstAdmin;
using EEMOCantilanSDS.Application.Queries.Auth.GetSetupStatus;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

[AllowAnonymous]
public class SetupController(ISender sender) : ApiBaseController(sender)
{
    [HttpGet("status")]
    public async Task<ActionResult<SetupStatusDto>> GetSetupStatus()
    {
        var result = await Sender.Send(new GetSetupStatusQuery());
        return HandleResponse(result);
    }

    [HttpPost("create-first-admin")]
    public async Task<ActionResult<bool>> CreateFirstAdmin([FromBody] CreateFirstAdminCommand request)
    {
        var result = await Sender.Send(request);
        return HandleResponse(result);
    }
}


