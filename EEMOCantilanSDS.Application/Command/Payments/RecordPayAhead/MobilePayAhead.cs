using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Payments.RecordPayAhead;

/// <summary>
/// Rent taken ahead by a collector (IA-071, amended 2026-11-25): the office's own pay-ahead, the same months, counts, limit and annual permission, for a
/// space in a facility the collector is assigned to. Nothing is priced on the phone; the months and the total are the server's.
/// </summary>
public sealed record GetMobilePayAheadQuery(Guid StallId) : IRequest<Result<PayAheadOptionsDto>>;

/// <summary>The collector's pay-ahead: the office command, after the assignment check. Online only; the phone never queues it.</summary>
public sealed record RecordMobilePayAheadCommand(Guid StallId, int FirstYear, int FirstMonth, int Months, decimal ExpectedTotal, string ORNumber)
    : IRequest<Result<bool>>;

internal static class CollectorStall
{
    /// <summary>The stall, when the caller is a collector assigned to its facility; otherwise the refusal to return.</summary>
    public static async Task<(Stall? Stall, Result<T>? Refusal)> ForAsync<T>(
        Guid stallId, IStallRepository stalls, ICollectorRepository collectors, ICurrentUserService user, CancellationToken ct)
    {
        if (user.Role != "Collector" || user.CollectorId is not { } collectorId) return (null, Result<T>.Forbidden());
        var stall = await stalls.GetByIdAsync(stallId, ct);
        if (stall?.Facility is null) return (null, Result<T>.NotFound());
        var collector = await collectors.GetByIdAsync(collectorId, ct);
        if (collector is null || !collector.IsActive || !collector.FacilityAssignments.Any(a => a.FacilityCode == stall.Facility.Code))
            return (null, Result<T>.Forbidden());
        return (stall, null);
    }
}

public sealed class GetMobilePayAheadQueryHandler(
    IStallRepository stalls, ICollectorRepository collectors, ICurrentUserService user, BillingRules billing)
    : IRequestHandler<GetMobilePayAheadQuery, Result<PayAheadOptionsDto>>
{
    public async Task<Result<PayAheadOptionsDto>> Handle(GetMobilePayAheadQuery request, CancellationToken ct)
    {
        var (stall, refusal) = await CollectorStall.ForAsync<PayAheadOptionsDto>(request.StallId, stalls, collectors, user, ct);
        return refusal ?? Result<PayAheadOptionsDto>.Success(await billing.PayAheadAsync(stall!, ct));
    }
}

public sealed class RecordMobilePayAheadCommandHandler(
    IStallRepository stalls, ICollectorRepository collectors, ICurrentUserService user, IPaymentRepository payments, ISender sender)
    : IRequestHandler<RecordMobilePayAheadCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(RecordMobilePayAheadCommand request, CancellationToken ct)
    {
        var (stall, refusal) = await CollectorStall.ForAsync<bool>(request.StallId, stalls, collectors, user, ct);
        if (refusal is not null) return refusal;
        if (request.Months is < 1 or > 12 || string.IsNullOrWhiteSpace(request.ORNumber))
            return Result<bool>.Failure("Choose the months and enter the receipt number.", ResultStatus.Invalid);

        // A resend after a lost answer (a phone on a thin signal): every one of these months already paid under this same receipt is the
        // payment already recorded, so it answers success and records nothing. Any other state goes to the office rules, which refuse a change.
        var or = request.ORNumber.Trim();
        var first = new DateOnly(request.FirstYear, request.FirstMonth, 1);
        var recorded = true;
        for (var i = 0; i < request.Months && recorded; i++)
        {
            var month = first.AddMonths(i);
            recorded = await payments.GetPaymentRecordAsync(stall!.Id, month.Year, month.Month, ct) is { Status: PaymentStatus.Paid } row
                && string.Equals(row.ORNumber?.Trim(), or, StringComparison.OrdinalIgnoreCase);
        }
        if (recorded) return Result<bool>.Success(true);

        return await sender.Send(new RecordPayAheadCommand(stall!.Id, request.FirstYear, request.FirstMonth, request.Months, request.ExpectedTotal, or), ct);
    }
}
