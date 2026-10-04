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

    /// <summary>
    /// Previews or commits an automatic allocation of one book's unassigned, in-office units across collectors
    /// (custody only; a commit re-validates custody against the confirmed preview).
    /// </summary>
    [HttpPost("auto-allocate")]
    public async Task<ActionResult<AutoAllocatePlanDto>> AutoAllocateAsync(
        [FromBody] AutoAllocateFormsRequest request, CancellationToken ct) =>
        HandleResponse(await workflow.AutoAllocateAsync(request, ct));

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

    /// <summary>Registers a range from its printed serials (for example "2315601 A" to "2315650 A"); the serial is kept exactly as printed.</summary>
    [HttpPost("register")]
    public async Task<ActionResult<AccountableFormBookDto>> RegisterAsync(
        [FromBody] RegisterAccountableFormsRequest request, CancellationToken ct) =>
        HandleResponse(await workflow.RegisterAsync(request, ct));

    /// <summary>Reports a serial or range lost, whole or by copy. A blank unit is blocked at once; the external notice may follow.</summary>
    [HttpPost("loss")]
    public async Task<ActionResult<FormLossResultDto>> ReportLossAsync(
        [FromBody] ReportFormLossRequest request, CancellationToken ct) =>
        HandleResponse(await workflow.ReportLossAsync(request, ct));

    /// <summary>Adds the external RCD or notice reference to forms already recorded as cancelled or lost.</summary>
    [HttpPost("references")]
    public async Task<ActionResult<FormReferenceResultDto>> AddReferenceAsync(
        [FromBody] AddFormReferenceRequest request, CancellationToken ct) =>
        HandleResponse(await workflow.AddReferenceAsync(request, ct));

    [HttpGet("exceptions")]
    public async Task<ActionResult<IReadOnlyList<AccountableFormExceptionDto>>> ExceptionsAsync(CancellationToken ct) =>
        HandleResponse(await workflow.GetExceptionsAsync(ct));

    [HttpGet("position")]
    public async Task<ActionResult<AccountableFormPositionDto>> PositionAsync(
        [FromQuery] RevenueInstrumentType instrument, CancellationToken ct) =>
        HandleResponse(await workflow.GetPositionAsync(instrument, ct));

    [HttpGet("raaf-support")]
    public async Task<ActionResult<AccountableFormRaafSupportDto>> RaafSupportAsync(
        [FromQuery] RevenueInstrumentType instrument, [FromQuery] int year, [FromQuery] int month, CancellationToken ct) =>
        HandleResponse(await workflow.GetRaafSupportAsync(instrument, year, month, ct));

    [HttpGet("history")]
    public async Task<ActionResult<IReadOnlyList<AccountableFormHistoryEventDto>>> HistoryAsync(
        [FromQuery] RevenueInstrumentType instrument, [FromQuery] int limit = 200, CancellationToken ct = default) =>
        HandleResponse(await workflow.GetHistoryAsync(instrument, limit, ct));
}
