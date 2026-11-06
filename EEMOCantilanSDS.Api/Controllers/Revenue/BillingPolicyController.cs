using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

[ApiController]
[Route("api/billing-policy")]
[Authorize(Roles = "SuperAdmin,Admin")]
public sealed class BillingPolicyController(ISender sender, BillingPolicyWorkflow workflow) : ApiBaseController(sender)
{
    [HttpGet]
    public async Task<ActionResult<BillingPolicyPageDto>> ReadAsync(CancellationToken ct) =>
        HandleResponse(await workflow.ReadAsync(ct));

    [HttpPost]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<BillingPolicyPageDto>> RecordAsync([FromBody] RecordBillingPolicyRequest request, CancellationToken ct) =>
        HandleResponse(await workflow.RecordAsync(request, ct));
}
