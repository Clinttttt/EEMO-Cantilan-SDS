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

    [HttpPost("collections/{collectionId:guid}/void")]
    public async Task<ActionResult<VoidOutcome>> VoidAsync(Guid collectionId, [FromBody] VoidRequest request, CancellationToken ct)
    {
        var result = await poster.VoidAsync(collectionId, request.Reason, ct);
        return HandleResponse(result.IsSuccess
            ? EEMOCantilanSDS.Application.Common.Result<VoidOutcome>.Success(new VoidOutcome(result.Value!.CollectionId, result.Value.ReferenceCode))
            : EEMOCantilanSDS.Application.Common.Result<VoidOutcome>.Failure(result.Error ?? "The collection could not be voided.", result.Status));
    }
}
