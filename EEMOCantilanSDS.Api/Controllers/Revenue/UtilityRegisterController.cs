using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

/// <summary>
/// The ECF / WCF source register for the office: verified utility sources of every facility for a billing month, filtered, counted
/// and paged by the server. Read-only; preparing amounts and recording money use their own writers.
/// </summary>
[ApiController]
[Route("api/utility-register")]
[Authorize(Roles = "SuperAdmin,Admin")]
public sealed class UtilityRegisterController(ISender sender, IUtilityRegisterReader reader, EEMOCantilanSDS.Application.Common.Revenue.UtilityPreparationWorkflow preparation) : ApiBaseController(sender)
{
    [HttpGet]
    public async Task<ActionResult<UtilityRegisterPageDto>> Get(
        [FromQuery] UtilityKind kind, [FromQuery] int year, [FromQuery] int month, [FromQuery] FacilityCode? facility,
        [FromQuery] string? section, [FromQuery] string? status, [FromQuery] string? search,
        [FromQuery] int skip = 0, [FromQuery] int take = 25, CancellationToken ct = default) =>
        HandleResponse(await reader.GetAsync(new(kind, year, month, facility, section, status, search, skip, take), ct));

    /// <summary>Prepares approved amounts for one or several sources of a month. Records an obligation only: no Collection, no SRC.</summary>
    [HttpPost("prepare")]
    public async Task<ActionResult<UtilityPrepareResult>> Prepare([FromBody] UtilityPrepareRequest request, CancellationToken ct = default) =>
        HandleResponse(await preparation.PrepareAsync(request, ct));
}
