using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

[ApiController]
[Route("api/accountable-forms")]
[Authorize(Roles = "SuperAdmin,Admin")]
public sealed class AccountableFormsController(
    ISender sender,
    AccountableFormCustodyWorkflow workflow) : ApiBaseController(sender)
{
    [HttpGet("books")]
    public async Task<ActionResult<IReadOnlyList<AccountableFormBookDto>>> BooksAsync(CancellationToken ct) =>
        HandleResponse(await workflow.ListAsync(ct));

    [HttpPost("books")]
    public async Task<ActionResult<AccountableFormBookDto>> ReceiveAsync(
        [FromBody] ReceiveAccountableFormBookRequest request, CancellationToken ct) =>
        HandleResponse(await workflow.ReceiveAsync(request, ct));

    [HttpPost("cash-tickets/assign")]
    public async Task<ActionResult<int>> AssignCashTicketsAsync(
        [FromBody] AssignAccountableFormRangeRequest request, CancellationToken ct) =>
        HandleResponse(await workflow.AssignCashTicketRangeAsync(request, ct));

    /// <summary>Assigns a range from a received Official Receipt book to a collector (custody only).</summary>
    [HttpPost("official-receipts/assign")]
    public async Task<ActionResult<int>> AssignOfficialReceiptsAsync(
        [FromBody] AssignAccountableFormRangeRequest request, CancellationToken ct) =>
        HandleResponse(await workflow.AssignRangeAsync(request, RevenueInstrumentType.OfficialReceipt, ct));
}
