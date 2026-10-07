using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;
[ApiController, Authorize(Roles = "Collector"), Route("api/mobile/npm-daily-batch")]
public sealed class NpmDailyBatchController(ISender sender, NpmDailyBatchWorkflow workflow) : ApiBaseController(sender)
{
    [HttpGet("sources")]
    public async Task<ActionResult<IReadOnlyList<NpmDailyBatchSource>>> Sources(CancellationToken ct) => HandleResponse(await workflow.SourcesAsync(ct));
    [HttpGet("readiness")]
    public async Task<ActionResult<NpmDailyBatchReadiness>> Readiness(CancellationToken ct) => HandleResponse(await workflow.ReadinessAsync(ct));
    [HttpPost("quote")]
    public async Task<ActionResult<NpmDailyBatchQuote>> Quote(NpmDailyBatchIntent intent, CancellationToken ct) => HandleResponse(await workflow.PreviewAsync(intent, ct));
    [HttpPost("record")]
    public async Task<ActionResult<CollectionSessionResult>> Record(RecordNpmDailyBatchRequest request, CancellationToken ct) => HandleResponse(await workflow.RecordAsync(request, ct));
}
