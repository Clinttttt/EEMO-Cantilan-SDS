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

            return new FeeRateSnapshot(entries, sectionEntries);
        }
    }
}
