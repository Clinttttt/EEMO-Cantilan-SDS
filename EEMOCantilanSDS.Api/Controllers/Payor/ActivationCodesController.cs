using EEMOCantilanSDS.Application.Command.Payors.GenerateStallActivationCode;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Payors;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

/// <summary>
/// Staff issuance of payor activation codes. Admins/heads (web) and collectors (mobile, restricted to
/// their assigned facilities) can generate a single-use code to hand to a payor for self-activation.
/// </summary>
[Route("api/activation-codes")]
[ApiController]
[Authorize(Roles = "SuperAdmin,Admin,Collector")]
public class ActivationCodesController(ISender sender) : ApiBaseController(sender)
{
    /// <summary>ST-19: a code for a HISTORICAL liable account (original stall contract or space obligation account), issued only for an actual unpaid account the caller may see.</summary>
    [HttpPost("historical")]
    public async Task<ActionResult<HistoricalAccessCodeDto>> IssueHistoricalAsync([FromBody] IssueHistoricalAccessRequest request, [FromServices] HistoricalAccessWorkflow workflow, CancellationToken ct) =>
        HandleResponse(await workflow.IssueAsync(request, ct));

    [HttpPost("generate")]
    public async Task<ActionResult<StallActivationCodeDto>> GenerateAsync([FromBody] GenerateStallActivationCodeCommand request)
    {
        var result = await Sender.Send(request);
        return HandleResponse(result);
    }
}
