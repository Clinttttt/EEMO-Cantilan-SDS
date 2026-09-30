using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

/// <summary>
/// Approved penalty definitions and the register of posted fines (IA-049). A fine is never entered here: it is collected
/// by adding an approved definition to an Official Receipt draft in the Composer.
/// </summary>
[ApiController]
[Route("api/penalties")]
[Authorize(Roles = "SuperAdmin,Admin")]
public sealed class PenaltiesController(ISender sender, PenaltyDefinitionWorkflow workflow) : ApiBaseController(sender)
{
    [HttpGet("definitions")]
    public async Task<ActionResult<IReadOnlyList<PenaltyDefinitionDto>>> DefinitionsAsync(CancellationToken ct) =>
        HandleResponse(await workflow.GetDefinitionsAsync(ct));

    [HttpPost("definitions")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<PenaltyDefinitionDto>> DefineAsync(
        [FromBody] DefinePenaltyRequest request, CancellationToken ct) =>
        HandleResponse(await workflow.DefineAsync(request, ct));

    [HttpGet("register")]
    public async Task<ActionResult<IReadOnlyList<PenaltyRegisterRowDto>>> RegisterAsync(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        HandleResponse(await workflow.GetRegisterAsync(from, to, ct));
}
