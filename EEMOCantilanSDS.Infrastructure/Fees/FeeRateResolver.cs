using EEMOCantilanSDS.Domain.Enums;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Fees
{
    /// <summary>
    /// Loads the current municipality's fixed <c>FacilityRate</c> rows (already scoped by the EF global query
    /// filter) into an immutable <see cref="FeeRateSnapshot"/>. A single small read per call; callers take one
    /// snapshot and read amounts as locals. A rate this office has not stated resolves to NOTHING: the paths that
    /// would raise a charge from one refuse instead, and the screens read it as nothing charged under that head.
    /// </summary>
    public sealed class FeeRateResolver(IAppDbContext context) : IFeeRateResolver
    {
        public async Task<FeeRateSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            var entries = await context.FacilityRates
                .AsNoTracking()
                .Select(r => new FeeRateEntry(r.FacilityCode, r.RateKey, r.Amount, r.EffectiveDate))
                .ToListAsync(cancellationToken);

            // The office's own sections' rates, read the same way and in the same call: one snapshot answers every
            // daily-fee question, so no caller has to know there are two tables behind it.
            var sectionEntries = await context.FacilitySectionRates
                .AsNoTracking()
                .Select(r => new FeeSectionRateEntry(r.FacilityCode, r.SectionName, r.Amount, r.EffectiveDate))
                .ToListAsync(cancellationToken);

            // How this office measures a market month, stated on its own market facility row. Read in the same call as the
            // rates because every path that bills already asks this snapshot: a basis fetched separately would be a second
            // read that some caller eventually forgets, and a month measured two ways is the fault this platform has been
            // bitten by before. An office with no market row keeps the default, which is the rule it has always had.
            var monthBasis = await context.Facilities
                .AsNoTracking()
                .Where(f => f.Code == FacilityCode.NPM)
                .Select(f => (NpmMonthBasis?)f.MonthBasis)
                .FirstOrDefaultAsync(cancellationToken) ?? NpmMonthBasis.RentGoal;

            return new FeeRateSnapshot(entries, sectionEntries, monthBasis);
        }
    }
}
