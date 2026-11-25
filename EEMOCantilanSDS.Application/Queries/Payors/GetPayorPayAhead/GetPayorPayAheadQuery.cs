using EEMOCantilanSDS.Application.Command.OnlinePayments.Initiate;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Dtos.Payors;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Queries.Payors.GetPayorPayAhead;

public record GetPayorPayAheadQuery : IRequest<Result<IReadOnlyList<PayorPayAheadDto>>>;

/// <summary>The payor's own spaces whose rent can be paid online, each with what the office lets it pay ahead now (<see cref="BillingRules.PayAheadAsync"/>).</summary>
public class GetPayorPayAheadQueryHandler(IAppDbContext db, IStallRepository stalls, BillingRules billing, ICurrentUserService currentUser, IClock clock)
    : IRequestHandler<GetPayorPayAheadQuery, Result<IReadOnlyList<PayorPayAheadDto>>>
{
    public async Task<Result<IReadOnlyList<PayorPayAheadDto>>> Handle(GetPayorPayAheadQuery request, CancellationToken ct)
    {
        if (currentUser.UserId is not { } payorId)
            return Result<IReadOnlyList<PayorPayAheadDto>>.Unauthorized();

        var today = clock.PhilippineToday;
        var stallIds = await db.PayorStallLinks.AsNoTracking().Where(l => l.PayorUserId == payorId).Select(l => l.StallId).ToListAsync(ct);
        var result = new List<PayorPayAheadDto>();
        foreach (var id in stallIds)
        {
            var stall = await stalls.GetByIdAsync(id, ct);
            if (stall?.Facility is null || !InitiateOnlinePaymentCommandHandler.IsOnlineStallRent(stall.Facility, PayorPayableKind.Monthly)) continue;
            var paidAhead = (await db.PaymentRecords.AsNoTracking()
                .Where(r => r.StallId == id && r.Status == PaymentStatus.Paid
                    && (r.BillingYear > today.Year || r.BillingYear == today.Year && r.BillingMonth > today.Month))
                .OrderBy(r => r.BillingYear).ThenBy(r => r.BillingMonth)
                .ToListAsync(ct)).Select(r => new PayAheadMonthDto(r.BillingYear, r.BillingMonth, r.AmountPaid)).ToList();
            result.Add(new(stall.Id, stall.StallNo, stall.Facility.Code, stall.Facility.Name,
                string.IsNullOrWhiteSpace(stall.Facility.ShortName) ? null : stall.Facility.ShortName, await billing.PayAheadAsync(stall, ct), paidAhead));
        }
        return Result<IReadOnlyList<PayorPayAheadDto>>.Success(result);
    }
}
