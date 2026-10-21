using EEMOCantilanSDS.Application.Command.Settings.UpdateTenantOperationSetting;
using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Application.Queries.Settings.GetTenantOperationSettings;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

/// <summary>
/// A municipality's own operation directory configuration: which operations it lists and what it calls them.
/// Presentation only - nothing here grants or removes authority to post money. The municipality is always the caller's own
/// (from the token); neither endpoint takes one.
/// </summary>
[Route("api/operation-settings")]
[ApiController]
public sealed class OperationSettingsController(ISender sender) : ApiBaseController(sender)
{
    /// <summary>Read by any signed-in office user: the Operations directory renders from it.</summary>
    [Authorize(Roles = "SuperAdmin,Admin")]
    [HttpGet]
    public async Task<ActionResult<TenantOperationSettingsDto>> GetAsync() =>
        HandleResponse(await Sender.Send(new GetTenantOperationSettingsQuery()));

    /// <summary>Changed by the Head only.</summary>
    [Authorize(Roles = "SuperAdmin")]
    [HttpPut("{operationCode}")]
    public async Task<ActionResult<TenantOperationSettingsDto>> UpdateAsync(
        string operationCode, [FromBody] UpdateTenantOperationSettingRequest request) =>
        HandleResponse(await Sender.Send(new UpdateTenantOperationSettingCommand(
            operationCode, request.IsListed, request.LocalDisplayName, request.ExpectedVersion)));
}
