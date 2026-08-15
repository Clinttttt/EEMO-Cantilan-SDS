using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Authorization
{
    /// <summary>
    /// Whether the caller may settle market collections at all.
    ///
    /// <para>
    /// One rule, one place. It was written out twice — in the handler that settles a chosen set of DAYS and in the one that
    /// settles a whole MONTH — and an authorisation rule kept in two copies is one that eventually gets fixed in one of them. It
    /// decides who may record that the office received money, so the two must never drift apart.
    /// </para>
    ///
    /// <para>
    /// The rule itself: an administrator may settle. A COLLECTOR may settle only where they are assigned, which for daily
    /// settlement means the market — the same restriction that applies to recording a single day's collection, because settling
    /// a month is only that done repeatedly.
    /// </para>
    /// </summary>
    public static class NpmSettlementAccess
    {
        /// <summary>
        /// True when this caller may settle market collections. Callers that are not collectors are permitted here and are
        /// governed by their endpoint's own authorization.
        /// </summary>
        public static async Task<bool> MaySettleMarketCollectionsAsync(
            ICurrentUserService currentUser,
            ICollectorRepository collectors,
            CancellationToken ct)
        {
            if (currentUser.Role != "Collector") return true;

            // A collector session with no collector id is not a collector we can check, so it is refused rather than trusted.
            if (currentUser.CollectorId is not { } actingCollectorId) return false;

            var collector = await collectors.GetByIdAsync(actingCollectorId, ct);
            return collector is not null
                && collector.FacilityAssignments.Any(a => a.FacilityCode == FacilityCode.NPM);
        }
    }
}
