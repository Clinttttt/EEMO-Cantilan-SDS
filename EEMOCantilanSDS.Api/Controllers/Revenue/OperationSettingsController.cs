using EEMOCantilanSDS.Application.Command.Settings.AddTenantOperation;
using EEMOCantilanSDS.Application.Command.Settings.UpdateTenantOperationSetting;
using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Application.Queries.Settings.GetOperationSetup;
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

    /// <summary>Where each operation stands (Available / Added / Setup required / Ready), from backend facts. Read-only.</summary>
    [Authorize(Roles = "SuperAdmin,Admin")]
    [HttpGet("setup")]
    public async Task<ActionResult<OperationSetupOverviewDto>> GetSetupAsync() =>
        HandleResponse(await Sender.Send(new GetOperationSetupQuery()));

    /// <summary>Adds an operation the municipality did not select at onboarding. Lists it and creates its revenue source if none exists; authorizes nothing.</summary>
    [Authorize(Roles = "SuperAdmin")]
    [HttpPost("{operationCode}/add")]
    public async Task<ActionResult<AddTenantOperationResultDto>> AddAsync(
        string operationCode, [FromBody] AddTenantOperationRequest request) =>
        HandleResponse(await Sender.Send(new AddTenantOperationCommand(operationCode, request.LocalDisplayName, request.ExpectedVersion)));

    /// <summary>Changed by the Head only.</summary>
    [Authorize(Roles = "SuperAdmin")]
    [HttpPut("{operationCode}")]
    public async Task<ActionResult<TenantOperationSettingsDto>> UpdateAsync(
        string operationCode, [FromBody] UpdateTenantOperationSettingRequest request) =>
        HandleResponse(await Sender.Send(new UpdateTenantOperationSettingCommand(
            operationCode, request.IsListed, request.LocalDisplayName, request.ExpectedVersion)));
}
