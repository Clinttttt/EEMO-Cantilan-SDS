using EEMOCantilanSDS.Application.Dtos.Transactions;
using EEMOCantilanSDS.Application.Queries.Transactions.GetRecentTransactions;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

[Authorize(Roles = "SuperAdmin,Admin")]
public class TransactionsController(ISender sender) : ApiBaseController(sender)
{
    /// <summary>
    /// Recent recorded transactions across all facilities (or a single facility), newest first.
    /// </summary>
    [HttpGet("recent")]
    public async Task<ActionResult<IReadOnlyList<TransactionFeedDto>>> GetRecent(
        [FromQuery] FacilityCode? facility, [FromQuery] DateOnly? onDate, [FromQuery] int limit = 100)
        => HandleResponse(await Sender.Send(new GetRecentTransactionsQuery(facility, onDate, limit)));
}
