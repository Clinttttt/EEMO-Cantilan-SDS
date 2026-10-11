using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EEMOCantilanSDS.Api.Controllers;
[ApiController, Route("api/direct-fines"), Authorize(Roles = "SuperAdmin")]
public sealed class DirectFinesController(ISender sender, DirectFineCollectionWorkflow workflow) : ApiBaseController(sender)
{
    [HttpGet("options")] public async Task<ActionResult<DirectFineOptionsDto>> Options(CancellationToken ct) => HandleResponse(await workflow.OptionsAsync(ct));
    [HttpPost("quote")] public async Task<ActionResult<DirectFineQuoteDto>> Quote(DirectFineCollectionRequest request, CancellationToken ct) => HandleResponse(await workflow.QuoteAsync(request, ct));
    [HttpPost("post")] public async Task<ActionResult<GovernedServiceOutcomeDto>> Post(DirectFineCollectionRequest request, CancellationToken ct) => HandleResponse(await workflow.PostAsync(request, ct));
}
