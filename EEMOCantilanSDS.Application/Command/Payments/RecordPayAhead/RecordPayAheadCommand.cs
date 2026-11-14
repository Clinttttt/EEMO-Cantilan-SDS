using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using FluentValidation;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Payments.RecordPayAhead;

/// <summary>
/// Rent taken ahead at the office: consecutive months on one receipt, a year included. The first month and the total are what the clerk saw, so a retry
/// after success finds that month paid and is refused rather than paying the months after it.
/// </summary>
public record RecordPayAheadCommand(Guid StallId, int FirstYear, int FirstMonth, int Months, decimal ExpectedTotal, string ORNumber) : IRequest<Result<bool>>;

public class RecordPayAheadCommandValidator : AbstractValidator<RecordPayAheadCommand>
{
    public RecordPayAheadCommandValidator()
    {
        RuleFor(x => x.StallId).NotEmpty();
        RuleFor(x => x.FirstYear).InclusiveBetween(2001, 2100);
        RuleFor(x => x.FirstMonth).InclusiveBetween(1, 12);
        RuleFor(x => x.Months).InclusiveBetween(1, 12);
        RuleFor(x => x.ORNumber).NotEmpty().MaximumLength(30);
    }
}

public class RecordPayAheadCommandHandler(
    IPaymentRepository payments,
    IStallRepository stalls,
    BillingRules billing,
    ICurrentUserService user,
    IUnitOfWork unitOfWork,
    IEemoCacheInvalidator cache,
    ITenantContext tenant) : IRequestHandler<RecordPayAheadCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(RecordPayAheadCommand request, CancellationToken ct)
    {
        var stall = await stalls.GetByIdAsync(request.StallId, ct);
        if (stall is null) return Result<bool>.NotFound();

        var options = await billing.PayAheadAsync(stall, ct);
        if (options.HeldBy is { } held) return Result<bool>.Failure(held, ResultStatus.Conflict);
        if (options.Months.Count == 0 || options.Months[0].Year != request.FirstYear || options.Months[0].Month != request.FirstMonth)
            return Result<bool>.Failure("These months have changed. Refresh this account before recording.", ResultStatus.Conflict);
        if (!options.Counts.Contains(request.Months))
            return Result<bool>.Failure("This office does not take that many months on one receipt.", ResultStatus.Invalid);

        var months = options.Months.Take(request.Months).ToList();
        if (months.Sum(m => m.Amount) != request.ExpectedTotal)
            return Result<bool>.Failure("The amount for these months has changed. Refresh this account before recording.", ResultStatus.Conflict);

        var or = request.ORNumber.Trim();
        if (!await payments.IsMonthlyOrAvailableForStallAsync(or, stall.Id, ct))
            return Result<bool>.Failure("OR number already exists.", ResultStatus.Conflict);

        var recordedBy = user.Username ?? "Admin";
        foreach (var month in months)
        {
            var existing = await payments.GetPaymentRecordAsync(stall.Id, month.Year, month.Month, ct);
            var record = existing is null ? PaymentRecord.Create(stall.Id, month.Year, month.Month, month.Amount, recordedBy) : await payments.GetByIdAsync(existing.Id, ct);
            if (record is null) return Result<bool>.NotFound();
            record.UpdateStatus(PaymentStatus.Paid, 0m, null, recordedBy, user.CollectorId);
            record.SetOrNumber(or, recordedBy);
            if (existing is null) await payments.AddAsync(record, ct);
            else await payments.UpdateAsync(record, ct);
        }

        // One save, so a year is recorded whole or not at all.
        await unitOfWork.SaveChangesAsync(ct);
        foreach (var month in months)
            await cache.InvalidatePaymentAffectedViewsAsync(tenant.TenantCode, stall.Facility?.Code, month.Year, month.Month, ct);
        return Result<bool>.Success(true);
    }
}
