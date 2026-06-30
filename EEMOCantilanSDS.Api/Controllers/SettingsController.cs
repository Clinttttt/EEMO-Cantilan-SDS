using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Application.Queries.Settings.GetSystemSettings;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

[Authorize(Roles = "SuperAdmin,Admin")]
public class SettingsController(ISender sender, IWebHostEnvironment environment) : ApiBaseController(sender)
{
    [HttpGet]
    public async Task<ActionResult<SystemSettingsDto>> GetSettings()
    {
        var result = await Sender.Send(new GetSystemSettingsQuery(environment.EnvironmentName));
        return HandleResponse(result);
    }
}
