using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

/// <summary>
/// Vehicle classes and their approved effective-dated rates for Transportation / Parking. The Head defines classes and
/// rates; Head and Admin read them. A collector receives the rates in force through the governed-service terms, never here.
/// </summary>
[ApiController]
[Route("api/vehicle-classes")]
[Authorize(Roles = "SuperAdmin,Admin")]
public sealed class VehicleClassesController(ISender sender, VehicleClassWorkflow workflow) : ApiBaseController(sender)
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<VehicleClassDto>>> GetAsync(CancellationToken ct) =>
        HandleResponse(await workflow.GetAsync(ct));

    [HttpPost]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<VehicleClassDto>> SaveAsync([FromBody] SaveVehicleClassRequest request, CancellationToken ct) =>
        HandleResponse(await workflow.SaveAsync(request, ct));

    [HttpPost("{id:guid}/active")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<VehicleClassDto>> SetActiveAsync(Guid id, [FromBody] bool isActive, CancellationToken ct) =>
        HandleResponse(await workflow.SetActiveAsync(id, isActive, ct));
}
