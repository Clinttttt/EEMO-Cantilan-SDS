using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Domain.Entities.Payments
{
    /// <summary>
    /// A Head-approved weekly operating pattern for the New Public Market from <see cref="EffectiveFrom"/> onward: which
    /// weekdays the market does not open (for example Saturday and Sunday). Append-only history — a later version replaces an
    /// earlier one only from its own effective date, so no past day is ever re-read under a rule that did not exist then.
    /// A tenant with no version keeps today's behavior exactly: every day open unless an explicit closure says otherwise.
    /// </summary>
    /// <remarks>
    /// This is the NPM operating calendar only. It never closes any other facility or operation, and it records no money:
    /// a closed day is excused through the same facility-wide closure rule explicit closures already use.
    /// </remarks>
    public class NpmOperatingSchedule : AuditableEntity, IMunicipalityOwned
    {
        /// <inheritdoc />
        public Guid MunicipalityId { get; private set; }
        public DateOnly EffectiveFrom { get; private set; }

        /// <summary>One bit per <see cref="DayOfWeek"/> (bit 0 = Sunday … bit 6 = Saturday); a set bit is a weekday the market is closed. 0 = open every day.</summary>
        public int ClosedWeekdays { get; private set; }
        public string? Remarks { get; private set; }

        private NpmOperatingSchedule() { }

        public static NpmOperatingSchedule Create(DateOnly effectiveFrom, IEnumerable<DayOfWeek> closedWeekdays, string? remarks, string createdBy)
        {
            var mask = 0;
            foreach (var day in closedWeekdays.Distinct()) mask |= 1 << (int)day;
            return new NpmOperatingSchedule
            {
                Id = Guid.NewGuid(),
                EffectiveFrom = effectiveFrom,
                ClosedWeekdays = mask,
                Remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim(),
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy
            };
        }

        /// <summary>Restates a version that has not yet taken effect. A version already in force is never edited.</summary>
        public void Revise(IEnumerable<DayOfWeek> closedWeekdays, string? remarks, string updatedBy)
        {
            var mask = 0;
            foreach (var day in closedWeekdays.Distinct()) mask |= 1 << (int)day;
            ClosedWeekdays = mask;
            Remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim();
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }

        public bool IsClosedOn(DayOfWeek day) => (ClosedWeekdays & (1 << (int)day)) != 0;
        public IReadOnlyList<DayOfWeek> ClosedDays => Enum.GetValues<DayOfWeek>().Where(IsClosedOn).ToList();
    }
}
