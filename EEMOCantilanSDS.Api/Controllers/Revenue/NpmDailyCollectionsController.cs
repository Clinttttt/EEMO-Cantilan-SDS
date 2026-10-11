using EEMOCantilanSDS.Application.Common.Revenue;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

/// <summary>
/// Office correction of a posted NPM daily stall-fee Collection (IA-051 / IA-062). A posted Collection is never edited: it is
/// voided through the canonical correction record, which removes its financial effect and projects the days it paid unpaid.
/// The actor and tenant come from the token.
/// </summary>
[ApiController]
[Route("api/npm-daily")]
[Authorize(Roles = "SuperAdmin,Admin")]
public sealed class NpmDailyCollectionsController(ISender sender, NpmDailyCanonicalPoster poster) : ApiBaseController(sender)
{
    public sealed record VoidRequest(string Reason);
    public sealed record VoidOutcome(Guid CollectionId, string ReferenceCode);

    

    /// <summary>What an amount received would apply to for a month: full or part installments, oldest first. Records nothing.</summary>
    [HttpGet("stall/{stallId:guid}/part-payment-quote")]
    public async Task<ActionResult<EEMOCantilanSDS.Application.Dtos.DailyCollections.NpmPartPaymentQuoteDto>> QuotePartPaymentAsync(
        Guid stallId, [FromQuery] int year, [FromQuery] int month, [FromQuery] decimal amount, [FromServices] NpmPartPaymentWorkflow workflow, CancellationToken ct) =>
        HandleResponse(await workflow.QuoteAsync(stallId, year, month, amount, ct));

    /// <summary>Records the reviewed amount as one canonical Collection. The same ClientOperationId is the same Collection; a changed month is a conflict.</summary>
    [HttpPost("part-payment")]
    public async Task<ActionResult<EEMOCantilanSDS.Application.Dtos.DailyCollections.NpmPartPaymentOutcomeDto>> RecordPartPaymentAsync(
        [FromBody] EEMOCantilanSDS.Application.Dtos.DailyCollections.NpmPartPaymentRequest request, [FromServices] NpmPartPaymentWorkflow workflow, CancellationToken ct)
    {
        var result = await workflow.PostAsync(request, ct);
        return HandleResponse(result.IsSuccess
            ? EEMOCantilanSDS.Application.Common.Result<EEMOCantilanSDS.Application.Dtos.DailyCollections.NpmPartPaymentOutcomeDto>.Success(new EEMOCantilanSDS.Application.Dtos.DailyCollections.NpmPartPaymentOutcomeDto(result.Value!.CollectionId, result.Value.ReferenceCode, result.Value.Amount, result.Value.Existing))
            : EEMOCantilanSDS.Application.Common.Result<EEMOCantilanSDS.Application.Dtos.DailyCollections.NpmPartPaymentOutcomeDto>.Failure(result.Error ?? "The payment could not be recorded.", result.Status));
    }

    [HttpPost("collections/{collectionId:guid}/void")]
    public async Task<ActionResult<VoidOutcome>> VoidAsync(Guid collectionId, [FromBody] VoidRequest request, CancellationToken ct)
    {
        var result = await poster.VoidAsync(collectionId, request.Reason, ct);
        return HandleResponse(result.IsSuccess
            ? EEMOCantilanSDS.Application.Common.Result<VoidOutcome>.Success(new VoidOutcome(result.Value!.CollectionId, result.Value.ReferenceCode))
            : EEMOCantilanSDS.Application.Common.Result<VoidOutcome>.Failure(result.Error ?? "The collection could not be voided.", result.Status));
    }
}
