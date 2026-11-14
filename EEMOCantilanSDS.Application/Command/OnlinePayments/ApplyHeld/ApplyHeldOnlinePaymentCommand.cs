using System.Globalization;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Command.OnlinePayments.ApplyHeld;

/// <summary>
/// The Head applies a held online payment to the space's rent on the payor's written request (IA-082; RA 7160, Sec. 196): the months are recorded
/// paid under the OR the office issues, and the decision is recorded, in one save. <paramref name="Months"/> is the plan the Head saw; a different
/// plan now is refused, so nothing is applied that was not shown.
/// </summary>
public record ApplyHeldOnlinePaymentCommand(Guid TransactionId, string ORNumber, string WrittenRequest, IReadOnlyList<HeldPaymentMonthDto> Months)
    : IRequest<Result<bool>>;

public class ApplyHeldOnlinePaymentCommandValidator : AbstractValidator<ApplyHeldOnlinePaymentCommand>
{
    public ApplyHeldOnlinePaymentCommandValidator()
    {
        RuleFor(x => x.TransactionId).NotEmpty();
        RuleFor(x => x.ORNumber).NotEmpty().MaximumLength(30);
        RuleFor(x => x.WrittenRequest).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Months).NotEmpty();
    }
}

/// <summary>What a held capture can be applied to: monthly rent on the space it was paid for, in this office, while it still awaits a decision.</summary>
public static class HeldPayment
{
    public static async Task<Result<(Stall Stall, HeldPaymentPlanDto Plan)>> PlanAsync(OnlinePaymentTransaction? transaction,
        IPaymentRepository payments, IStallRepository stalls, BillingRules billing, CancellationToken ct)
    {
        if (transaction is null) return Result<(Stall, HeldPaymentPlanDto)>.NotFound();
        if (!transaction.IsAwaitingOfficeDecision)
            return Result<(Stall, HeldPaymentPlanDto)>.Failure("This payment is not awaiting a decision. Refresh the list.", ResultStatus.Conflict);
        if (transaction.TargetKind != OnlinePaymentTargetKind.MonthlyRecord || transaction.PaymentRecordId is not { } recordId)
            return Result<(Stall, HeldPaymentPlanDto)>.Failure("Only a monthly rent payment can be applied here. Record a refund instead.", ResultStatus.Invalid);

        var record = await payments.GetByIdAsync(recordId, ct);
        var stall = record is null ? null : await stalls.GetByIdAsync(record.StallId, ct);
        if (stall is null) return Result<(Stall, HeldPaymentPlanDto)>.NotFound();
        return Result<(Stall, HeldPaymentPlanDto)>.Success(
            (stall, await billing.HeldPaymentPlanAsync(stall, transaction.Id, transaction.CapturedAmount ?? transaction.Amount, ct)));
    }
}

public class ApplyHeldOnlinePaymentCommandHandler(
    IOnlinePaymentRepository onlinePayments,
    IPaymentRepository payments,
    IStallRepository stalls,
    BillingRules billing,
    IAppDbContext context,
    ICurrentUserService user,
    IUnitOfWork unitOfWork,
    IEemoCacheInvalidator cache,
    ITenantContext tenant,
    IClock clock) : IRequestHandler<ApplyHeldOnlinePaymentCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(ApplyHeldOnlinePaymentCommand request, CancellationToken ct)
    {
        // Serializable, as the Head's other decisions are: two saves on one capture cannot both stand, so it is never applied twice or applied and refunded.
        await using var saving = context.HasActiveTransaction ? null : await context.BeginSerializableTransactionAsync(ct);

        var transaction = await onlinePayments.GetByIdAsync(request.TransactionId, ct);
        var planned = await HeldPayment.PlanAsync(transaction, payments, stalls, billing, ct);
        if (!planned.IsSuccess) return Result<bool>.Failure(planned.Error ?? "", planned.Status);
        var (stall, plan) = planned.Value;
        if (plan.Refusal is { } refusal) return Result<bool>.Failure(refusal, ResultStatus.Conflict);
        if (!plan.Months.SequenceEqual(request.Months))
            return Result<bool>.Failure("The months this payment would pay have changed. Refresh and check them again.", ResultStatus.Conflict);

        var or = request.ORNumber.Trim();
        if (!await payments.IsMonthlyOrAvailableForStallAsync(or, stall.Id, ct))
            return Result<bool>.Failure("OR number already exists.", ResultStatus.Conflict);

        var recordedBy = user.Username ?? "Head";
        var remarks = $"Applied from online payment {transaction!.Reference}";
        foreach (var month in plan.Months)
        {
            var existing = await payments.GetPaymentRecordAsync(stall.Id, month.Year, month.Month, ct);
            var record = existing is null ? PaymentRecord.Create(stall.Id, month.Year, month.Month, month.Due, recordedBy) : await payments.GetByIdAsync(existing.Id, ct);
            if (record is null) return Result<bool>.NotFound();
            // Partial takes the month's whole amount paid, and becomes Paid by itself once that reaches the bill.
            record.UpdateStatus(PaymentStatus.Partial, record.AmountPaid + month.Applied, remarks, recordedBy, user.CollectorId);
            record.SetOrNumber(or, recordedBy);
            if (existing is null) await payments.AddAsync(record, ct);
            else await payments.UpdateAsync(record, ct);
        }

        var note = $"On the payor's written request: {request.WrittenRequest.Trim()}. Applied to {Describe(plan.Months)}.";
        transaction.RecordOfficeDecision(OnlinePaymentDisposition.Applied, or, note[..Math.Min(500, note.Length)], recordedBy, clock.UtcNow);

        try
        {
            await unitOfWork.SaveChangesAsync(ct);
            if (saving is not null) await saving.CommitAsync(ct);
        }
        catch (Exception e) when (LostARace(e))
        {
            return Result<bool>.Failure("This payment or its months changed while saving. Refresh the list.", ResultStatus.Conflict);
        }

        foreach (var month in plan.Months)
            await cache.InvalidatePaymentAffectedViewsAsync(tenant.TenantCode, stall.Facility?.Code, month.Year, month.Month, ct);
        return Result<bool>.Success(true);
    }

    private static string Describe(IEnumerable<HeldPaymentMonthDto> months) => string.Join(", ", months.Select(m =>
        new DateOnly(m.Year, m.Month, 1).ToString("MMM yyyy", CultureInfo.InvariantCulture) + " ₱" + m.Applied.ToString("N2", CultureInfo.InvariantCulture)
        + (m.Applied < m.Due ? " of ₱" + m.Due.ToString("N2", CultureInfo.InvariantCulture) : "")));

    // A concurrent save on the same capture or month: an optimistic-concurrency miss, a serialization failure, or the same month inserted twice.
    internal static bool LostARace(Exception e)
    {
        for (Exception? ex = e; ex is not null; ex = ex.InnerException)
            if (ex is DbUpdateConcurrencyException || ex.GetType().GetProperty("SqlState")?.GetValue(ex) is "40001" or "23505")
                return true;
        return false;
    }
}
