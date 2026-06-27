using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Payors;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public class PayorRepository(AppDbContext context) : IPayorRepository
{
    public async Task<PayorUser?> GetByContactNumberAsync(string contactNumber, CancellationToken ct = default)
    {
        var normalized = contactNumber.Trim();
        return await context.PayorUsers
            .FirstOrDefaultAsync(p => p.Username == normalized, ct);
    }

    public async Task<PayorActivationCode?> GetActivationCodeAsync(string code, CancellationToken ct = default)
    {
        var normalized = code.Trim();
        return await context.PayorActivationCodes
            .FirstOrDefaultAsync(c => c.Code == normalized, ct);
    }

    public async Task<bool> ActivationCodeExistsAsync(string code, CancellationToken ct = default)
    {
        var normalized = code.Trim();
        return await context.PayorActivationCodes
            .IgnoreQueryFilters()
            .AnyAsync(c => c.Code == normalized, ct);
    }

    public async Task<bool> ActiveCodeExistsForContactOnOtherStallAsync(string contactNumber, Guid stallId, CancellationToken ct = default)
    {
        var normalized = contactNumber.Trim();
        var now = DateTime.UtcNow;
        return await context.PayorActivationCodes
            .AnyAsync(c => c.ContactNumber == normalized
                && c.StallId != stallId
                && !c.IsUsed
                && c.ExpiresAt > now, ct);
    }

    public async Task RemoveCodesForStallAsync(Guid stallId, CancellationToken ct = default)
    {
        // Hard-delete every prior code for the stall (IgnoreQueryFilters catches any soft-deleted
        // remnants too) so issuing a new one leaves exactly one record per stall.
        var existing = await context.PayorActivationCodes
            .IgnoreQueryFilters()
            .Where(c => c.StallId == stallId)
            .ToListAsync(ct);

        if (existing.Count > 0)
            context.PayorActivationCodes.RemoveRange(existing);
    }

    public async Task AddActivationCodeAsync(PayorActivationCode code, CancellationToken ct = default)
    {
        await context.PayorActivationCodes.AddAsync(code, ct);
    }

    public async Task<bool> LinkExistsAsync(Guid payorUserId, Guid stallId, CancellationToken ct = default)
    {
        return await context.PayorStallLinks
            .AnyAsync(l => l.PayorUserId == payorUserId && l.StallId == stallId, ct);
    }

    public async Task AddPayorAsync(PayorUser payor, CancellationToken ct = default)
    {
        await context.PayorUsers.AddAsync(payor, ct);
    }

    public async Task AddStallLinkAsync(PayorStallLink link, CancellationToken ct = default)
    {
        await context.PayorStallLinks.AddAsync(link, ct);
    }

    public async Task<IReadOnlyList<PayorStallBalanceDto>> GetBalancesAsync(Guid payorUserId, CancellationToken ct = default)
    {
        var stalls = await GetLinkedStallsAsync(payorUserId, ct);
        if (stalls.Count == 0)
            return Array.Empty<PayorStallBalanceDto>();

        var items = await BuildPayableItemsAsync(stalls, ct);
        var byStall = items.GroupBy(i => i.StallId).ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<PayorStallBalanceDto>();
        foreach (var stall in stalls)
        {
            byStall.TryGetValue(stall.Id, out var stallItems);
            stallItems ??= new List<PayorPayableItemDto>();
            var oldest = stallItems.OrderBy(i => i.Year).ThenBy(i => i.Month).FirstOrDefault();
            var occupant = stall.Contracts.FirstOrDefault(c => c.IsActive)?.ActualOccupant ?? "—";

            result.Add(new PayorStallBalanceDto(
                stall.Id,
                stall.StallNo,
                stall.Facility!.Code,
                occupant,
                stall.MonthlyRate,
                stallItems.Sum(i => i.BalanceDue),
                stallItems.Count,
                oldest?.Period));
        }

        return result.OrderByDescending(r => r.OutstandingBalance).ToList();
    }

    public async Task<IReadOnlyList<PayorPayableItemDto>> GetPayableItemsAsync(Guid payorUserId, CancellationToken ct = default)
    {
        var stalls = await GetLinkedStallsAsync(payorUserId, ct);
        if (stalls.Count == 0)
            return Array.Empty<PayorPayableItemDto>();

        var items = await BuildPayableItemsAsync(stalls, ct);
        return items.OrderBy(i => i.Year).ThenBy(i => i.Month).ToList();
    }

    private async Task<List<Stall>> GetLinkedStallsAsync(Guid payorUserId, CancellationToken ct)
    {
        var stallIds = await context.PayorStallLinks
            .Where(l => l.PayorUserId == payorUserId)
            .Select(l => l.StallId)
            .ToListAsync(ct);

        if (stallIds.Count == 0)
            return new List<Stall>();

        return await context.Stalls
            .Where(s => stallIds.Contains(s.Id))
            .Include(s => s.Facility)
            .Include(s => s.Contracts.Where(c => c.IsActive))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Builds the payable obligations for the given stalls: every existing unpaid/partial record
    /// (arrears) PLUS a synthesized current-month charge for monthly-rental stalls that are active,
    /// under an effective contract, and have no record yet for the current month. NPM (daily-billed)
    /// is excluded from synthesis — its obligation is day-based, handled elsewhere.
    /// </summary>
    private async Task<List<PayorPayableItemDto>> BuildPayableItemsAsync(List<Stall> stalls, CancellationToken ct)
    {
        var stallIds = stalls.Select(s => s.Id).ToList();

        // BalanceDue/PeriodKey are computed (unmapped) — materialize, then work in memory.
        var nonPaid = await context.PaymentRecords
            .Where(p => stallIds.Contains(p.StallId) && p.Status != PaymentStatus.Paid)
            .ToListAsync(ct);

        var today = PhilippineTime.Today;
        int curYear = today.Year, curMonth = today.Month;

        // Stalls that already have ANY record for the current month (paid or not) — don't synthesize for these.
        var hasCurrentMonthRecord = (await context.PaymentRecords
                .Where(p => stallIds.Contains(p.StallId) && p.BillingYear == curYear && p.BillingMonth == curMonth)
                .Select(p => p.StallId)
                .ToListAsync(ct))
            .ToHashSet();

        var monthStart = new DateOnly(curYear, curMonth, 1);
        var monthEnd = new DateOnly(curYear, curMonth, DateTime.DaysInMonth(curYear, curMonth));

        var items = new List<PayorPayableItemDto>();
        foreach (var stall in stalls)
        {
            var facility = stall.Facility!.Code;

            // 1) Existing arrears / partials (real records with a remaining balance).
            foreach (var r in nonPaid.Where(r => r.StallId == stall.Id && r.BalanceDue > 0m))
            {
                items.Add(new PayorPayableItemDto(
                    stall.Id, stall.StallNo, facility, r.BillingYear, r.BillingMonth, r.PeriodKey, r.BalanceDue));
            }

            // 2) Synthesized current-month obligation for monthly-rental facilities.
            var isMonthly = facility != FacilityCode.NPM;
            var dueThisMonth = isMonthly
                && stall.MonthlyRate > 0m
                && stall.Status == StallStatus.Active
                && !hasCurrentMonthRecord.Contains(stall.Id)
                && stall.Contracts.Any(c => c.IsActive && c.EffectivityDate <= monthEnd && monthStart <= c.ExpiryDate);

            if (dueThisMonth)
            {
                items.Add(new PayorPayableItemDto(
                    stall.Id, stall.StallNo, facility, curYear, curMonth,
                    $"{curYear:0000}-{curMonth:00}", stall.MonthlyRate));
            }
        }

        return items;
    }
}
