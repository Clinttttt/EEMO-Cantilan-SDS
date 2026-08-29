using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Facilities
{
    /// <summary>
    /// Whether stalls in one of the office's OWN market sections are metered, as a DEFAULT for a stall being recorded
    /// there.
    ///
    /// <para>
    /// A default and not a rule, deliberately. The meters belong to the space, not to the section it trades in: a section
    /// may be wired throughout while one stall in it has no connection, and a stall already carrying electricity keeps it
    /// when a clerk corrects its section. This row only says what a NEW stall in that section usually has, so the clerk
    /// is not ticking the same two boxes for every space in a wired row. What is billed remains the stall's own
    /// applicable fees and its own metered bill.
    /// </para>
    ///
    /// <para>
    /// Undated, unlike <see cref="FacilitySectionRate"/>, and for the same reason that one is dated: a rate is money owed
    /// for a day and must never move retroactively, while this is a form default that bills nothing and has no history
    /// worth keeping.
    /// </para>
    /// </summary>
    public class FacilitySectionUtilities : AuditableEntity, IMunicipalityOwned
    {
        /// <inheritdoc />
        public Guid MunicipalityId { get; private set; }

        public FacilityCode FacilityCode { get; private set; }

        /// <summary>The office's own name for the section, trimmed. Matched case-insensitively, as section names are.</summary>
        public string SectionName { get; private set; } = string.Empty;

        public bool Electricity { get; private set; }
        public bool Water { get; private set; }

        private FacilitySectionUtilities() { }

        public void Set(bool electricity, bool water, string updatedBy = "System")
        {
            Electricity = electricity;
            Water = water;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }

        public static FacilitySectionUtilities Create(
            FacilityCode facilityCode,
            string sectionName,
            bool electricity,
            bool water,
            Guid municipalityId = default,
            string createdBy = "System")
        {
            var name = (sectionName ?? string.Empty).Trim();
            if (name.Length == 0)
                throw new ArgumentException("A section's utilities must name the section.", nameof(sectionName));

            return new FacilitySectionUtilities
            {
                Id = Guid.NewGuid(),
                MunicipalityId = municipalityId,
                FacilityCode = facilityCode,
                SectionName = name,
                Electricity = electricity,
                Water = water,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy
            };
        }
    }
}
