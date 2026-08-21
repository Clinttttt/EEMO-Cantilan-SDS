using System;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Domain.Entities.TaboanMarket
{
    /// <summary>
    /// The weekday the office holds its weekly market on, from a given date onwards.
    ///
    /// <para>
    /// An office may move its market day — from a Friday to a Thursday, say — and when it does, the weeks it has
    /// already collected were held on the old day. So the day is effective-dated rather than simply overwritten,
    /// for the same reason a fee rate is: a market day that reached backwards would make every attendance the
    /// office has already recorded fall on a day its own system says was not a market day, and it would refuse
    /// the office a correction to last week's list.
    /// </para>
    ///
    /// <para>
    /// Read through <c>ITpmMarketDayProvider</c>, which answers what the day was on a given date. An office that
    /// has never moved its day has no rows here at all and is unaffected.
    /// </para>
    /// </summary>
    public class TpmMarketDaySchedule : AuditableEntity, IMunicipalityOwned
    {
        /// <inheritdoc />
        public Guid MunicipalityId { get; private set; }

        /// <summary>The weekday the market is held on from <see cref="EffectiveFrom"/> onwards.</summary>
        public DayOfWeek Day { get; private set; }

        /// <summary>
        /// The first date this weekday applies from. Never earlier than the day the office set it, so a change
        /// cannot restate a week that has already been collected.
        /// </summary>
        public DateOnly EffectiveFrom { get; private set; }

        private TpmMarketDaySchedule() { }

        public static TpmMarketDaySchedule Create(
            DayOfWeek day,
            DateOnly effectiveFrom,
            Guid municipalityId = default,
            string createdBy = "System")
        {
            return new TpmMarketDaySchedule
            {
                Id = Guid.NewGuid(),
                Day = day,
                EffectiveFrom = effectiveFrom,
                MunicipalityId = municipalityId,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy
            };
        }
    }
}
