using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers.Revenue;

/// <summary>
/// ST-20. The Head-controlled route that makes a monthly facility's FRESH unpaid rent rows collectible with a genuine SRC. The plan writes nothing;
/// activation is bound to the reviewed plan and the attested checklist, runs each row through the existing source-scoped cutover, and never touches a
/// row that carries legacy settlement evidence. Deploying this route activates nothing: a row changes only when the Head activates it.
/// </summary>
[ApiController]
[Route("api/rent-activations")]
[Authorize(Roles = "SuperAdmin,Admin")]
public sealed class RentActivationsController(ISender sender, RentActivationWorkflow workflow) : ApiBaseController(sender)
{
    [HttpPost("plan")]
    public async Task<ActionResult<RentActivationPlanDto>> PlanAsync([FromBody] RentActivationPlanRequest request, CancellationToken ct) =>
        HandleResponse(await workflow.PlanAsync(request, ct));

    [HttpPost]
    public async Task<ActionResult<RentActivationResultDto>> ActivateAsync([FromBody] RentActivationRequest request, CancellationToken ct) =>
        HandleResponse(await workflow.ActivateAsync(request, ct));
}
