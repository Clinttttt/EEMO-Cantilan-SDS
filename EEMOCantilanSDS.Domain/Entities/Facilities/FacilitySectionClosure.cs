using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Facilities
{
    /// <summary>
    /// One of the office's own market sections, closed: no longer offered when a stall is recorded, and gone from the
    /// market's own tabs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The office asked for closing a section to close the stalls in it as one act, knowing what that means, so this row
    /// records WHICH stalls the act closed. Without that, reopening the section would also reopen a stall the office had
    /// deliberately closed months earlier for its own reasons, and the office would never know it had happened. The stalls
    /// this closure did not touch stay exactly as they are, in both directions.
    /// </para>
    /// <para>
    /// Undated as a record of "is it closed now", with the day it happened kept for the office's own reading. It is not
    /// effective-dated like <see cref="FacilitySectionRate"/>, because a rate is money owed for a day and must never move
    /// retroactively, while a closure is a state the office is in today. What the closure DID to billing lives on each
    /// stall, where the platform already keeps it: a closed stall leaves the register, and reopening one writes its frozen
    /// span as excused so nothing back-bills.
    /// </para>
    /// </remarks>
    public class FacilitySectionClosure : AuditableEntity, IMunicipalityOwned
    {
        /// <inheritdoc />
        public Guid MunicipalityId { get; private set; }

        public FacilityCode FacilityCode { get; private set; }

        /// <summary>The office's own name for the section, trimmed. Matched case-insensitively, as section names are.</summary>
        public string SectionName { get; private set; } = string.Empty;

        /// <summary>The day the office closed it, for its own reading. The closure applies from that day forward.</summary>
        public DateOnly ClosedOn { get; private set; }

        /// <summary>
        /// The stalls THIS act closed, so reopening returns exactly those and nothing else.
        /// </summary>
        /// <remarks>
        /// A stall already closed when the section was closed is not listed, so it stays closed on reopen. Stored as a
        /// native uuid[] for the same reason a facility's section names are a text[]: one row, one list, no join table for
        /// a handful of ids.
        /// </remarks>
        public List<Guid> ClosedStallIds { get; private set; } = new();

        private FacilitySectionClosure() { }

        /// <summary>Records a fresh closure over an existing row, which is what closing a section a second time is.</summary>
        public void Reclose(DateOnly closedOn, IEnumerable<Guid> closedStallIds, string updatedBy = "System")
        {
            ClosedOn = closedOn;
            ClosedStallIds = closedStallIds?.Distinct().ToList() ?? new List<Guid>();
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }

        public static FacilitySectionClosure Create(
            FacilityCode facilityCode,
            string sectionName,
            DateOnly closedOn,
            IEnumerable<Guid> closedStallIds,
            Guid municipalityId = default,
            string createdBy = "System")
        {
            var name = (sectionName ?? string.Empty).Trim();
            if (name.Length == 0)
                throw new ArgumentException("A closure must name the section it closes.", nameof(sectionName));

            return new FacilitySectionClosure
            {
                Id = Guid.NewGuid(),
                MunicipalityId = municipalityId,
                FacilityCode = facilityCode,
                SectionName = name,
                ClosedOn = closedOn,
                ClosedStallIds = closedStallIds?.Distinct().ToList() ?? new List<Guid>(),
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy
            };
        }
    }
}
