using EEMOCantilanSDS.Infrastructure.Time;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.StallHolders;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Extensions;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace EEMOCantilanSDS.Infrastructure.Repositories;

// Partial of StallRepository: contracts needing the office's attention (IContractAttentionQueries) - occupied stalls whose
// term has expired or is about to. Asked as of a stated date, never of "now", so a report for a past month answers for that
// month.
public partial class StallRepository
{
    /// <summary>
    /// Occupied stalls whose active contract is expired or expiring within <paramref name="withinMonths"/>.
    /// Expiry (= effectivity + duration years) is a domain-computed value, so the active contracts are
    /// projected then filtered in memory; expired rows sort first, then by nearest expiry.
    /// </summary>
    public async Task<IReadOnlyList<ContractAttentionDto>> GetContractAttentionAsync(int withinMonths, CancellationToken ct)
        => await GetContractAttentionAsOfCoreAsync(_clock.PhilippineToday, withinMonths, ct);

    public async Task<IReadOnlyList<ContractAttentionDto>> GetContractAttentionAsOfAsync(int year, int month, int withinMonths, CancellationToken ct)
    {
        // Snapshot reference = the LAST day of the requested period.
        var asOf = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        return await GetContractAttentionAsOfCoreAsync(asOf, withinMonths, ct);
    }

    private async Task<IReadOnlyList<ContractAttentionDto>> GetContractAttentionAsOfCoreAsync(DateOnly asOf, int withinMonths, CancellationToken ct)
    {
        var horizon = asOf.AddMonths(withinMonths);

        var rows = await _context.Stalls
            .AsNoTracking()
            .Where(s => s.Status == StallStatus.Active && s.Contracts.Any(c => c.IsActive))
            .Select(s => new
            {
                s.Id,
                s.StallNo,
                Code = s.Facility!.Code,
                Contract = s.Contracts
                    .Where(c => c.IsActive)
                    .OrderByDescending(c => c.EffectivityDate)
                    .Select(c => new { c.ActualOccupant, c.EffectivityDate, c.DurationYears })
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var attention = new List<ContractAttentionDto>();
        foreach (var s in rows)
        {
            if (s.Contract is null) continue;
            var expiry = s.Contract.EffectivityDate.AddYears(s.Contract.DurationYears);
            var expired = asOf > expiry;
            var expiringSoon = !expired && expiry <= horizon;
            if (!expired && !expiringSoon) continue;

            attention.Add(new ContractAttentionDto(
                s.Id,
                s.Code,
                s.StallNo,
                string.IsNullOrWhiteSpace(s.Contract.ActualOccupant) ? string.Empty : s.Contract.ActualOccupant,
                s.Contract.EffectivityDate,
                expiry,
                expired));
        }

        return attention
            .OrderByDescending(a => a.IsExpired)
            .ThenBy(a => a.ExpiryDate)
            .ToList();
    }
}
