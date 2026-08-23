using System;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Fees
{
    /// <summary>
    /// The daily fee to bill for ONE market stall, stated in one place.
    ///
    /// <para>
    /// An office may price the areas of its market apart — vegetables at ₱35 while fish stays at ₱30 — and the office
    /// that does not simply states one rate for the whole market. Both are the same question asked of one stall, so it
    /// is answered here rather than at each of the twenty places that bill, settle, import or report a daily fee.
    /// </para>
    ///
    /// <para>
    /// The order is fixed and each step is somebody's ordinance:
    /// </para>
    /// <list type="number">
    ///   <item>A stall in a market's OWN area is let at its own rate, recorded when the stall was registered.</item>
    ///   <item>Otherwise the rate the office stated for that stall's area, if it stated one.</item>
    ///   <item>Otherwise the rate the office stated for the market.</item>
    ///   <item>Otherwise nothing: the office has stated no daily rate, and nothing may be charged.</item>
    /// </list>
    ///
    /// <para>
    /// An office that states one market rate resolves through step 3 for every stall, which is exactly what happened
    /// before this rule existed — Cantilan has no per-area rows and its every figure is unchanged. A canonical stall's
    /// own <see cref="Stall.DailyRate"/> is still ignored, as it always was: an area's price belongs to the ordinance,
    /// not to a row somebody typed against one stall.
    /// </para>
    /// </summary>
    public static class NpmDailyFee
    {
        /// <summary>
        /// The stated daily fee for this stall as of a date, or null where the office has stated none. Anything that
        /// creates a charge asks this and refuses on null, rather than billing a figure the office never set.
        /// </summary>
        public static decimal? ForStallOrNull(Stall stall, FeeRateSnapshot snapshot, DateOnly asOf)
        {
            ArgumentNullException.ThrowIfNull(stall);
            ArgumentNullException.ThrowIfNull(snapshot);

            // 1) A market's own area: the stall carries the rate it was let at.
            if (stall.IsCustomSection && stall.DailyRate is { } own && own > 0m)
                return own;

            // 2) The office's rate for this stall's area, where it prices its areas apart.
            if (stall.Section is { } section
                && FacilityRateKeys.PerAreaDailyKey(section) is { } areaKey
                && snapshot.ResolveOrNull(areaKey, asOf) is { } areaRate)
                return areaRate;

            // 3) The office's rate for the market.
            return snapshot.ResolveOrNull(FeeRateKey.NpmDailyStall, asOf);
        }

        /// <summary>
        /// The stated daily fee for this stall, or zero where the office has stated none.
        ///
        /// <para>
        /// Zero, and deliberately not a constant: a constant here would be the reference municipality's ordinance
        /// charged to somebody else. Callers that must not proceed on nothing use <see cref="ForStallOrNull"/>.
        /// </para>
        /// </summary>
        public static decimal ForStall(Stall stall, FeeRateSnapshot snapshot, DateOnly asOf)
            => ForStallOrNull(stall, snapshot, asOf) ?? 0m;

        /// <summary>
        /// True when the office has stated SOME daily rate as of a date — for its market, or for at least one of its
        /// areas. For the gates that run before any particular stall is in hand: an office that prices only by area has
        /// stated a daily rate, and refusing its import because no market-wide row exists would be wrong. Each stall
        /// still resolves its own fee, and a stall whose area is unpriced is refused there.
        /// </summary>
        public static bool AnyStated(FeeRateSnapshot snapshot, DateOnly asOf)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            if (snapshot.ResolveOrNull(FeeRateKey.NpmDailyStall, asOf) is not null) return true;

            foreach (var section in Enum.GetValues<MarketSection>())
            {
                if (FacilityRateKeys.PerAreaDailyKey(section) is { } key
                    && snapshot.ResolveOrNull(key, asOf) is not null)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// The office's stated daily rate for one collection area, or the market's where it prices that area no
        /// differently. For the screens and reports that state an area's rate without a stall in hand.
        /// </summary>
        public static decimal? ForAreaOrNull(MarketSection section, FeeRateSnapshot snapshot, DateOnly asOf)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            if (FacilityRateKeys.PerAreaDailyKey(section) is { } areaKey
                && snapshot.ResolveOrNull(areaKey, asOf) is { } areaRate)
                return areaRate;

            return snapshot.ResolveOrNull(FeeRateKey.NpmDailyStall, asOf);
        }
    }
}
