using EEMOCantilanSDS.Application.Command.DailyCollections.RecordDailyCollection;
using EEMOCantilanSDS.Application.Command.DailyCollections.SaveDailyCollectionOrForDays;
using EEMOCantilanSDS.Application.Command.DailyCollections.SaveDailyCollectionOrNumber;
using EEMOCantilanSDS.Application.Command.DailyCollections.SettleNpmDays;
using EEMOCantilanSDS.Application.Command.DailyCollections.SettleNpmMonth;
using EEMOCantilanSDS.Application.Dtos.DailyCollections;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Queries.DailyCollections.GetDailyCollectionMonth;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

[Authorize(Roles = "SuperAdmin,Admin,Collector")]
public class DailyCollectionsController(ISender sender) : ApiBaseController(sender)
{
    [HttpPost("npm-month/activate-opening")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<long>> ActivateNpmOpeningMonth(NpmOpeningMonthActivationRequest request,
        [FromServices] NpmRentMonthWorkflow workflow, CancellationToken ct) =>
        HandleResponse(await workflow.ActivateOpeningMonthAsync(request, ct));

    [HttpGet("npm-month/opening-assessment")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<NpmOpeningMonthAssessmentReview>> ReviewNpmOpeningMonth(Guid stallId, Guid contractId, DateOnly period,
        [FromServices] NpmRentMonthWorkflow workflow, CancellationToken ct) =>
        HandleResponse(await workflow.ReviewOpeningMonthAsync(stallId, contractId, period, ct));

    [HttpPost("npm-month/prepare-opening")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<Guid>> PrepareNpmOpeningMonth(NpmOpeningMonthPreparationRequest request,
        [FromServices] NpmRentMonthWorkflow workflow, CancellationToken ct) =>
        HandleResponse(await workflow.PrepareOpeningMonthAsync(request.StallId, request.ContractId, request.PeriodStart, ct));

    [HttpGet("npm-month/review")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<NpmRentMonthHeadReview>> ReviewNpmMonth(Guid stallId, DateOnly period,
        [FromServices] NpmRentMonthWorkflow workflow, CancellationToken ct) => HandleResponse(await workflow.ReviewHeadAsync(stallId, period, ct));

    [HttpPost("npm-month/credits")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<NpmRentMonthCreditOutcome>> CreditNpmMonth(NpmRentMonthCreditRequest request,
        [FromServices] NpmRentMonthWorkflow workflow, CancellationToken ct) => HandleResponse(await workflow.ApplyReviewedCreditAsync(request, ct));

    [HttpPost("npm-month/reverse")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<NpmRentMonthPaymentOutcome>> ReverseNpmMonth(NpmRentMonthReverseRequest request,
        [FromServices] NpmRentMonthWorkflow workflow, CancellationToken ct) => HandleResponse(await workflow.ReverseReviewedAsync(request, ct));

    [HttpPost("npm-month/replace")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<NpmRentMonthPaymentOutcome>> ReplaceNpmMonth(NpmRentMonthReplacementRequest request,
        [FromServices] NpmRentMonthWorkflow workflow, CancellationToken ct) => HandleResponse(await workflow.ReplaceReviewedAsync(request, ct));
    [HttpGet("npm-month/day-quote")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<NpmRentMonthDayReview>> NpmMonthlyDayQuote(Guid stallId, DateOnly installmentDate,
        [FromServices] NpmRentMonthWorkflow workflow, [FromServices] EEMOCantilanSDS.Application.Common.Interface.Time.IClock clock, CancellationToken ct)
    {
        var period = new DateOnly(installmentDate.Year, installmentDate.Month, 1);
        if (!await workflow.IsMonthActivatedAsync(stallId, period, ct))
            return HandleResponse(EEMOCantilanSDS.Application.Common.Result<NpmRentMonthDayReview>.Success(new(false, null)));
        var quote = await workflow.QuoteAsync(stallId, period, clock.PhilippineToday, NpmRentPaymentScope.DailyInstallment, ct, installmentDate);
        return HandleResponse(quote.IsSuccess
            ? EEMOCantilanSDS.Application.Common.Result<NpmRentMonthDayReview>.Success(new(true, quote.Value))
            : EEMOCantilanSDS.Application.Common.Result<NpmRentMonthDayReview>.Failure(quote.Error!, quote.Status));
    }
    [HttpGet("npm-month/available")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public ActionResult<bool> NpmMonthlyAvailable([FromServices] NpmRentMonthWorkflow workflow) =>
        HandleResponse(EEMOCantilanSDS.Application.Common.Result<bool>.Success(workflow.Enabled));

    // Exactly the same authoritative quote/post contract as Mobile. Unavailable until coordinated activation.
    [HttpGet("npm-month/whole-quote")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<NpmWholePaymentQuoteDto>> NpmMonthlyWholeQuote(Guid stallId, int year, int month,
        [FromServices] NpmWholePaymentWorkflow whole, [FromServices] NpmRentMonthWorkflow workflow, CancellationToken ct) =>
        HandleResponse(workflow.Enabled ? await whole.QuoteAsync(stallId, year, month, ct)
            : EEMOCantilanSDS.Application.Common.Result<NpmWholePaymentQuoteDto>.Failure("Monthly settlement is not activated.", 409));

    [HttpPost("npm-month/whole-payment")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<NpmRentMonthPaymentOutcome>> NpmMonthlyWholePayment([FromBody] NpmWholePaymentRequest request,
        [FromServices] NpmWholePaymentWorkflow whole, [FromServices] NpmRentMonthWorkflow workflow, CancellationToken ct)
    {
        if (!workflow.Enabled) return HandleResponse(EEMOCantilanSDS.Application.Common.Result<NpmRentMonthPaymentOutcome>.Failure("Monthly settlement is not activated.", 409));
        var result = await whole.PostAsync(request, ct);
        return HandleResponse(result.IsSuccess
            ? EEMOCantilanSDS.Application.Common.Result<NpmRentMonthPaymentOutcome>.Success(new(result.Value!.CollectionId, result.Value.ReferenceCode, result.Value.Amount, result.Value.Existing))
            : EEMOCantilanSDS.Application.Common.Result<NpmRentMonthPaymentOutcome>.Failure(result.Error!, result.Status));
    }

    [HttpPost("record")]
    public async Task<ActionResult<bool>> RecordDailyCollection([FromBody] RecordDailyCollectionCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }

    // OR numbers are entered manually by admins/head only (never by field collectors).
    [HttpPost("or")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<bool>> SaveOrNumber([FromBody] SaveDailyCollectionOrNumberCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }

    // Apply one OR (receipt) to several PAID days of the same stall in one go (admin/head only) —
    // for when a single physical receipt covers multiple days of the same payor.
    [HttpPost("or-days")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<bool>> SaveOrForDays([FromBody] SaveDailyCollectionOrForDaysCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }

    // Settle a whole NPM month at once (admin/head only) — records the month's collectable days as paid.
    [HttpPost("settle-month")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<bool>> SettleNpmMonth([FromBody] SettleNpmMonthCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }

    // Settle specific NPM days at once (admin/head only), optionally stamping one OR across them.
    [HttpPost("settle-days")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<bool>> SettleNpmDays([FromBody] SettleNpmDaysCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }

    // The still-settleable (unpaid, collectable) days of an NPM stall for a month — the day picker source.
    [HttpGet("stall/{stallId}/settleable-days")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<IReadOnlyList<Application.Dtos.DailyCollections.SettleableNpmDayDto>>> GetSettleableDays(
        Guid stallId, [FromQuery] int year, [FromQuery] int month, [FromQuery] Guid? contractId = null)
    {
        var result = await Sender.Send(new Application.Queries.DailyCollections.GetSettleableNpmDays.GetSettleableNpmDaysQuery(stallId, year, month, contractId));
        return HandleResponse(result);
    }

// What an amount received would settle for a month: whole payable days, oldest first. Records nothing.    [HttpGet("stall/{stallId}/amount-quote")]    [Authorize(Roles = "SuperAdmin,Admin")]    public async Task<ActionResult<Application.Dtos.DailyCollections.NpmAmountQuoteDto>> QuoteAmount(        Guid stallId, [FromQuery] int year, [FromQuery] int month, [FromQuery] decimal amount)    {        var result = await Sender.Send(new Application.Queries.DailyCollections.QuoteNpmAmount.QuoteNpmAmountQuery(stallId, year, month, amount));        return HandleResponse(result);    }
    [HttpGet("stall/{stallId}/month")]
    public async Task<ActionResult<DailyCollectionMonthDto>> GetDailyCollectionMonth(Guid stallId, [FromQuery] int year, [FromQuery] int month)
    {
        var query = new GetDailyCollectionMonthQuery(stallId, year, month);
        var result = await Sender.Send(query);
        return HandleResponse(result);
    }
}
