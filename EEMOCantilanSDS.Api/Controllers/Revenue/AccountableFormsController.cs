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

    /// <summary>Assigns several collectors' ranges from one book in one all-or-nothing save (custody only).</summary>
    [HttpPost("assign-batch")]
    public async Task<ActionResult<int>> AssignBatchAsync(
        [FromBody] AssignAccountableFormBatchRequest request, CancellationToken ct) =>
        HandleResponse(await workflow.AssignBatchAsync(request, ct));

    /// <summary>Moves assigned, unused units from one collector to another with a recorded reason (custody only).</summary>
    [HttpPost("transfer")]
    public async Task<ActionResult<int>> TransferAsync(
        [FromBody] TransferAccountableFormsRequest request, CancellationToken ct) =>
        HandleResponse(await workflow.TransferAsync(request, ct));

    // Returns unused assigned units to office custody (IA-052). Issued, consumed or spoiled units cannot return.
    [HttpPost("return")]
    public async Task<ActionResult<int>> ReturnUnusedAsync(
        [FromBody] ReturnUnusedFormsRequest request, CancellationToken ct) =>
        HandleResponse(await workflow.ReturnUnusedAsync(request, ct));

    // Records a blank form as spoiled or cancelled, with a reason. It is never revenue and never returns to stock.
    [HttpPost("spoilage")]
    public async Task<ActionResult<SpoiledFormDto>> SpoilAsync(
        [FromBody] SpoilFormRequest request, CancellationToken ct) =>
        HandleResponse(await workflow.SpoilAsync(request, ct));

    [HttpGet("spoilage")]
    public async Task<ActionResult<IReadOnlyList<SpoiledFormDto>>> SpoiledAsync(CancellationToken ct) =>
        HandleResponse(await workflow.GetSpoiledAsync(ct));
}
