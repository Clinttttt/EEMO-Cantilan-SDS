using EEMOCantilanSDS.Application.Common.Lifecycle;
using EEMOCantilanSDS.Application.Queries.Accounts.GetAccountLifecycle;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

/// <summary>
/// Read-only account lifecycle (state, history, available actions with reasons) for stalls, space rentals and event lots.
/// Space rentals (Kanmanggay, Vegetable/Fruit) also execute Resume, Renew, Reassign and Remove here (preview, then execute); permanent stalls do not: their Close/Resume/Renew/Reassign/Remove stay on the existing Stall endpoints.
/// </summary>
[Authorize(Roles = "SuperAdmin,Admin")]
public sealed class AccountLifecycleController(ISender sender) : ApiBaseController(sender)
{
    [HttpGet("{source}/{accountId:guid}")]
    public async Task<ActionResult<AccountLifecycleDto>> GetAsync(LifecycleSource source, Guid accountId, CancellationToken ct) =>
        HandleResponse(await Sender.Send(new GetAccountLifecycleQuery(source, accountId), ct));

    /// <summary>The lifecycle of several accounts of one source at once (a register's per-row action state). Read-only.</summary>
    [HttpPost("{source}/batch")]
    public async Task<ActionResult<IReadOnlyList<AccountLifecycleDto>>> GetManyAsync(LifecycleSource source, [FromBody] Guid[] accountIds, CancellationToken ct) =>
        HandleResponse(await Sender.Send(new GetAccountLifecycleBatchQuery(source, accountIds), ct));

    /// <summary>Preview a Resume, Renew, Reassign or Remove. Read-only; the workflow re-checks the role and tenant.</summary>
    [HttpPost("{source}/{accountId:guid}/preview")]
    public async Task<ActionResult<LifecyclePreviewDto>> PreviewAsync(LifecycleSource source, Guid accountId, [FromBody] LifecycleCommand command, CancellationToken ct) =>
        HandleResponse(await Sender.Send(new PreviewAccountLifecycleQuery(source, accountId, command), ct));

    /// <summary>Execute a previewed command. Admin or the Head (as for a stall); retiring is the Head only. Idempotent on the client operation id.</summary>
    [HttpPost("{source}/{accountId:guid}/execute")]
    public async Task<ActionResult<LifecycleOutcome>> ExecuteAsync(LifecycleSource source, Guid accountId, [FromBody] LifecycleExecution execution, CancellationToken ct) =>
        HandleResponse(await Sender.Send(new ExecuteAccountLifecycleCommand(source, accountId, execution), ct));
}
